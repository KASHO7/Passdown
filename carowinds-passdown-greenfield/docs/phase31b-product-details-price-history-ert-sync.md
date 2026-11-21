# Phase 31B — Product Availability, Fine Print, Price History, and ERT Sync

Refinement of Phase 31 (dynamic seasons and deals). Four focused changes; no unrelated features, no
Manager workflow, no Morning Message, no release packaging. RC.10 artifacts are untouched.

- Starting commit: `36bc7b2` (Phase 31, schema 18 — verified: `Migration0018CreateOfficialReferenceDocumentsTable`
  is registered in `App.axaml.cs` and covered by the migration tests).
- Ending commit: the Phase 31B commit on `feature/passdown-redesign` (see `git log`).
- Database schema: **unchanged at 18.** No migration was needed (details are stored as rows of the
  existing `official_reference_documents` cache, keyed by new source ids).
- Personal Sync wire format: **unchanged** (`SyncSchemaVersion` 1, ERT payload schema 1, official
  price-history file format unchanged). The stored dirty-tracking content hashes are unchanged.

---

## 1. 2027 Gold Military Pass shown as "Retired"

### Root cause

The official page `https://www.sixflags.com/carowinds/military-discounts` was redesigned (verified
live 2026-11-21) to the shared `productv2-card` component. `MilitaryFirstResponderHtmlParser` only
knew the original `h3.font-medium.text-blue-dark.type-20-600` card markup, so it found **zero cards**,
and `MilitaryFirstResponderCatalogSource` still reported a **Verified** retrieval with 0 products.
Reconciliation correctly treats a verified empty result as a "successful absence check"; after two
such checks (`CatalogProductLifecycle.ConfirmedAbsenceThreshold`) both the Military Gold Pass
(`FallMilitaryGoldSP2027`) and the Military & First Responder Ticket card (`MilitaryWizard`) were
archived, and the Price History screen labelled the pass "Retired". The pass was never missing from
the official page; it was misread.

Nothing about the general Season Passes page was involved: the military pass is matched only within
its own channel and is never required to appear on `memberships-and-season-passes`.

### Fix

- `MilitaryFirstResponderHtmlParser.ParsePage` understands both layouts. Current-layout cards keep the
  **established identity** — the accesso keyword (`MilitaryFirstResponder:id:fallmilitarygoldsp2027`,
  `…:id:militarywizard`), deliberately ignoring the new card's CMS id — so catalog rows, draft
  overrides, the Phase 21A `MilitaryWizard` per-date merge and price history continue unchanged.
- **A page with official Buy Now offers but no readable card is an `Error`**, never a verified empty
  catalog, so a future layout change can no longer retire anything. An unidentifiable unreadable card
  also makes the check an Error. One unreadable card *with* a keyword is reported through
  `CatalogPayload.UnconfirmedProductKeys` ("Partially verified"): it keeps its last verified state and
  never counts as absent.
- `ProductAvailabilityClassifier` (Domain) decides what may be shown:

| Stored lifecycle | General catalog | Dedicated channel (Military / First Responder) |
|---|---|---|
| Active | Available | Available |
| PendingConfirmation (one missing check) | Availability unconfirmed | Availability unconfirmed |
| Archived by repeated absence | Retired | **Availability unconfirmed** — the ID.me-gated purchase flow cannot be checked without signing in, and absence from the public page alone is not reliable evidence |
| Archived because the source said unavailable | Retired | Retired |
| Expired (authoritative end date) | Expired | Expired |

  The Price History list shows "Season Pass · Availability unconfirmed" with the reason underneath,
  and an unconfirmed product stays listed without "Include retired products". Lifecycle/absence
  counting itself is unchanged (it still governs current pricing).
- Recovery: on the first successful refresh after updating, the existing record **reactivates under
  the same key** (`ConfirmPresent`), keeping its first-seen date and every earlier price-history interval.

### Official sources verified (2026-11-21, anonymous HTTP GET, no sign-in)

| Page | What was confirmed |
|---|---|
| `https://www.sixflags.com/carowinds/military-discounts` | "2027 Military Gold Pass", "Only $95 /ea*", Buy Now `FallMilitaryGoldSP2027`, "Exclusive season pass savings for active and retired military."; "Military & First Responder Ticket", "Starting From $40", Buy Now `MilitaryWizard`, "ID.me Verified" |
| `https://www.sixflags.com/carowinds/memberships-and-season-passes` | Silver/Gold/Prestige tier cards, banner "Hurry! Black Friday Deal Ends 11/27", each tier's "Buy during the Black Friday Deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27." |
| `https://www.sixflags.com/carowinds/black-friday-deal` | Black Friday Deal / Silver Pass Bundle cards with the same perk wording; Resorts deal "Book by November 30, 2026. Valid on select nights November 2, 2026 – December 30, 2027." |

The ID.me-gated checkout itself was **not** accessed (it requires authentication); availability
through it is therefore never asserted beyond what the public page shows.

Trimmed deterministic fixtures: `fixtures/phase31b/military-discounts.html`,
`fixtures/phase31b/memberships-and-season-passes.html` (images/SEO removed; card markup and
structured `product_v2` fields unchanged).

---

## 2. Important Details & Deadlines

### What is read

`OfficialProductDetailsReader` (Infrastructure) reads each official card's structured CMS data
(`product_v2`: description, featured benefits, taxes-and-fees disclosure, `disclaimer` and
`disclaimerModal` fine print) plus the page's own campaign banners, on the three channels that sell
the prioritized products — Season Passes & Memberships, Military & First Responders, and Promotions
(special offers + linked campaign pages). It runs on the same response the catalog source already
fetched (no extra requests). `OfficialProductTermsReader` (Domain, pure) classifies concise statements:

| Kind | Examples (official wording) |
|---|---|
| Bring-A-Friend | "Buy during the Black Friday Deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27."; "Bring-A-Friend Discount" |
| Incentive | conditional bonuses tied to buying during a campaign / with a purchase |
| Eligibility | "Exclusive season pass savings for active and retired military.", "ID.me Verified", "Must be accompanied by a valid Season Passholder." |
| Visit validity | "Visits Thru Labor Day 2027", stated date ranges |
| Purchase | "Offer available online only.", "Book by November 30, 2026.", "Up to 6 tickets" |
| Restriction | "Not valid for Knott's Scary Farm…", "Blackout dates may apply.", "Subject to availability." |
| Fees & charges | "Plus processing fee of up to $9.99 & applicable taxes.", "Haunted mazes are an additional fee." |
| General terms | card fine print without a more specific kind |

Rules that keep it honest:

- **Purchase and redemption deadlines are separate fields.** A purchase deadline comes from the
  statement itself ("Book by …") or — for "buy *during the Black Friday Deal*" — from an official
  page banner naming that same campaign ("Black Friday Deal Ends 11/27", year inferred and labelled).
  A redemption deadline comes only from use wording ("Use by 1/3/27", "valid on any public operating
  day through 01/03/2027"). Example as published: a qualifying Silver/Gold/Prestige purchase by
  **November 27, 2026** (campaign end) includes a Bring-A-Friend ticket usable through **January 3, 2027**.
  (The November 30, 2026 date on the official pages is the Camp Wilderness resort "Book by" date.)
- **Never assume all passes get the same perk.** The fine-print modal is shared by every pass card,
  including the Military Gold Pass, and lists footnoted perks (`^` Season Pass Black Friday
  Bring-A-Friend, `+` Membership Bring-A-Friend). A footnoted paragraph is attached **only to a card
  whose own benefits/description reference that marker** (`Free General Parking*`, `Admission …**`).
  The Military Gold Pass references `*`/`**` only, so it shows the Home Park restriction but **not** the
  Black Friday Bring-A-Friend perk.
- **Never invent terms.** Ordinary benefit labels ("Park Admission") and marketing sentences are not
  "important details"; a missing date stays missing; a campaign phrase without an official banner
  date gets no deadline.
- **Conflicting terms are shown, not merged**: a card's own deadline that disagrees with the banner for
  the same campaign (or two different stated deadlines for the same kind of benefit) is listed as a
  discrepancy, and the card's own wording is used for its deadline.

### Status (pure, by Eastern "today"; deadlines inclusive)

`Current` · `Expired` (use-by passed; purchase deadline passed — reported as "No longer offered with
new purchases … already-earned benefits are usable through …" when the redemption window is still
open; or no longer listed at the latest successful check) · `Previously verified` (stale: not
re-verified within 48 hours, the deals rule) · `Unverified` (the latest check listed the card but could
not read it).

### Storage and last-known-good

One reference document per channel (`product-details.season-passes.official`,
`product-details.military-first-responder.official`, `product-details.promotions.official`) in the
existing `official_reference_documents` table, written by `CatalogSourceRegistration` only on a
Verified check, merged into the previous copy:

- a failed check records the attempt only — the verified copy is never overwritten;
- unreadable cards and cards seen only on an unreachable campaign page keep their last verified
  details (flagged unverified);
- a card no longer listed is kept (status "no longer listed") for 400 days;
- **changed official terms are preserved**: the previous statements move to `PreviousTerms` (up to 5,
  with their verified window) and appear under "Earlier official terms".

### UI

Each matching Products row gets a collapsed **Important Details & Deadlines** expander (the Phase 31
"Terms and details" pattern): the next open deadline under the header, then one line per statement
(kind · status, official text, deadline line, status reason when not current), discrepancies,
freshness warning, earlier terms, provenance ("From the official Gold Tier card on the Memberships &
Season Passes page · Last verified …") and the allowlisted official page link. Display only — never
printed, never part of the draft, never triggers retrieval (the index is read from cache with the
deals panel).

Matching (`ProductImportantDetailsIndex`): Season Pass/Membership rows match the same **tier's** card on
the Season Passes page; Military rows (including the per-date Military Ticket rows) match by Buy Now
keyword within the Military channel; Promotion rows match by card id, keyword or option keyword; exact
official title is the fallback. A row never borrows another channel's terms.

---

## 3. Synchronized Price History

### Audit

| Suspected origin | Finding |
|---|---|
| Import/export identity | Sound: content-derived SHA-256 ids; own observations dropped on import by id. A relayed own observation whose local row was re-keyed (e.g. RC.9 legacy Gold Pass fold) arrives as a *different contributor* — kept, but not recognized as the same origin. |
| Observation deduplication | Exact ids deduplicated; identical-origin evidence (same contributor + sequence + state + first-observed) was not folded. |
| Product identity folding | Correct (`ImportedPassIdentityFolding`); display-only, nothing rekeyed. |
| Sale-price classification | Different app versions/sources can record the same amount with different `PriceKind` (e.g. Sale vs Catalog); these are genuinely different states and must stay distinct. |
| **Interval construction** | **Root cause.** The merge compared each span only with the *last* row. When another computer's evidence interleaved (a brief concurrent different-type observation), one period was split into alternating rows — "$99 Sale (Nov 1) · $99 Catalog (Nov 3) · $99 Sale (Nov 11)" — producing repeated first-seen-looking rows. The reappearance check also looked only at the last joined span, so another computer's span could hide a genuine reappearance. |
| UI rendering | Faithful to the merged entries. |

### Aggregation rules (`PriceHistoryTimelineMerge`, read-time only)

1. **Identical-origin evidence folds** (same contributor, contributor sequence, state and first-observed
   instant), keeping the latest confirmation and every provenance flag. With this computer's device id
   supplied, its own relayed observations fold into the local row. Distinct observations are never dropped.
2. **A span joins the most recent row with the same price state** — amount *and* price type; different
   types at the same amount, different passes, product years, membership rates and renewal categories
   (different product keys) are never conflated.
3. **No merge across an intervening verified price change**: a different-state row that started after
   the candidate row and was still confirmed after that row's last confirmation separates them. A real
   sale ($105 → $95 → $105) stays three rows; a concurrent disagreement nested inside a period does not
   split it (it is kept as its own row, labelled "Recorded while another computer's official checks
   still showed the earlier price; both observations are kept.").
4. **A contributor's own reappearance is preserved** after a gap no other evidence bridges.
5. **First Seen** = earliest first observation of the row's evidence; **Last Confirmed** = latest
   confirmation. The screen's timing note now says so.

The result depends only on the set of observations (content-only total order), so repeated two- and
three-computer synchronization and out-of-order imports give identical history. Stored
`season_price_history` rows and `synced_price_observations` are never rewritten.

---

## 4. Early Ride Time conflicts

### Root cause

ERT is not draft data: it lives in the synced `MonthlyErt` and `ErtDateOverride` entities. Their
conflict check compared a SHA-256 of the whole payload JSON — rides **in list order** plus the edit
time — so "Copperhead Strike, Mountain Gliders" vs "Mountain Gliders, Copperhead Strike" (or the same
rides saved at different times) was a conflict on first contact. In addition, the Settings conflict
panel could not open ERT conflicts at all (its detail loader only handled drafts and notes), so they
could not be resolved from the UI.

### Comparison

- `ErtRideSelection` (Domain): canonical ride key = Unicode-compatibility-normalized name with
  whitespace collapsed, case folded and typographic apostrophes/dashes/quotes unified. Equality is
  the **set** of keys (order and duplicates ignored). No official stable ride id exists for attractions,
  and **no alias is assumed**: `VerifiedAliases` is empty until an alias is verified, so "Intimidator"
  and "Intimidator 305" remain different rides.
- `SyncContentHash.ComputeMonthlyErtSemanticHash` / `ComputeErtDateOverrideSemanticHash`: state, start
  time, ride set and normalized note/reason (never the edit time or order). `SyncReconciliation.Decide`
  takes optional semantic hashes; equal meaning → `Converged`. Drafts and notes keep exact comparison.
- On convergence the **local bytes become the agreed baseline**, so each computer keeps its own ride
  order, nothing is re-exported, and a later reorder on one computer never reorders the other.
- An existing conflict is cleared on convergence **only when it is moot** (the same remote device moved
  to agreeing content, or the recorded conflicting content itself means the same as local — e.g.
  order-only conflicts recorded before this phase). A conflict holding another computer's still-different
  rides is preserved untouched.

### Editable resolution

Opening an ERT conflict shows This Device / Other Device for Early Ride Time, start time, rides (an
order-only difference is labelled "same rides, different order") and note/reason, with:

- **Keep This Device** / **Use Other Device** (existing whole-entity actions, unchanged);
- **Merge or edit rides**: a ride list prefilled with this device's rides followed by the other
  device's additional rides (one per line; commas also accepted), plus — when start time / state / note
  differ — a choice of which side's values to keep. **Save Edited Rides** writes the result as the
  resolved canonical value.

`ResolveWithEditedErtRidesAsync` exports the new revision first; only after the export succeeds does one
transaction save the ERT record, the sync state and delete the conflict. An unavailable folder or a
crash before the commit leaves the conflict and local data exactly as they were (retrying applies the
same edit; our own exported file is ignored on re-scan). Adding rides to a "No Early Ride Time" base is
refused with an explanation. Only the ERT entity is written — drafts, notes and every other field are
untouched. The edited value then synchronizes normally (`SafeRemoteUpdate` on the other computers).

---

## 5. Schema and compatibility

- SQLite schema 18, no migration; no backup step required. New reference-document rows are additive;
  an older build ignores them.
- Sync formats unchanged; older builds still read every file this build writes. An older build will
  still report order-only ERT differences as conflicts on its side until it is updated.
- Stored hashes unchanged, so existing sync state stays valid; no forced re-export.
- Preserved: Associate layout and original Copy options, Phase 31 seasons/deals, pricing and promotion
  categories, Notes, History, PDFs (details are never printed), Calendar, draft and price-history sync,
  updater, frozen release artifacts. Morning Message remains retired.

## 6. Tests

New deterministic tests (all opt-out of the network; live smoke tests remain `Category=LiveSmoke`):

| Project | File | Tests |
|---|---|---|
| Domain | `Catalog/Phase31BProductAvailabilityTests` | 7 |
| Domain | `Ert/Phase31BErtRideSelectionTests` | 12 |
| Domain | `Promotions/Phase31BProductTermsTests` | 16 |
| Application | `Catalog/Phase31BPriceHistoryMergeTests` | 9 |
| Application | `Catalog/Phase31BMilitaryAvailabilityTests` | 4 |
| Application | `PersonalSync/Phase31BErtSemanticComparisonTests` | 12 |
| Application | `ProductDetails/Phase31BProductDetailsTests` | 11 |
| Infrastructure | `Retrieval/Sources/Phase31BMilitaryAndProductDetailsTests` | 9 |
| Infrastructure | `PersonalSync/Phase31BErtSyncTests` (real SQLite, 2–3 devices) | 14 |
| Ui (mixed partition) | `Phase31BProductDetailsAndSyncUiTests` | 12 |

Coverage includes: military products available through their dedicated channel; unknown vs retired;
failed checks never retiring; temporary omissions; official perk deadlines and expired incentives;
missing and conflicting terms; duplicate and identical-origin observations; cross-device period
aggregation; equal amounts with different price types; out-of-order and repeated 2-/3-device sync;
same ERT rides in different orders (monthly and per-date, case/whitespace/edit time); genuine ERT
conflicts; editing and merging a conflict; restarting during resolution; folder unavailable during
resolution; preservation of unrelated draft data; pre-existing order-only conflicts; preservation of a
third device's genuine conflict.

Opt-in live check (excluded from the deterministic suite, `Category=LiveSmoke`):
`Retrieval/Sources/MilitaryFirstResponderCatalogSourceLiveSmokeTests` — run once on 2026-11-21 against the
official page: Verified, "2 Military/First Responder offers confirmed", keys
`MilitaryFirstResponder:id:fallmilitarygoldsp2027` ($95, "Only") and `…:id:militarywizard` ($40,
"Starting From"); the Military Gold Pass details listed its eligibility, Home Park restriction and fees
and no Black Friday Bring-A-Friend perk.

Deterministic total: **4,984** (Phase 31: 4,878; +106). Domain 396, Application 1,336, Infrastructure
1,126, Documents 471, Ui mixed 1,503, plus the five run-alone Ui classes (26 + 113 + 2 + 7 + 4) unchanged.
No existing assertion was weakened; the two existing markup tests that flagged the first draft of the
expander (nested templates in the product row; unnamed inner controls) were satisfied by fixing the XAML.

## 7. Windows validation required

Developed and tested on macOS (Release, deterministic partition). On the Windows 11 VM, verify:

1. Associate → Products: a Season Pass, a Membership, the 2027 Military Gold Pass and a Black Friday
   promotion each show **Important Details & Deadlines**; expanding is keyboard-accessible and reads with
   Narrator; the official-page link opens the default browser.
2. After **Check Online**, the 2027 Military Gold Pass is listed as available in Products and Price
   History (not "Retired"); with a stale archived record from RC.10 it reads "Availability unconfirmed"
   until the first successful check.
3. Price History on two synced Windows computers: no alternating/duplicate rows; timing note wording.
4. Settings → Personal Sync: an ERT conflict opens, shows the comparison, "Save Edited Rides" resolves
   it, and the other computer picks the edited rides up on its next sync. Same rides in different orders
   on two computers produce no conflict (OneDrive-backed sync folder).

## 8. Known limitations

- Availability through the ID.me-gated military checkout cannot be verified without authentication;
  the app only reports what the public official page shows ("Availability unconfirmed" when absent).
- A Military Gold Pass that RC.10 archived during the misread will, on reactivation, show one
  "Published again after being retired." price-history row — that row reflects what the app recorded at
  the time; history is never rewritten.
- Footnote-scoped fine print depends on the card referencing the marker in its featured benefits or
  description; the full "View All Benefits" list is an unresolved CMS reference in the page data, so a
  perk referenced only there is not attached (conservative: never assumed).
- Year-less banner dates ("Ends 11/27") are resolved to the next occurrence and labelled "year inferred".
- No verified ride aliases exist yet; alternate spellings that are not purely case/spacing/punctuation
  differences are treated as different rides (a genuine-looking conflict the user can merge/edit).
- Keep This Device / edited resolutions overwrite the single shared ERT file, as Keep This Device always
  has; a newer, unincorporated change from a third computer is surfaced by the existing "changed again"
  note before resolving.
