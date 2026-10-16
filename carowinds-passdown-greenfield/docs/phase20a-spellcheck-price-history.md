# Phase 20A — Spell check and Season Pass price history

Two independent workstreams. Starting point: `cdd88de` (RC.3 manifest; Phase 19B code at `97de18f`).
Neither depends on the other: a spell-check failure never touches retrieval, history or the
database, and a price-history failure never touches text editing.

Schemas after this phase: **DB 16** (one new table), Draft 4, Snapshot 2, SyncSchemaVersion 1.

---

## Part A — Spell check

### Backend decision

Avalonia 12.1.1's `TextBox` has no platform spell check (no API, no Windows hook). The Windows 11
target ships the **Windows Spell Checking API** (`ISpellCheckerFactory`/`ISpellChecker`, Windows 8+):
the same local engine Windows itself uses, with installed language packs and the user's Windows
custom dictionary. It is reached through built-in .NET COM interop — **no NuGet package and no
dictionary files were added**. On macOS/Linux development hosts there is no backend
(`UnavailableSpellingBackend`): fields work normally, without underlines.

Considered and rejected: a heavyweight rich-text editor (out of proportion), and bundling a
Hunspell-style managed library plus dictionaries (adds a dependency and ~1 MB of dictionaries while
Windows already has a better, user-maintained one).

### Architecture

| Layer | Type | Role |
|---|---|---|
| Application | `SpellCheck.ISpellingBackend` | Local engine abstraction (`IsCorrect`, `Suggest`, `AddToDictionary`) |
| Application | `SpellCheck.SpellCheckTokenizer` | Pure word splitting (positions, punctuation, skips) |
| Application | `SpellCheck.DomainSpellingTerms` | Shipped allow-list |
| Application | `SpellCheck.ISpellCheckService` / `SpellCheckService` | Allow-list → session ignore → per-word cache → backend; never throws |
| Infrastructure | `SpellCheck.WindowsSpellingBackend` | Windows COM backend (Windows-only, lazy) |
| Infrastructure | `SpellCheck.SpellingBackendFactory` | Windows backend on Windows, unavailable elsewhere |
| Desktop | `Spelling.SpellCheck` (attached `IsEnabled`) + `SpellCheckController` | Debounce, underline adorner, context menu |
| Desktop | `Spelling.SpellCheckAdorner` | Red wavy underline drawn from the TextBox presenter's own text layout |

View models know nothing about spell checking; it is opt-in per `TextBox` in XAML
(`spell:SpellCheck.IsEnabled="True"`). The composition root sets `SpellCheck.Service` once on the
interactive desktop path only (never the `--scheduled-refresh` host), inside a `try/catch`.

### Fields covered

| Screen | Field | Covered |
|---|---|---|
| Associate → Overview | Daily Feature text | Yes |
| Associate → Overview | Monthly ERT note | Yes |
| Associate → Overview | ERT override reason | Yes |
| Associate → Overview | Private buyout details | Yes |
| Associate → Products | Custom item notes | Yes |
| Associate → Notes | Operational Notes (daily, multiline) | Yes |
| Operational Notes | Note title | Yes |
| Operational Notes | Note body (multiline) | Yes |
| Associate | Attendance, pre-solds, times, gate overrides, price overrides, custom item price | No (numeric/time/price) |
| Associate | ERT ride lists (one ride per line), custom item name | No (proper names → noise) |
| Price History | Product search | No (search box) |

### Behaviour

- **Underline**: misspelled words get the familiar red wavy underline, following wrapping and
  scrolling (drawn in the adorner layer over the TextBox's text presenter).
- **Right-click / context-menu key** over a flagged word: up to five suggestions, **Ignore** (this app
  session only), **Add to dictionary** (Windows user dictionary — local, persistent, shared with other
  Windows apps, never synced), then the normal Cut/Copy/Paste. Elsewhere the normal menu opens.
- Picking a suggestion replaces the word through the normal `Text` binding, exactly like typing, so
  autosave sees it. It is skipped if it would exceed the field's `MaxLength`.
- **Non-blocking**: nothing in save, autosave, PDF, finalization, Teams copy or shutdown consults spell
  checking. A misspelling is purely visual.
- **Performance**: checks run 350 ms after the last keystroke (cancellable delay, the same pattern as
  the Associate autosave). The service caches each word's result, so a 4,000-character note only asks
  the backend about words it has not seen.
- **Failure**: any backend exception turns spell checking off for the session (fields stay fully
  editable) and logs one line with the exception type only.
- **Privacy**: entirely local/offline. No text is sent anywhere, and no user text is ever logged.

### Tokenizer rules

Checked: letter runs, with an internal apostrophe kept (`don't`, `guest’s`); hyphenated compounds are
checked part by part. Skipped: single letters; all-caps acronyms (`ERT`, `DAP`, `VIP`); anything glued
to a digit (`Fury325`, `2nd`, `6:00`, `$105.00`); URLs, e-mail addresses and dotted/slashed tokens.

### Domain allow-list

`DomainSpellingTerms.Seed` (shipped code, case-insensitive, possessive-tolerant): Carowinds,
SCarowinds, WinterFest, Passdown, accesso, FunPix, Snoopy, ERT, DAP, passholder(s), walkie(s),
Conjuring, and the ride names already present in app data (Copperhead, Intimidator, Afterburn,
Nighthawk). "Fast Lane" needs no entry (both are English words). Extend by adding a term to the seed
list, or pass extra terms to `SpellCheckService`'s constructor. Not Personal Sync.

### Known limitations

- Underlines, suggestions and "Add to dictionary" exist only on Windows (Windows backend). The
  Windows COM path could not be executed on the macOS build host → **Windows validation required**.
- Language: US English (`en-US`) first, then the Windows UI language; if neither dictionary is
  installed, spell checking is silently off.
- "Ignore" is per app session; there is no in-app user dictionary (Windows' own is used).
- Underline position is computed from Avalonia's text layout; exotic fonts/scaling should be checked
  in the Windows run.

---

## Part B — Season Pass price history

### Tracked scope (`Application.Catalog.PriceHistoryTrackingPolicy`)

The one authoritative decision (`ShouldTrackPriceHistory`/`Classify`); reconciliation, composition,
the Price History screen and the migration backfill all call it.

| Tracked | Rule |
|---|---|
| Season Passes | every `SourceCategory.SeasonPasses` product (official Season Passes page) |
| Season Pass Add-Ons | `SourceCategory.AddOns` products observed by the official **Season Pass Add-Ons** page (e.g. All Season Dining, All Season Fast Lane, All Season FunPix) |
| Military/First Responder season passes | `MilitaryFirstResponder` products whose name is a season-pass tier pass ("Military Gold Pass"), reusing `ProductDisplaySectionResolver.IsSeasonPassName` |

| Not tracked | Why |
|---|---|
| Daily admission, dated Fast Lane, dated haunted/SCarowinds/Conjuring, dated Bring-A-Friend / Duo | Date-variable prices (`DateSpecific`) vary naturally by date and demand — not "price increases". Dated storage is unchanged |
| General add-ons page | Shares the `AddOns` category but also lists single-day items ("Single Day FunPix"); ambiguous → not tracked |
| Promotions cards | Marketing cards; a pass promoted there duplicates its native Season Passes row |
| Custom products | Not official data |

A product that already has history keeps exposing it even if its latest observation came from an
untracked page (the same official add-on id is listed on both add-on pages) — `ClassifyStoredProduct`.

### Data model — `season_price_history` (migration 16)

One row per **interval** of an advertised state:

| Column | Meaning |
|---|---|
| `product_key` | stable `CatalogProductKey` text (official id > URL > source+category+name) |
| `sequence` | 1, 2, 3… per product; `UNIQUE(product_key, sequence)` |
| `amount`, `price_kind` | the state — amount **and** `PriceKind` (StandardCatalog / StartingFrom / Sale / Bundle) |
| `display_price`, `price_qualifier` | the source's own text at first observation (reconstruction) |
| `display_name`, `source_name` | name/source at first observation (metadata only; never identity) |
| `first_observed_at_utc` | first successful official check that showed this state |
| `last_confirmed_at_utc` | most recent successful check that still showed it |
| `origin` | `Observed` (Phase 20A+) or `Backfilled` (migration, below) |

Changes (`+$6.00`, `+6.1%`) are **derived** from adjacent intervals (`Domain.Catalog.SeasonPriceChange`),
never stored. The model supports a future chart without schema change.

### Rules (`Domain.Catalog.SeasonPriceTimeline`)

- **Equality** includes the kind: `$99.00` ≠ `From $99.00`. `Sale` is a distinct kind (a comparison
  "was" price appeared or disappeared).
- **Same state** → only `last_confirmed_at_utc` of the newest interval moves (never backwards). Repeated
  automatic or manual checks never add rows.
- **Different amount and/or kind** → a new interval. Increases and decreases are both recorded; nothing
  decides whether a change is "temporary". `$105 → $95 → $105` is three intervals.
- **Timestamps**: a change happened sometime between the previous interval's last-confirmed time and
  the new interval's first-observed time. The UI says "First seen", not "changed on".
- **No price** in an observation → no state (never `$0.00`, never null), interval untouched.
- **Failed retrieval** → reconciliation never runs; nothing written, nothing closed.
- **Missing once** (pending confirmation) → interval untouched; if it is seen again at the same price,
  the same interval continues.
- **NotPublished / retirement** (confirmed absence, explicitly unavailable, expired) → lifecycle only;
  no price event, no zero, no closed-by-null row. History stays viewable.
- **Reactivation** → same identity, same timeline; the first observation after retirement **always
  opens a new interval**, even at the same price (it became available again at a new time). The UI
  notes "Published again after being retired."
- Older rows are immutable; only the newest interval's last-confirmed time is ever updated.

### Write path, transactions and concurrency

`ProductReconciliationService` (optional `SeasonPriceHistoryRecorder`) records each tracked,
actually-observed product **inside the same `IUnitOfWork` as its catalog upsert** (and its
`product_history` append, if any). SQLite units of work are `BEGIN IMMEDIATE`, so the
read-latest/decide/write sequence is serialized across the interactive app, Check Online/Deep Refresh
and the scheduled-refresh process. If the history write fails, the whole product write rolls back
(current price stays last-known-good) and the source is reported as failed by the orchestrator. The
unique key is a final guard against duplicate intervals (tested with concurrent connections).

There is no new polling loop and no extra request: Phase 19A's comprehensive refresh, Check Online
and Deep Refresh already reconcile these catalog sources.

### Backfill and "available from"

`product_history` (since Phase 2) was written only from successful official retrievals, with the
check time, whenever a product was added or its price/qualifier/comparison/name/lifecycle changed. Its
`Active` rows are genuine price observations. Migration 16 folds them, per tracked product, with the
same timeline rules (absence/retired rows never become a price; a retirement makes the next
observation a new interval), and extends the open interval to `catalog_products.last_seen_at_utc`
when the catalog still shows that state. Backfilled rows are marked `Backfilled`. Backfilled
"last confirmed" times of closed intervals are the last recorded change row, which may be earlier
than the true last check (unchanged checks wrote no row) — conservative, never invented.

**Price history is available starting from the first tracked observation of each product** — on an
upgraded install, its first `product_history` observation; on a fresh install, its first successful
check after Phase 20A. Nothing older is inferred from screenshots, PDFs, constants or memory. The
screen shows "Price history available starting from <date>."

### UI

Associate → Products: a quiet **Season pass price history** button above the sections, and a small
**Price history** button on each tracked row only (daily products show none). It opens a read-only
overlay ("Season Price History"):

- product list (tracked products only, active by default; **Include retired products** shows retired
  ones marked "Retired"; simple name search);
- table, **oldest first** (each Change reads against the row above): First seen · Last confirmed ·
  Price · Change — e.g. `Sep 19, 2026 · Sep 21, 2026 · $105.00 · +$6.00 (+6.1%)`;
- `From $105.00` for Starting From, `$89.00 (sale price)` for Sale; a kind-only change shows `$0.00`
  plus "Price type changed: $105.00 → From $105.00.";
- percent uses the previous price; previous `$0.00` shows the amount only; tiny changes show `<0.1%`;
- one state: the row plus "No price changes recorded yet."; no state: "No official price has been
  recorded for this product yet…";
- US formatting; dates are the Eastern park date; Escape closes; focus starts in the product list.

No chart and no Dashboard card (deferred).

### Logging and diagnostics

Logged (product key, old/new amount, old/new kind): "Price history baseline created", "Price changed",
"Price semantics changed", "Price history resumed after the product was published again".
Reconfirmations are silent. Diagnostics `summary.txt` → "Season price history": tracked products with
history, recorded price states, most recent change first-seen time. Counts only.

### Personal Sync

Not synced (SyncSchemaVersion stays 1). History is derived from official retrieval; each device builds
its own from its own successful checks (and its own backfill). Gap: a device that is off during a
short-lived price will not have that interval. Accepted — the PC that runs the app all day sees what
the business needs; syncing derived official data would create merge problems for no clear gain.

### Upgrade / compatibility

Migration 16 only creates a table and inserts derived rows; nothing existing is altered or deleted.
Tested from DB 0/7/8/11/13/14/15 and with a realistic DB-15 history. **Older builds (RC.3 and earlier)
refuse a DB-16 database** (`UnsupportedSchemaVersionException`, existing Phase 7D behaviour) — do not
run an older build against an upgraded profile.

---

## Tests added

- Domain: `SeasonPriceTimelineTests` (baseline, confirm, increase, decrease, Exact↔From, both,
  reappearance, percent, zero previous, semantic-only).
- Application: `SeasonPriceHistoryReconciliationTests` (full matrix incl. temporary sale, failed
  retrieval, no price, retirement, explicitly unavailable, single miss, reactivation, rename, write
  failure, tracked/untracked families), `PriceHistoryTrackingPolicyTests`,
  `SeasonPriceHistoryServiceTests` (list incl. retired, derived changes, Associate row eligibility,
  history read failure tolerated), `SpellCheckTokenizerTests`, `SpellCheckServiceTests`, diagnostics
  formatter.
- Infrastructure: `SeasonPriceHistorySqliteTests` (round trip, duplicate sequence rejected, confirm
  forward-only, end-to-end, rollback on write failure ×2, concurrent connections → one baseline),
  `Migration0016BackfillTests`, `MigrationHardeningTests` (+DB 15 start, schema 16).
- Ui: `PriceHistoryTests` (formatting, view model states, retired access, search, error, row button,
  shell overlay, XAML load, markup keyboard/accessibility), `SpellCheckBehaviorTests` (issues, menu
  model, replacement through the binding, ignore, failing backend never blocks, coverage markup).

## Windows validation checklist (for the next RC)

Run on Windows 11 x64 with a **copy** of an existing profile (back it up first; the DB upgrades to 16).
Mark PASS / FAIL / NOT EXECUTED.

| # | Check | Expected | Status |
|---|---|---|---|
| S1 | Type "opne" in Daily Feature | Red wavy underline under the word after a short pause | NOT EXECUTED |
| S2 | Type quickly in a long Operational Note body | No lag; underlines update after pausing | NOT EXECUTED |
| S3 | Right-click the flagged word | Suggestions, Ignore, Add to dictionary, Cut/Copy/Paste | NOT EXECUTED |
| S4 | Pick a suggestion | Word replaced; draft autosaves | NOT EXECUTED |
| S5 | Type Carowinds, SCarowinds, WinterFest, ERT, DAP, Fast Lane, Conjuring, Copperhead | Not underlined | NOT EXECUTED |
| S6 | Multiline fields (Operational Notes, note body) | Underlines follow wrapping/scrolling | NOT EXECUTED |
| S7 | Leave a misspelling; Preview PDF, finalize, Copy for Teams, close the app | Nothing blocked | NOT EXECUTED |
| S8 | Time/price/attendance fields | Never underlined | NOT EXECUTED |
| S9 | 100% / 125% / 150% scaling | Underline sits under the word | NOT EXECUTED |
| P1 | Products → Season pass price history | Overlay opens; Season Passes and Season Pass Add-Ons listed | NOT EXECUTED |
| P2 | Price history on a Season Pass row | That pass selected; current price = last row | NOT EXECUTED |
| P3 | Daily admission / Fast Lane rows | No Price history button | NOT EXECUTED |
| P4 | Upgraded profile | "Available starting from" ≈ the product's first observation in this install | NOT EXECUTED |
| P5 | Price increase / decrease (as it occurs live, or by inspection of backfilled rows) | `+$x (+y%)` / `-$x (-y%)` | NOT EXECUTED |
| P6 | Exact ↔ From change (if observed) | `$0.00` + "Price type changed" note | NOT EXECUTED |
| P7 | Include retired products | Retired tracked products listed with their history | NOT EXECUTED |
| P8 | Automatic 4-hour refresh, then Check Online twice | No duplicate rows; last confirmed moves | NOT EXECUTED |
| P9 | Escape / Tab / arrow keys in the overlay | Closes / moves / selects | NOT EXECUTED |
| P10 | Diagnostics export `summary.txt` | "Season price history" counts, no prices | NOT EXECUTED |
| P11 | Scaling 100% / 125% / 150% | Table readable, no clipping | NOT EXECUTED |
