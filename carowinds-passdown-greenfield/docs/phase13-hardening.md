# Phase 13 — Hardening, Coverage, and Rename

Phase 13A (this document) is an **audit, measurement, and planning** phase only. No startup
architecture, refresh behavior, pricing model, product source, Manual Hours/Buyout UX, ERT
semantics, or branding was changed. The only artifacts added: one non-invasive profiling test, one
new documentation file (`docs/retrieval/pricing-semantics.md`), and additions to
`docs/retrieval/catalog-source-inventory.md`. Everything else below is findings + proposals for
13B–13G to execute against.

Six parallel, code-grounded investigations fed this document (each is preserved as a standalone
audit and cited by section below): startup call graph/timing, refresh-orchestration/finalization,
retrieval architecture/product identity, product-gap/pricing-model, Manual Hours+Buyout UX/ERT, and
rename/data-compatibility. Every claim below is traced to file:line evidence by at least one of
those passes, not inferred from prior-phase docs alone (several places below note where prior docs
had drifted from current code).

---

## 1–4. Startup call graph, timing, classification, network audit

### 1. Call graph (process entry → first usable Dashboard)

```
Program.Main(args)
  → CommandLineArguments.Parse (in-memory)
  → no flags → BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)
       → App.Initialize()
            → AvaloniaXamlLoader.Load(this)                  [in-memory, compiled XAML]
            → App.BuildServiceProvider()                      [App.axaml.cs:91-210]
                 → ConfigurationBuilder + appsettings.json     [real file I/O, small]
                 → AddLogging/AddConsole                       [in-memory]
                 → AddRefreshOrchestration(...)                [pure DI registration — every
                                                                 SqliteConnectionFactory/HttpClient/
                                                                 source adapter is registered lazily;
                                                                 nothing is constructed or opens I/O
                                                                 here]
                 → services.BuildServiceProvider()             [container construction only]
       → App.OnFrameworkInitializationCompleted()
            → MigrationRunner.Run()                            [BLOCKING, App.axaml.cs:66 — first
                                                                 real SQLite I/O: opens/creates the
                                                                 db file, reads PRAGMA user_version,
                                                                 applies any pending migrations]
            → resolve MainWindow, then MainViewModel            [BLOCKING — MainViewModel's ctor
                                                                 requires all 5 child VMs already
                                                                 constructed by DI]
                 → OperationsDashboardViewModel ctor: fire-and-forget LoadAsync()
                 → OperationsCalendarViewModel ctor: fire-and-forget LoadAsync()
                 → AssociateViewModel ctor: sets SelectedDate → OnSelectedDateChanged side effect
                     → fire-and-forget SwitchDateAsync() → LoadAssociateDateAsync (local-only)
                 → OperationalNotesViewModel ctor: fire-and-forget LoadAsync()
                 → PassdownHistoryViewModel ctor: fire-and-forget LoadHistoryAsync()
            → desktop.MainWindow = mainWindow                   [window assigned; Avalonia proceeds
                                                                 to render — "first pixel" begins
                                                                 here]
            → _ = StartupCatchUpRefresh.RunAsync(...)           [FIRE-AND-FORGET, never awaited —
                                                                 the only background step in the
                                                                 whole sequence]
```

Every one of the 5 child ViewModels follows the identical shape: synchronous, non-blocking
constructor + a fire-and-forget **local-only** load task. None is awaited by `MainViewModel` or
`App.axaml.cs`, so none can delay the window opening — the window can render before any of them
have populated real data (each screen has its own loading/empty state to cover the gap).
`MainViewModel.SelectedSection` defaults to `AppSection.Dashboard` (confirmed at code level, matches
`docs/phase10-operations-dashboard.md`).

Full evidence: `/private/tmp/.../scratchpad/phase13a-startup.md` §1.

### 2. Timing measurements

Measured via a new Stopwatch harness (see §34) against isolated temp-file SQLite databases, 5 runs,
on the Fedora dev host (relative numbers — not the Windows 11 x64 target):

| Component | Median | Min | Max |
|---|---|---|---|
| DI ServiceProvider build (config + logging + all registrations) | 0.16 ms | 0.14 ms | 0.36 ms |
| SQLite cold connection open (fresh temp db) | 0.16 ms | 0.15 ms | 12.37 ms* |
| MigrationRunner.Run (10 migrations, empty db) | 5.42 ms | 4.11 ms | 18.06 ms* |
| 5 child VMs + MainViewModel construction (loads not awaited) | 0.96 ms | 0.80 ms | 52.70 ms* |
| OperationsDashboardService.GetTodayAsync (empty db) | 0.17 ms | 0.16 ms | 0.31 ms |

\* outliers are first-iteration JIT/file-create warmup; steady-state is the median column.

On a normal (non-fresh) launch, `MigrationRunner.Run()` collapses to a single `PRAGMA user_version`
read (all migrations already applied) — trivially fast, not separately measured.

Everything measured sums to well under 10 ms steady-state — the composition-root/data-layer portion
of startup is very unlikely to be the perceived-slowness bottleneck. **"Time to first pixel" is
PARTIAL / not measured** — this dev environment is headless (no compositor), so actual Avalonia
window creation/first-render cost is a separate, unmeasured quantity that needs a Windows-host (or
on-device) wall-clock measurement (process start → `Window.Opened`/first `Render`) to state with
confidence. No number is fabricated for it.

### 3. Startup work classification

| Task | Classification | Basis |
|---|---|---|
| Configuration load | MUST BLOCK FIRST SHELL | ~0.16 ms combined with DI build; everything downstream depends on it |
| Logging setup | MUST BLOCK FIRST SHELL | Same synchronous call, negligible cost |
| DI ServiceProvider construction | MUST BLOCK FIRST SHELL | Required before MigrationRunner/MainWindow/MainViewModel can resolve |
| Schema guard / migrations | MUST BLOCK FIRST SHELL **as currently implemented** — could become MAY RUN AFTER FIRST SHELL, but that's a 13B architecture decision, not free today | Called synchronously before MainWindow/MainViewModel resolve; ~5.4 ms fresh-install cost, ~0 ms normal-launch cost — currently harmless, but blocking is a choice, not a law |
| Finalization recovery (`StartupCatchUpRefresh` → finalizer) | SHOULD BE LAZY — **already correctly fire-and-forget** | Runs inside the detached background task; finalize-before-refresh ordering confirmed correct |
| Scheduled-refresh catch-up (background) | SHOULD BE LAZY — **already correctly fire-and-forget**, but see §4 | Never blocks UI; can still make real HTTP calls in the background |
| Dashboard / Calendar / History / Notes / Associate local load | MAY RUN AFTER FIRST SHELL — **already implemented this way** | Fire-and-forget from each constructor; each measured well under 1 ms against an empty db |
| Source/retriever construction | SHOULD NOT RUN DURING NORMAL STARTUP — **and does not**, beyond DI registration | Only constructed if `IRefreshOrchestrator` actually reaches a due source |
| Network refresh (synchronous) | SHOULD NOT RUN DURING NORMAL STARTUP — **confirmed it never does, synchronously** | See §4 for the background nuance |

### 4. Normal-startup network audit

**Synchronously, before/during first window render: definitively no HTTP, no Playwright, no refresh
orchestration.** Every synchronous step is either pure SQLite DDL/pragma or pure object
construction; every child ViewModel's fire-and-forget load is local-only by both code and
constructor-shape (several are guarded by dedicated no-network-dependency reflection tests already
in the suite). Playwright itself was already removed from the production dependency graph in an
earlier phase (`UnavailableJobBrowserSessionFactory` stands in for it).

**In the background, via `StartupCatchUpRefresh`: real HTTP calls are normal and expected whenever a
source is due.** The decisive fact: `FreshnessPolicy.IsDue` returns `true` unconditionally when a
source has never succeeded. On a fresh install (empty `source_state`), or after any gap that leaves
a source overdue, the very first interactive launch's background catch-up task **will** make real
HTTP calls to the actual Carowinds/Six Flags endpoints within moments of the window opening — unless
a `--scheduled-refresh` process already holds the shared file lock, in which case this launch's
catch-up is skipped entirely. This is the documented, intended "missed-run catch-up" behavior (spec
§6.6), not a bug — but it means the literal answer to "does ordinary app launch ever cause outbound
network activity" is **yes, in the background, guaranteed on first run and possible on any run** —
even though the synchronous path never does. 13B should decide whether this needs any user-facing
signal (e.g. "checking for updates in the background" state) or stays silent as today.

Full evidence: `phase13a-startup.md` §2–4.

---

## 5. Refresh entry-point matrix

Exactly **five** production entry points exist — confirmed by an exhaustive grep of every caller of
`RunManualDateRefreshAsync`/`RunScheduledRefreshAsync`/`CheckAssociateDateOnlineAsync`. `SourceCategory`
has 7 values, not 8 — Season-Pass Add-Ons and General Add-Ons share one `SourceCategory.AddOns` and
one `FreshnessPolicy`, but remain two independent `source_state` rows (keyed by source id).

| Entry point | Park Hours | Dated Admission | Dated Fast Lane | Haunted | Season Passes | Add-Ons (2 source ids) | Promotions |
|---|---|---|---|---|---|---|---|
| **1. Scheduled 6 AM** (`--scheduled-refresh`) | Sched/Due, daily, rolling 7-day window | Sched/Due, same | Sched/Due, same | Sched/Due, same | Sched/Due, rolling 7-day interval | Sched/Due, rolling 14-day interval | Sched/Due, daily |
| **2. Startup catch-up** | Identical to row 1 (same runner/orchestrator call) | Identical | Identical | Identical | Identical | Identical | Identical |
| **3. Dashboard "Reload"** | Not-covered (local-only; no refresh call at all) | Not-covered | Not-covered | Not-covered | Not-covered | Not-covered | Not-covered |
| **4. Dashboard "Check Online"** (Eastern "today", always) | Always-refreshed (unconditional, no freshness gate) | Always-refreshed | Always-refreshed | Always-refreshed | **Not-covered** | **Not-covered** | **Not-covered** |
| **5. Associate "Check Online"** (`SelectedDate`) | Always-refreshed | Always-refreshed | Always-refreshed | Always-refreshed | **Not-covered** | **Not-covered** | **Not-covered** |
| **6. Any Products/catalog explicit refresh** | — | — | — | — | **Does not exist** | **Does not exist** | **Does not exist** |

Row 6 records a real, structural gap: `RunManualDateRefreshAsync` filters to `SourceScope.Dated`
only, so **neither** "Check Online" action can ever force-recheck Season Passes, Add-Ons, or
Promotions pricing — those refresh only on their 7/14-day rolling cadence (Promotions daily) via the
scheduled/catch-up path. There is no true "force ignore freshness" mode for Catalog scope anywhere
in the app today. Concurrency (`MaxConcurrency=3`), per-operation persistence (never batched),
cancellation (never corrupts completed results), and LKG-preservation-on-failure are identical
across both orchestrator methods; `source_state`/due-ness bookkeeping is updated only by the
scheduled job, never by manual "Check Online" (spec-correct — manual refresh must not change when
the next scheduled job considers a source due).

Full matrix detail, per-column caller/service/ordering/error-behavior citations:
`phase13a-refresh.md` Deliverable 1.

## 6. Proposed 13B refresh policy

1. **Give "Check Online" a real Catalog-refresh capability, or explicitly document the gap as
   permanent.** Recommend adding a new, explicitly-named `RunManualCatalogRefreshAsync()` on
   `IRefreshOrchestrator` (parallel in shape to `RunManualDateRefreshAsync`, unconditionally
   re-running every `SourceScope.Catalog` registration, no date parameter) rather than overloading
   the existing Dated method or adding a generic `force: bool` to the scheduled job. This keeps the
   bright line "which mode gates on freshness" intact: the rolling-window scheduled job must always
   stay freshness-gated (its whole contract depends on it); only the two explicit user actions
   should ever bypass it.
2. **Dashboard and Associate already share one explicit-refresh pathway — no consolidation needed.**
   Both bottom out in the identical `CheckAssociateDateOnlineAsync` → `RunManualDateRefreshAsync`
   call; the only difference (Dashboard fixed to Eastern "today", Associate to whatever date is
   selected) is a deliberate, correct UX distinction, not duplicated plumbing. If a catalog-refresh
   method is added, both should call it the same way.
3. **Startup must never gain new reach.** `StartupCatchUpRefresh` should keep calling only
   `RunScheduledRefreshAsync` (freshness-gated, no date/UI involvement) — any new catalog-refresh
   capability must stay confined to the two explicit "Check Online" surfaces.
4. **UI copy will need to distinguish two kinds of "Check Online"** once catalog refresh exists
   (date/ticket check vs. pricing/catalog check), since they'd be two different unconditional
   operations even if triggered from the same area of the screen.

Full reasoning: `phase13a-refresh.md` Deliverable 3.

## 7. Finalization hard invariants (Phase 9) — verified against code, not just docs

Pipeline: saved draft (gate 1) → eligible date (gate 2: 06:00 America/New_York the day after
`PassdownDate`) → not already finalized (gate 3) → `FinalizedPassdownFinalizationService.FinalizeAsync`
(builds the document model once, derives snapshot JSON + PDF + both hashes in memory, one atomic
insert) → immutable row → refresh may proceed.

- **Failure ordering — structurally confirmed, not just conventional.** `ScheduledRefreshHost`'s
  `catch` block around finalization `return`s before the refresh-runner line is reached.
  `StartupCatchUpRefresh` wraps both calls in one `try`, so a finalizer throw skips the refresh call
  via the same `catch`. A finalization failure genuinely blocks that invocation's refresh in both
  paths.
- **Origin (Scheduled vs. Recovery)** is a pure date comparison (`passdownDate + 1 day == today
  Eastern?`), never derived from Task Scheduler's own missed-run reporting. `Manual` has no producer
  anywhere in the codebase.
- **Multiple eligible prior dates**: all finalized in one pass, oldest-first, sharing one captured
  `FinalizedAtUtc` per pass (not re-read per date).
- **Overlap lock is genuinely shared**: both `Program.cs` (scheduled process) and `App.axaml.cs`
  (startup catch-up) construct `FileScheduledRefreshLock` against the *same* static
  `DefaultPath` — confirmed by reading both call sites, not inferred. They cannot both
  finalize/refresh concurrently.
- **Immutability**: `IFinalizedPassdownRepository` has no `Update`/`Delete`; `FinalizeAsync` re-checks
  `GetByDateAsync` before ever inserting; everything (model, snapshot, PDF, both hashes) is built in
  memory before the one insert.
- **No drift found** between `docs/phase9-finalization.md`/`docs/architecture.md` and current code
  for any claim checked.

13B must preserve every one of the above unchanged. Full citations: `phase13a-refresh.md`
Deliverable 2.

---

## 8–21. Retrieval architecture, product coverage, identity, pricing

Full architecture map (8 sources → parsers → fixtures → tests → DI → persistence), product-identity
audit, and reconciliation/removal audit now live in `docs/retrieval/catalog-source-inventory.md`'s
new **"Phase 13A audit"** section (appended, nothing prior removed) — this document does not
duplicate that inventory, only summarizes the load-bearing conclusions:

- **8 currently-implemented commercial surfaces**, all wired into DI and the refresh orchestrator:
  Park Hours, Dated Admission, Dated Fast Lane, Dated Haunted, Promotions, Season Passes,
  Season-Pass Add-Ons, General Add-Ons. Sources 1–4 share the accesso keyword-calendar parser;
  sources 5–7 share the `productv2-card` HTML parser; source 8 has its own parser for a simpler card
  shape.
- **Product identity precedence (official id > official URL > source+category+name) is implemented
  exactly as documented**, and category-scoping already correctly resolves the one real observed
  near-collision ("2027 Gold Pass" appearing under two different categories with two different
  Sanity ids, tracked as two independent rows).
- **Real, unverified collision risk (Part Q)**: the official-id key tier (`id:{normalizedId}`) has
  **no product-name component at all** — if a future product (BAF Duo, Military/First-Responder, or
  any other) ever reused an existing accesso keyword/Sanity id, it would silently overwrite that
  product's catalog row, regardless of display name. The name-fallback tier is safe (distinct names
  normalize to distinct keys). This cannot be confirmed or ruled out from current evidence — **before
  onboarding any new product family in 13C, its official id must be checked against every existing
  id in use.**
- **A secondary, opposite-direction fragility**: `Normalize` only lowercases/collapses whitespace —
  it does not strip hyphens/punctuation, so `"Bring-A-Friend"` vs. an unhyphenated rendering would
  under-merge (two rows for one real product) rather than collide.
- **Reconciliation/removal invariants all hold, each backed by a passing test**: an `Error` is
  rejected as an absence signal at both the service and Domain layers (never advances the two-strike
  absence counter); confirmed absence requires two *consecutive successful* absent observations
  before archiving; a removed/archived product reactivates unconditionally on next confirmed
  presence; LKG is preserved by construction since a failed retrieval never reaches reconciliation at
  all. One caveat honestly recorded: this pass did not independently re-open
  `SqliteDatedResultRepository`/`CatalogSourceRegistration` internals to confirm the orchestrator
  itself skips calling reconciliation on an `Error` result — only that reconciliation refuses to
  proceed if called with one.

Full citations: `docs/retrieval/catalog-source-inventory.md` "Phase 13A audit" section;
`phase13a-retrieval-architecture.md` in full.

### Product-surface gap audit (Bring-A-Friend Duo, Military/First Responder)

Both are **confirmed absent from the entire codebase** — zero hits in any fixture, adapter, or
retrieval doc, verified by direct inspection, not inferred from the Application layer.

- **BAF Duo**: a prior live investigation (2026-08-20) of `sixflags.com/carowinds/daily-tickets`
  already recorded a bespoke `"Two Friends Just"` qualifier on that page — almost certainly the Duo
  product — but that page's card was never captured into a fixture, and no adapter targets that URL
  (`DatedAdmissionSource` only queries the `SnapWizardDailyTickets` keyword; the four catalog
  adapters target different pages entirely). **Classification: source surface investigated but never
  crawled/adapted.** The 20+-day-stale investigation and the "Celebrate America" campaign naming
  both suggest this may be a temporary promotion that could have already changed or expired — **live
  reconfirmation is required before 13C builds anything**, not an assumption that the $50/$25 figures
  or the card's markup shape still hold.
- **Military/First Responder**: zero evidence anywhere — no adapter, no fixture, no doc reference, no
  name string. **Classification: a dedicated official surface that has never been discovered at
  all.** None of the six pages previously investigated surfaced it. This needs live discovery from
  scratch in 13C; nothing in the existing evidence base supports even a confident guess at its URL
  shape or eligibility-verification flow.
- Ruled out as causes for both: parser name/keyword filtering, category filtering, and
  duplicate/collision logic — all three mechanisms were read directly and none filter by product
  name or keyword.

Full evidence: `phase13a-retrieval-gaps.md` §1.

### Pricing model audit

`ObservedProduct`/`AssociateProduct` carry: amount + display string, comparison amount (numeric
only — the raw comparison display string, e.g. `"$80 Gate Price"`, is discarded at the parser
boundary), free-text `PriceQualifier`, `DateSpecific`/`ApplicableDate`. **Fully unrepresented
anywhere in the pipeline**: per-person price, a structured bundle/component flag (bundle-ness is
only nominal, via the display-name string), member/passholder-only eligibility text (parsed nowhere
— the subtitle span is outside the parser's extraction set entirely), and per-product promotional
expiry (the page-level promo-bar date is deliberately not linked to any individual product, a prior
documented decision, not an oversight). The PDF renders `PriceQualifier` as plain text with no
visual distinction between an exact price and a "Starting From" estimate; `DateSpecific` is the only
reliable machine-checkable "is this exact for the selected date" signal today, and nothing currently
branches on it for that purpose.

**New authoritative vocabulary**: `docs/retrieval/pricing-semantics.md` (new file) defines Exact
Dated Price, Starting From, Standard/Catalog Price, Sale/Current Price, Comparison/Was Price,
Per-Person Price, and Bundle Price, each cited against the actual field(s) that represent (or fail to
represent) it today, plus the retrieval → domain/persistence → presentation → PDF/History flow and
two worked examples (see next two sections). No dollar amount from either worked example was
hardcoded into any source file.

### Aug 29 2026 Bring-A-Friend regression requirement (locked for 13C)

For Saturday, August 29, 2026: exact dated-ticketing value for **Season Pass Bring-A-Friend = $39.00
(comparison $80.00)**, while the generic catalog page's Bring-A-Friend card shows **"Starting From"
$35 (comparison $80 Gate Price)** — not date-specific. Under the precedence rule (§9 below), the $39
exact-date value must always win for a passdown selected on Aug 29; the $35 catalog floor must never
be substituted for it, silently overwrite it, or be presented as if it were the exact price for that
date. Concretely, this scenario **cannot yet happen mechanically** — `CatalogProductKey`'s
category-scoping means no code path today merges a Promotions-category value with a hypothetical
future dated-ticketing value for "the same" real-world product; nothing currently produces a $39
value at all, since no adapter queries whatever accesso keyword a "Season Pass Bring-A-Friend" dated
SKU would live under. This is precisely why the precedence rule must be written down and followed
**before** 13C adds the dated source that would make the collision possible for the first time.

### Bring-A-Friend Duo product requirement (locked for 13C)

Record as a separate product identity from ordinary Bring-A-Friend: **"Celebrate America!
Bring-A-Friend Duo Offer"**, $50 for 2 friends / $25 per friend. 13C must: (1) live-reconfirm the
current markup/price/keyword on whatever page still carries it (do not assume `daily-tickets`'
"Two Friends Just" card is unchanged from the 20+-day-old investigation); (2) discover from an
official source, never invent; (3) remain a distinct product identity from ordinary BAF — verify its
official id (if any) does not collide with BAF's existing id before persisting either; (4) represent
the per-person/bundle semantics using new, distinctly-named fields (never repurposing
`ComparisonPriceAmount`, per the pricing-semantics doc's worked example 2); (5) allow normal
reconciliation removal/reactivation rules to apply if the promotion disappears/reappears, same as
any other product.

### Military / First Responder requirement (locked for 13C)

Record as a future, separate official commercial surface. 13C must discover it from scratch (no
existing evidence of any kind to build from), classify it correctly, preserve its eligibility
qualifier, preserve "Starting From" wording if that's genuinely what the source says (never invent
an exact dated price from a generic floor), and coexist with ordinary Daily Admission rather than
replacing it. Its official id (once discovered) must be checked against every existing catalog id
before persisting, per the id-tier collision risk noted above.

### Source-precedence proposal (design only)

For any product observable from more than one official surface, highest to lowest:

1. **Exact selected-date official value** (`DateSpecific = true`, `ApplicableDate` == selected date)
   — always wins.
2. **Non-date-specific catalog value** — used only when no exact-date value exists; its qualifier
   (e.g. "Starting From") must be retained and surfaced, never dropped.
3. **Last-known-good / stale value** — used only when the current attempt itself failed; stale
   status must be retained and surfaced.

Proposed product-family buckets:

| Bucket | Products | Notes |
|---|---|---|
| DateSensitive | Daily Admission, Fast Lane (dated), Haunted (dated) | Already correctly modeled |
| CatalogStable | Season Passes, Season Pass Add-Ons, General Add-Ons | Non-date-specific, rolling-interval cadence |
| Promotional | Current Promotions (Bring-A-Friend, Gold Pass sale) | Ambiguous today: uses daily cadence like DateSensitive sources but isn't `DateSpecific` — needs its own precedence semantics ("may expire without notice; treat expiration as absence") |
| BundleOffer | BAF Duo, Ticket+Meal/Ticket+Fast-Lane bundles | Needs the not-yet-existing per-person/per-unit field; whether BAF Duo is DateSensitive or Promotional needs live discovery to settle |
| EligibilityDiscount | Military/First-Responder, the unparsed "passholders and members" text on the current BAF card | 100% unimplemented today, both as a source and as a domain concept |

Ambiguity to flag explicitly: "Season Pass Bring-A-Friend" (the name used in the Aug 29 regression
scenario) could be a Season Pass add-on (CatalogStable) or a dated checkout SKU under its own
accesso keyword (DateSensitive) — the regression's own exact-$39-for-one-Saturday framing implies
the latter, which today's `DatedAdmissionSource` does not query (it only requests
`SnapWizardDailyTickets`). This is the concrete mechanism 13C must resolve.

### Fixture strategy for 13C (proposal)

Existing convention: one directory per source family under `fixtures/`, each holding raw
captured-page/response fixtures (`published-page.html`/`published-window.json` etc.) plus edge-case
fixtures (malformed, empty, duplicate-card, missing-service). Recommend for 13C, **created only when
the corresponding adapter/parser/test actually needs them** (per CLAUDE.md — do not create empty
directories for planning):

- `fixtures/bring-a-friend/` (or reuse `fixtures/promotions/` if the Duo card turns out to live on
  the same page already fetched there) — a raw, unmodified capture of whatever page currently shows
  the Duo offer (`daily-tickets` or otherwise), captured fresh at 13C implementation time, not
  reused from the 13A-era stale investigation notes.
- `fixtures/military-first-responder/` — created only once the official surface is actually
  discovered and an adapter is being built against it.

Naming convention already established and should continue: `published-page.html`/`published-*.json`
for the happy path, plus targeted edge-case files (`no-products-page.html`, `malformed-card.html`,
`duplicate-card-page.html`) mirroring the shape of the sibling source families. Every fixture must be
a genuine, unmodified (or minimally-trimmed-for-size) capture of real official content — never
hand-authored markup — per the existing provenance convention visible in every current fixture
family's own doc comments. Exact-dated BAF test data (the Aug 29 $39/$80 scenario) should be
represented as its own fixture entry distinct from the generic catalog's $35/$80 entry, so a
regression test can assert the precedence rule directly against two real, differently-shaped
fixtures rather than one hand-edited pair.

---

## 22–26. Manual Hours / Private Buyout UX and ERT

### Current Manual Hours / Private Buyout control

Both use Avalonia's stock `TimePicker` (Avalonia 12.1.1 — no third-party control package anywhere in
the Desktop project) as two **fully independent, hand-duplicated** pairs of controls. Worst
usability problem: `TimePicker` is a click-and-spin flyout with **no keyboard-typing path at all** —
an associate cannot type "9:00 AM," they must operate three spinner columns (hour/minute/AM-PM) per
value, ~4 pointer interactions × 2 (start+end) × 2 (Manual + Buyout, if both are set). The
"closing ≤ opening" check is a **soft warning-text-only** guard — it never blocks `CanCreatePdf` or
autosave, so an invalid range can be saved and printed silently today. Persistence is already clean:
`TimeOnly?` (no date component) end to end, converted from the control's native `TimeSpan?` only at
the ViewModel boundary. Manual Hours and Private Buyout are two separately-written, structurally
identical implementations (own enabled-flag, own two time properties, own near-duplicate warning
logic, own XAML block) — a real duplication-risk finding for 13D to consolidate.

### Proposed 13D interaction model

Replace both `TimePicker` pairs with one shared, reusable `TimeRangeEntry` control built from stock
Avalonia primitives (`TextBox` + a custom parsing `IValueConverter`/behavior — Avalonia has no
built-in typable time control besides the spinner-flyout `TimePicker`, confirmed by a package audit,
so no new NuGet dependency is needed): free typing of "9", "900", "9:00a", "9:00 AM" (case-
insensitive), normalized and echoed back on blur/Tab so the associate gets immediate confirmation of
what was captured; promote the existing soft warning to a **blocking inline error** (red border +
message directly under the field) mirroring the pattern the numeric Attendance/Pre-solds fields on
this same screen already use, rather than inventing a new validation idiom; keep `TimeOnly?`/no date
component (already correct). As a plain `TextBox`, this inherits normal Avalonia DPI-aware layout
scaling for free — sidestepping `TimePicker`'s fixed-size spinner-column risk at 125/150% Windows
scaling.

### Current ERT semantics

**There is no structured ERT time field today at all** — `EarlyRideTimeDetails(string? Details,
IReadOnlyList<string> ParticipatingAttractions)` is pure free text; the placeholder literally
invites typing a time range into `Details`. "Enabled"/no-ERT is a **derived** boolean (non-blank
`Details` OR non-empty `ParticipatingAttractions`), independently re-implemented in three places
(ViewModel, Dashboard summary, PDF builder) — a third instance of the same duplication pattern found
in Manual Hours/Buyout. History freezes ERT as the same already-resolved display string, never a raw
time. `AssociateDraftPayload`'s schema (version 3) has never changed shape for ERT.

### Proposed 13E semantics

ERT start = effective park opening − 30 minutes, identical to North Gate opening
(`EffectiveHours.NorthGateOpening` already computes exactly this value for whatever opening
`ParkHoursCalculator` resolved — official, manual override, or private buyout). This is a pure
*addition* of a new presentation-only derived value; nothing needs to change on
`EarlyRideTimeDetails` itself, so **no schema version bump and no draft-migration code is needed**.
An old draft's manually-typed `Details` text keeps rendering unchanged alongside the new computed
value.

**Ambiguous cases flagged for the product owner, not resolved here:**

- **Private Buyout days** mechanically produce a derivable ERT time (buyout opening − 30 min), but
  whether Early Ride Time — a public dated-ticket-holder benefit — should even be offered as a
  concept on a closed-to-the-public buyout day is a business question the code cannot answer.
- **NotPublished / Unavailable park-hours states**: no effective opening exists to derive from
  (same bucket as "not checked yet" today) — should the UI explicitly say "cannot compute ERT — hours
  not published yet," or silently stay blank as it effectively does now?
- **ERT enabled via a typed note but zero rides selected**: today's derivation rule ORs "has
  Details" with "has attractions" for the enabled flag — should a derived start time still appear in
  that case, or only once at least one attraction is selected?

### Compatibility

Because this is additive, not a reinterpretation of an existing field, and because
`FinalizedEarlyRideTimeSnapshot` already freezes ERT as an already-resolved display string (never a
raw time) inside an insert-only, uniquely-indexed, immutable repository, **already-finalized History
snapshots cannot be affected by this change under any circumstance** — there is no write path
capable of touching an existing row, and there would be nothing for a hypothetical "redo the
derivation" pass to even operate on.

Full citations: `phase13a-uxaudit.md` Parts A and B in full.

---

## 27–30. Rename impact and data-compatible plan

### Important context: the app is already partway renamed

The literal window title, brand band, and PDF header **already read "Carowinds Admissions
Operations"**, not "Carowinds Passdown" — an earlier phase's retheme changed the visible UI text
without a corresponding full rename elsewhere. `docs/release-strategy.md` still asserts (now stale)
that the displayed name "is already 'Carowinds Passdown'" — a pre-existing doc/code drift, flagged
here, not fixed. Net effect: the live UI is already closer to "Carowinds Operations" than to
"Carowinds Passdown," so Phase 13F's actual rename surface is smaller than it looks and is
concentrated in non-UI layers: assembly Product metadata, filesystem paths, Task Scheduler, docs,
tests, and packaging scripts.

### Classification (full table in the rename audit)

- **RENAME VISIBLY (15 items)**: window title wording (tune "Admissions Operations" → "Operations"),
  brand band, PDF header, diagnostic summary heading, diagnostic export ZIP filename, `<Product>`
  assembly metadata, release ZIP filename, tester-guide/`.cmd`/`.ps1` prose, doc prose across
  `docs/*.md`, `CLAUDE.md`/spec titles.
- **MAY RENAME (5 items, recommend deferring)**: the `.exe` binary filename (already explicitly
  deferred in `docs/release-strategy.md`), the `Carowinds.Passdown.*` namespace/project/folder names
  (~250 files — pure internal identifier, invisible to users, large mechanical diff for zero
  user-facing benefit), the `CarowindsPassdown.slnx` solution filename (used as a repo-root marker by
  several tests), Avalonia XAML resource key names (`PassdownBrandDarkColor` etc.), and the OS-temp
  folder used by the PDF launcher.
- **KEEP LEGACY — hard requirement (4 items)**: the `%LOCALAPPDATA%\Carowinds Passdown` folder name,
  the `carowinds-passdown.db` filename, the `scheduled-refresh.lock` filename, the log
  directory/naming pattern.
- **REQUIRES MIGRATION/ALIAS (1 item)**: the Windows Task Scheduler task name, `"Carowinds Passdown
  Daily Refresh"` — duplicated by hand in two places and guarded by its own sync test.

### Confirmed exact current values (pulled directly from code, not assumed)

- AppData folder: `"Carowinds Passdown"` (`AppPaths.cs`)
- Database filename: `"carowinds-passdown.db"` (`appsettings.json` / `StorageOptions.cs`)
- Lock filename: `"scheduled-refresh.lock"` (`FileScheduledRefreshLock.cs`)
- Task Scheduler task name: `"Carowinds Passdown Daily Refresh"` (`ScheduledTaskPlan.cs` and
  `ScheduledTaskStatusProvider.cs`, hand-synced, test-guarded)

### Safety of "branding-only rename, legacy paths untouched"

**Confirmed safe.** Nothing in the codebase derives behavior from these names beyond using them as
literal path/string constants — no runtime parsing of "Passdown" out of a path, no feature-flagging
on the product name. The one coupled edit required: `ReleaseVersionTests.cs` hard-asserts the exact
`<Product>` string, so a branding change there must update that test in the same commit — a required
paired edit, not a hidden risk to legacy data.

### Task Scheduler rename recommendation

**Keep the legacy task name** (`"Carowinds Passdown Daily Refresh"`). The install script's
idempotency is by exact task name; adding a detect-and-migrate-old-name step introduces a stateful
migration path whose failure modes (a locale/timing miss in detection) risk either a duplicate daily
task or a silently orphaned one — worse than the purely cosmetic mismatch of a legacy-named task
under new branding. The task name is never shown inside the app itself, only in Windows' own Task
Scheduler UI, which the product owner is not expected to open. Revisit only if real tester confusion
surfaces later, as its own separately-tested change — never bundled into the visible-rebrand commit.

### Logo requirements (documentation only — no asset created)

Recognizable at 16×16 through 256×256; suitable against both light/dark taskbars and the app's own
navy brand band; simple bold silhouette, no fine detail/gradients; no emoji (matching the existing
house rule already enforced for the PDF); reads as an internal operations tool, not a marketing mark;
not a copy of protected Carowinds/Six Flags branding — the app's existing original "ascending-track"
geometric motif is a reasonable evidentiary starting point, not a "logo" to copy verbatim. Confirmed
current palette from `App.axaml`: brand navy `#0B2A4A`/`#071B30`, header white/`#B9CBDE`, accent
`#1E5AA8` (light)/`#6FA8E0` (dark), red `#C0182D`/`#FF8A80` **reserved for warning/error only** — a
logo should not use red as a primary color. Brainstormed (undecided) easter-egg directions: a
sine-wave/roller-coaster curve, a small parabolic-trajectory motif, a hidden π/integral/orbital
shape in negative space, a faint coordinate-grid texture — whichever is chosen must stay legible at
16×16 with the easter egg invisible at that size, surfacing only on closer inspection.

Full citations: `phase13a-rename.md` in full (includes the complete ~250-file/~140-match grep
inventory).

---

## 31. Phase 13 dependency plan

```
13A  Audit / profiling / architecture           (this document — done)
 |
13B  Startup performance + refresh policy         depends on 13A's classification/matrix
 |
13C  Product coverage + pricing semantics         depends on 13A's gap/pricing/identity findings;
 |                                                 independent of 13B's internals, but should not run
 |                                                 concurrently with 13B against the same
 |                                                 RefreshOrchestrator surface to avoid merge risk
 |
13D  Manual Hours / Buyout UX                     technically independent of 13B/13C (pure UI + one
 |                                                 draft-payload consumer); kept sequential for
 |                                                 regression isolation
 |
13E  ERT simplification                           technically independent of 13D (different fields),
 |                                                 but shares the same view (AssociateView) and the
 |                                                 same duplication-cleanup opportunity 13D's audit
 |                                                 flagged — sequencing after 13D lets 13E reuse
 |                                                 whatever shared time-range-entry pattern 13D
 |                                                 establishes, if any
 |
13F  Rename + branding/logo                       deliberately last among the functional phases —
 |                                                 kept separate from retrieval/refresh debugging so a
 |                                                 rename diff never gets tangled with a behavior fix
 |
13G  Full audit + release rebuild
```

13D and 13E are confirmed technically independent of each other and of 13B/13C (different files,
different concerns) — sequential delivery is a regression-isolation choice, not a technical
requirement.

## 32. Updated difficulty/risk assessment

| Phase | Prior estimate | Revised | Why |
|---|---|---|---|
| 13B | 9/10 | **7/10** | The audit found the hard invariants (finalization ordering, overlap lock, LKG) already correct and test-covered; the actual gap (no catalog force-refresh) is a small, well-scoped addition (one new orchestrator method), not a redesign. Risk is concentrated in *not breaking* what already works, which is now precisely documented. |
| 13C | 10/10 | **10/10 — unchanged** | Two live-discovery-required product surfaces, a pricing model with multiple genuinely unrepresented semantics (per-person, bundle, eligibility), and an unverified id-collision risk that must be checked against live data before either new product is onboarded. Still the highest-uncertainty phase. |
| 13D | 6/10 | **5/10** | No Avalonia control gap — a stock `TextBox` + converter is sufficient, no new dependency. The audit fully specified the target interaction model and found the exact duplication to remove. |
| 13E | 5/10 | **3/10** | The derived value is a pure addition — no schema change, no migration, no History-compatibility work needed (confirmed unconditionally safe). Only the product-owner-facing ambiguous cases (Buyout, NotPublished) carry any real risk, and those are decisions, not engineering. |
| 13F | 6/10 | **4/10** | The app is already partway renamed in the UI; the KL/RV/MR split is fully enumerated; the one genuinely risky item (Task Scheduler name) has a clear low-risk recommendation (keep legacy). |
| 13G | 7/10 | **7/10 — unchanged** | Depends entirely on what 13B–13F actually ship; no new information from 13A changes this estimate. |

## 33. Risk register

| # | Risk | Phase | Impact | Mitigation | Regression evidence strategy |
|---|---|---|---|---|---|
| 1 | Normal startup becomes slower | 13B | User-perceived regression | Keep migrations/DI synchronous only while cheap (confirmed <10ms); measure before/after any change with the new profiling harness | Re-run `StartupProfilingTests` before/after 13B changes |
| 2 | Accidental network work on startup | 13B | Privacy/reliability | Any new catalog-refresh capability must never be reachable from `StartupCatchUpRefresh` | Constructor-reflection test asserting `StartupCatchUpRefresh`'s dependency graph is unchanged |
| 3 | Finalization ordering regression | 13B | Data integrity (History) | Do not touch `ScheduledRefreshHost`/`StartupCatchUpRefresh`'s try/catch structure | Re-run existing `ScheduledRefreshHostTests`/`Phase9EIntegrationTests` unchanged |
| 4 | "Check Online" fails to truly force refresh | 13B | Stale data shown as current | New `RunManualCatalogRefreshAsync` must skip `IFreshnessPlanner` entirely, mirroring the existing Dated method | New orchestrator test asserting no freshness-planner call for the new method |
| 5 | Too many parallel official-source requests | 13B/13C | Rate-limiting/blocking by Carowinds/Six Flags | Preserve existing `MaxConcurrency` bound; any new source must register through the same `SourceRegistration`/semaphore path | Existing concurrency tests + a new one for any added source |
| 6 | Generic price overrides exact dated price | 13C | Wrong price shown to guests | Follow the documented 3-tier precedence; never let a Promotions-category value and a future dated-BAF value merge automatically | Aug 29 worked-example fixture pair as a locked regression test once the dated BAF source exists |
| 7 | "Starting From" qualifier lost | 13C | Misrepresented price | Treat `DateSpecific` (not free-text `PriceQualifier`) as the authoritative "is this exact" signal per pricing-semantics.md | PDF row test asserting qualifier text survives unchanged for a Starting-From product |
| 8 | Product identity collision | 13C | Silent data overwrite | Check any new product's official id against every existing id before onboarding (id tier has no name component) | New `CatalogProductKeyTests` case exercising the specific id once discovered |
| 9 | Temporary promotion incorrectly removed | 13C | Lost visibility into a live offer | Two-strike confirmed-absence rule already in place; do not weaken it for new promotional products | Existing `ProductReconciliationServiceTests` two-strike coverage, extended for the new product |
| 10 | Error treated as absence | 13C | Same as #9 | Already enforced at two independent layers (service + Domain); do not bypass either guard | Existing `RequireNotError`/reconciliation rejection tests |
| 11 | Stale LKG presented as current | 13B/13C | Misleading verification status | `AssociateVerificationState.Stale`/`LastVerifiedAt` semantics already correct; preserve as-is | Existing Dashboard/Calendar staleness tests |
| 12 | Time-entry redesign changes stored semantics | 13D | Draft/History corruption | Keep `TimeOnly?`, no date component, at the persistence boundary — only the control/converter changes | Existing `AssociateDraftPayload` round-trip tests re-run unchanged |
| 13 | ERT derivation breaks old drafts | 13E | Load/crash on legacy data | Confirmed additive-only — no schema bump needed; verify no migration was skipped | `AssociateDraftPayloadSerializer` deserialization tests against a real old-shape fixture |
| 14 | Finalized History changes accidentally | 13E | Breaks the core immutability guarantee | Confirmed structurally impossible (insert-only, no update path) — still worth an explicit regression test | A test finalizing under old semantics, then changing derivation logic, and asserting the frozen snapshot is byte-identical |
| 15 | Rename breaks local-data path | 13F | Users appear to lose their data | Keep the 4 KL items exactly as-is; only branding text/metadata changes | Existing `AppPathsTests`/`LocalDataCoexistenceTests`/`PortableLifecycleTests` re-run unchanged |
| 16 | Scheduled task duplication during rename | 13F | Two daily refreshes, or zero | Keep the legacy task name (recommended) — avoids the migration step that could cause this | `ScheduledTaskPlanTests`/`ScheduledTaskStatusProviderTaskNameSyncTests` re-run unchanged |
| 17 | Icon/logo causes Windows packaging issue | 13F | Broken build/publish | Follow the documented multi-resolution `.ico` requirement; verify via a real `dotnet publish` inspection, same as prior icon/asset audits | Manual publish-output inspection, same method Phase 7A/7B already used |
| 18 | Windows scaling problems in new time controls | 13D | Unusable at 125/150% | Chosen `TextBox`-based design inherits normal DPI-aware layout (no fixed-size flyout) | Manual Windows-VM validation checklist item (no automated DPI test exists in this codebase) |

## 34. Tests / profiling code added

- `tests/Carowinds.Passdown.Ui.Tests/Startup/StartupProfilingTests.cs` (new, 1 test) — a
  Stopwatch-based harness using real production types (`App.BuildServiceProvider`,
  `SqliteConnectionFactory`, all 10 real migrations, all 5 real Application services, the real
  `MainViewModel` and its 5 real child ViewModels) against isolated temp-file SQLite databases —
  never the real per-user database. Runs in under a second, fully offline. **Recommendation: safe to
  keep for 13B as a lightweight regression/reference check, or safe to delete** — nothing else
  depends on it.
- No other test or production code was added or changed. `docs/retrieval/catalog-source-inventory.md`
  gained one appended, purely additive section (0 deletions). `docs/retrieval/pricing-semantics.md`
  is a new file. No `special-ticket-sources.md` was created — the retrieval-gap findings were folded
  into `pricing-semantics.md`'s worked examples and this document instead, to avoid duplicating the
  architecture doc's own gap section.

One unrelated, pre-existing flake was observed once during a full-suite run
(`DashboardViewTests.DashboardView_DenseSnapshot_ConstructsWithoutError`, an Avalonia-headless
XAML-population parallelism race) — passed cleanly in isolation and on immediate re-run, never
touches anything this audit added. Flagged for visibility, not fixed (out of scope, matches the
already-documented Phase 6D/12B precedent for this exact class of flake).

## 35–37. Final validation

- **Test count**: **1396/1396** passing (Domain 66 + Application 400 + Documents 96 + Infrastructure
  317 + Ui 517), up from the 1395/1395 baseline by the one new profiling test. Independently
  re-confirmed by a full `dotnet test` run after all six investigations completed (not just trusted
  from each agent's own report).
- **Build**: `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors. Independently re-confirmed.
- **Documentation changed**: `docs/phase13-hardening.md` (this file, new),
  `docs/retrieval/catalog-source-inventory.md` (appended section, 101 insertions / 0 deletions),
  `docs/retrieval/pricing-semantics.md` (new file).

## Phase 13B — Refresh-mode model, UserForced coverage, and startup catch-up hardening

Phase 13B executes against 13A's findings: it makes refresh intent explicit and typed, closes the
one real gap 13A found (catalog families never force-refreshed by either "Check Online" surface),
and audits/confirms (rather than redesigns) every invariant 13A already found correct. No parser,
pricing, or new-source-family work was done here — that remains 13C.

### Refresh-mode model (Part A)

`Carowinds.Passdown.Application.Refresh.RefreshMode` (new): `Scheduled`, `StartupCatchUp`,
`UserForced`. `IScheduledRefreshRunner.RunAsync` now takes a `RefreshMode` purely so its log line
names which caller triggered a given run — `ScheduledRefreshHost` passes `Scheduled`,
`StartupCatchUpRefresh` passes `StartupCatchUp`; both still resolve to the identical due-based
`IRefreshOrchestrator.RunScheduledRefreshAsync` call (13A already found that sharing correct — not
redesigned here). Passing `UserForced` to the runner throws `ArgumentOutOfRangeException`: that mode
never reaches this runner at all — it has its own orchestrator entry point,
`RunUserForcedRefreshAsync`, called only from `AssociatePassdownService.CheckAssociateDateOnlineAsync`.
`RefreshJobType.ManualSingleDate` was renamed to `RefreshJobType.UserForced` to match (a mechanical
rename across every caller/test — no behavior change from the rename itself).

### UserForced semantics (Part B/G) — the actual gap closed

`IRefreshOrchestrator.RunUserForcedRefreshAsync(DateOnly date, ...)` replaces the old
`RunManualDateRefreshAsync`. It now plans **every** registered source — every `SourceScope.Dated`
registration against `date`, and every `SourceScope.Catalog` registration once, unconditionally —
rather than only the dated ones. Freshness is bypassed entirely for both scopes (no
`IFreshnessPlanner` call at all, matching the previous dated-only behavior extended to catalog).
Source-state (`source_state`/due-ness bookkeeping) is still never updated by this mode — an explicit
Check Online must never change when the next scheduled/startup-catch-up job considers a source due —
but each source's own per-operation persistence (dated results, catalog products, evidence,
`LastVerifiedAt`) updates exactly as it always has, since that happens inside
`SourceRegistration.ExecuteAsync` regardless of mode. Bounded concurrency, immediate persistence,
partial-failure isolation, cancellation safety, and Error-never-becomes-absence are all inherited
unchanged from the shared `ExecuteJobAsync` — this was an extension of what already ran, not new
logic (`RefreshOrchestratorTests`: `RunUserForcedRefreshAsync_AlsoForceRefreshesEveryCatalogSource`,
`_IgnoresFreshness_EvenForJustSucceededSources`, `_PartialFailure_PreservesEachSourceIndependently`,
`_CallerCancellation_StopsOutstandingWork...`, `_DoesNotTouchSourceState`).

### Source coverage matrix (Part F/Y) — from actual composition-root registrations

| Source family | Source id(s) | Scheduled | StartupCatchUp | UserForced |
|---|---|---|---|---|
| Park Hours | `park-hours` (see `ParkHoursSource`) | due | due | **yes, always** |
| Dated Admission | `DatedAdmissionSource` | due | due | **yes, always** |
| Dated Fast Lane | `DatedFastLaneSource` | due | due | **yes, always** |
| Dated Haunted | `DatedHauntedSource` | due | due | **yes, always** |
| Season Passes | `SeasonPassCatalogSource` | due | due | **yes, always** (was omitted before 13B) |
| Season Pass Add-Ons | `SeasonPassAddOnsCatalogSource` | due | due | **yes, always** (was omitted before 13B) |
| General Add-Ons | `GeneralAddOnsCatalogSource` | due | due | **yes, always** (was omitted before 13B) |
| Promotions | `PromotionsCatalogSource` | due | due | **yes, always** (was omitted before 13B) |

`CompositionRootTests.BuildServiceProvider_SourceRegistrations_CoverEveryExpectedFamily` (new) reads
the real `IReadOnlyList<SourceRegistration>` DI registration and fails if any of these 4 dated + 4
catalog families is ever accidentally dropped — this is the "test that fails on omission" Part F asks
for, built from the actual registration list rather than a hand-maintained parallel table.

### Dashboard / Associate Check Online (Parts C/D/E) — no consolidation needed, confirmed again

Both screens already funnel through one shared Application-layer method —
`OperationsDashboardService.CheckOnlineAsync` → `AssociatePassdownService.CheckAssociateDateOnlineAsync`
(Dashboard fixed to Eastern "today"; Associate to `SelectedDate`) — so extending
`CheckAssociateDateOnlineAsync` to call the new `RunUserForcedRefreshAsync` fixed both surfaces from
one call-site change; no per-screen source-family list exists anywhere to keep in sync (Part E's
"avoid duplicate lists" requirement was already satisfied by the existing architecture, confirmed by
`AssociateCheckOnlineCatalogCoverageTests` — a real `RefreshOrchestrator`, not a fake, proving the
recomposed workspace actually reflects newly force-refreshed catalog products end to end). No draft
mutation, no bypassed reconciliation rule, no changed selected date — all unchanged from before.

### Reload Summary — confirmed local-only, unchanged (Part T)

Dashboard's "Reload" (`OperationsDashboardViewModel.ReloadCommand` → `GetTodayAsync` →
`AssociatePassdownService.LoadAssociateDateAsync`) was not touched by this phase and remains proven
local-only by the pre-existing `OperationsDashboardServiceTests` assertion that `GetTodayAsync`/`LoadAsync`
never populate `FakeRefreshOrchestrator.ManualDateRefreshCalls` — still true after the rename (the
fake's own call-recording property name is unchanged; only the interface method it backs was renamed).

### Startup catch-up policy (Part H/I) — kept as-is, deliberately

Decision: **Option A, unchanged** — `StartupCatchUpRefresh` keeps performing real overdue-source
network refresh in the background via the shared `IScheduledRefreshRunner`/`RunScheduledRefreshAsync`
due-based job, now explicitly tagged `RefreshMode.StartupCatchUp` for logging. 13A's evidence showed
this is already narrow (never reaches beyond due sources, never touches UserForced's new catalog
capability, never blocks the window, respects the same `IScheduledRefreshLock` a scheduled process
uses) — narrowing it to "finalization/recovery only" (Option B) would remove real missed-work
recovery value for no correctness gain, since due-ness gating already prevents it from over-refreshing.
First-run behavior is the same story: a fresh install's guaranteed-overdue `source_state` still causes
background network work shortly after the first window opens, exactly as 13A documented — this is
intended "missed-run catch-up" (spec §6.6), the app is usable immediately regardless, there is no UI
freeze, and it cannot duplicate work (the shared lock plus due-ness gating both apply). No behavior
changed; only the `RefreshMode` tag was added so scheduled vs. startup-catch-up runs are
distinguishable in the log (`StartupCatchUpRefreshTests.RunAsync_LockAvailable_InvokesRunnerWithStartupCatchUpMode`,
`ScheduledRefreshHostTests.RunAsync_InvokesRunnerWithScheduledMode`).

### Finalization ordering, shared lock, bounded concurrency, immediate persistence, partial failure, cancellation (Parts J/K/M/N/O/P/Q)

All confirmed unchanged and still correct — none of `ScheduledRefreshHost`'s or
`StartupCatchUpRefresh`'s try/catch/finally structure, lock-acquire-before-migrations ordering, or
`RefreshOrchestrator.ExecuteJobAsync`'s bounded-`SemaphoreSlim`/per-operation-persistence/
Error-never-absence logic was touched. Existing Phase 6/9 tests re-run unmodified and green.

### New: UserForced vs. an in-progress scheduled/startup-catch-up refresh (Parts K/L)

The one genuine new lock-related behavior this phase adds: `IScheduledRefreshLock` was relocated
from `Infrastructure.Concurrency` to `Application.Concurrency` (a pure abstraction move — the
concrete `FileScheduledRefreshLock` stays in Infrastructure, implementing the relocated interface —
mirroring the existing repository-port pattern) so `AssociatePassdownService` can depend on it
directly without Application acquiring an Infrastructure reference.
`AssociatePassdownService.CheckAssociateDateOnlineAsync` now accepts an optional
`Func<IScheduledRefreshLock>? refreshLockFactory` (default `null` = no overlap protection, matching
every pre-13B caller/test exactly, the same optional-factory pattern `ScheduledRefreshHost` already
established). The Desktop composition root supplies a real factory
(`() => new FileScheduledRefreshLock(FileScheduledRefreshLock.DefaultPath)`), so a Check Online
click that would run concurrently with an already-in-progress scheduled refresh or startup catch-up
(same file lock, same OS-level exclusivity) instead:

1. Never starts a second orchestrator run (`TryAcquire()` fails fast, non-blocking).
2. Returns a distinct `RefreshSummary.AlreadyInProgress(...)` (`WasSkippedDueToOverlap: true`,
   `OperationsAttempted: 0`) rather than a raw lock exception.
3. Still recomposes and returns whatever is currently cached locally (Part L "reload local state
   when appropriate") — a concurrently-running refresh may already be writing fresher data.
4. `RefreshMessages.Summary` reads this case as "A refresh is already in progress. Try again in a
   moment." — never the unrelated "Nothing to check for this date." (zero-operations) message, and
   never counted as a failure (`Failed`/`Cancelled` both stay `0`).

Because Dashboard and Associate share this one method, this guard also covers the case of a
Dashboard Check Online and an Associate Check Online being triggered near-simultaneously in the same
process — proven at the `AssociatePassdownService` layer (the shared call site), not duplicated per
ViewModel. Tests: `AssociatePassdownServiceTests` (`_NoLockFactory_RunsRefreshWithoutAnyGuard`,
`_LockAlreadyHeld_ReturnsAlreadyInProgress_AndNeverCallsOrchestrator`,
`_LockAlreadyHeld_StillReloadsCurrentLocalState`, `_LockAcquired_RunsRefresh_AndReleasesLockAfterward`),
`RefreshMessagesTests`, `CompositionRootTests.BuildServiceProvider_ResolvesScheduledRefreshLockFactory`.

### Calendar local-only guarantee (Part U)

Untouched by this phase; Phase 12's own `OperationsCalendarServiceTests` (no `IRefreshOrchestrator`
dependency on `OperationsCalendarService` at all) continue to pass unchanged.

### Startup/first-shell (Parts V/W/X) — no change

Dashboard remains the default startup screen; `App.OnFrameworkInitializationCompleted` still assigns
`MainWindow` before firing `StartCatchUpRefresh` as a detached, never-awaited task — unchanged. No
polling, no toast/Alerts infrastructure was added (out of scope, and unnecessary — the existing
data-health section already reflects whatever a background catch-up wrote, on next reload). The
Phase 13A `StartupProfilingTests` harness is unchanged and was re-run (see below); its existing
`UnreachableRefreshOrchestrator` fake is itself the structural proof Part X asks for — every local
load path throws if it ever reaches the orchestrator, so a passing profiling run already proves no
local-load path in this harness reaches retrieval, independent of the timing numbers.

### Performance re-check (Part AK)

Re-run on the same dev host, same methodology as 13A (5 iterations, isolated temp-file SQLite,
never the real per-user database):

| Component | 13A median | 13B median |
|---|---|---|
| DI ServiceProvider build | 0.16 ms | 0.19 ms |
| SQLite cold connection open | 0.16 ms | 0.19 ms |
| MigrationRunner.Run (10 migrations, empty db) | 5.42 ms | 4.43 ms |
| 5 child VMs + MainViewModel construction | 0.96 ms | 0.88 ms |
| OperationsDashboardService.GetTodayAsync (empty db) | 0.17 ms | 0.18 ms |

No regression — differences are within normal run-to-run noise on this dev host, and no startup
architecture changed. The structural claim ("no new synchronous network/blocking work on normal
startup") holds because nothing on the synchronous startup path was touched at all this phase.

### Schema (Part AM)

Unchanged: database schema **10**, `AssociateDraftPayload` schema **3**,
`FinalizedPassdownSnapshot` schema **1**. No migration added — every change in this phase is
refresh-orchestration/API-contract/DI-wiring, none of it persistence-shape.

### Tests added

19 new tests (1396 → 1415, all passing, `Category!=LiveSmoke`):

- `RefreshOrchestratorTests` (Application): +4 net — `RunUserForcedRefreshAsync_TargetsRequestedDatedWork_EvenOutsideRollingWindow`,
  `_AlsoForceRefreshesEveryCatalogSource`, `_IgnoresFreshness_EvenForJustSucceededSources`,
  `_DoesNotTouchSourceState`, `_PartialFailure_PreservesEachSourceIndependently`,
  `_CallerCancellation_StopsOutstandingWork_WithoutCorruptingCompletedResults` (replacing the 2
  pre-13B manual-refresh tests whose assertions were specific to the old dated-only behavior).
- `AssociateCheckOnlineCatalogCoverageTests` (new file, Application): 1 real-orchestrator end-to-end
  proof of catalog coverage through the shared Check Online path.
- `AssociatePassdownServiceTests`: +4 — the overlap-lock guard's four states (no factory, held,
  held-but-reloads-local-state, acquired-and-released).
- `ScheduledRefreshRunnerTests`: +3 — `RefreshMode.StartupCatchUp` still calls the scheduled path,
  `RefreshMode.UserForced` throws, the log line names the mode.
- `ScheduledRefreshHostTests`: +1 — invokes the runner with `RefreshMode.Scheduled`.
- `StartupCatchUpRefreshTests`: +1 — invokes the runner with `RefreshMode.StartupCatchUp`.
- `CompositionRootTests`: +2 — the source-coverage matrix guard, and the lock-factory resolves.
- `RefreshMessagesTests` (new file): +3 — the "already in progress" message is distinct, never reads
  as a failure, and the ordinary zero-operations message is unaffected.

### Final validation

- **Build**: `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
- **Tests**: `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **1415/1415**
  passing (Domain 66, Application 412, Documents 96, Infrastructure 317, Ui 524), re-run twice.
- **Known flake observed once**: an Avalonia-headless XAML-population parallelism race (this run:
  `AssociateViewTests.AssociateView_EachInternalSection_ConstructsWithoutError(section: Notes)`;
  previously documented against different tests in 13A/6D/12B) — passed cleanly on immediate re-run
  of the whole `Ui.Tests` project, unrelated to any 13B change (it is a pre-existing Avalonia
  headless-runtime parallelism issue, not a regression).

### Exact work deferred to 13C–13G

Unchanged from 13A's own deferral list below (§38) — 13B added no product/parser/pricing scope of
its own. In particular: BAF Duo, Military/First Responder, per-person/bundle pricing fields, Manual
Hours/Buyout redesign, ERT semantics, rename/branding, and the final release rebuild all remain
exactly as 13A scoped them.

## 38. Exact work deferred to 13B–13G

- Startup architecture changes (moving migrations after first shell, any lazy-loading redesign) —
  13B, using this document's classification table as the target.
- Any refresh-policy code change (new `RunManualCatalogRefreshAsync`, UI copy for two kinds of
  "Check Online") — 13B.
- Any new product source, parser, or fixture (BAF Duo, Military/First-Responder) — 13C, starting
  with live reconfirmation, not the 13A-era stale investigation notes.
- Any pricing-model field addition (per-person, bundle, eligibility, comparison-display-string
  preservation) — 13C.
- Product-identity id-collision verification against live accesso/Sanity data for any new product —
  13C, before persisting.
- Manual Hours/Private Buyout control replacement and validation hardening — 13D.
- ERT derived-time implementation and the three product-owner-ambiguous cases — 13E, pending product
  decisions on Private Buyout/NotPublished/no-rides-selected behavior.
- All branding/rename work, the Task Scheduler decision's actual implementation, and the logo asset
  itself — 13F.
- Final full-system audit and release rebuild — 13G.

No major Phase 13 behavior was implemented in 13A. Build and tests remain green.
