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

## Phase 13C — commercial coverage, pricing semantics, daily cadence

Executes against 13A/13B's findings: closes the two flagged product-coverage gaps (BAF Duo,
Military/First Responder), adds a structured price-semantics classification, and moves
Season Passes/Add-Ons to daily cadence after measuring the cost. Full narrative:
`docs/retrieval/special-ticket-sources.md` (new), `docs/retrieval/pricing-semantics.md` and
`docs/retrieval/catalog-source-inventory.md` (both appended).

### 1–3. Coverage, official surfaces, pricing model

10 commercial sources now implemented (was 8) — see the updated table in
`docs/retrieval/catalog-source-inventory.md`. Live-reconfirmed 2026-08-27:
`special-offers`, `daily-tickets`, `season-passes`, `season-pass-add-ons`, `add-ons`,
`military-discounts` (new page, found via the site's own nav tab), and the accesso
`getkeywordcalendar` endpoint for `SUBAF50` (new keyword). New `Domain.Catalog.PriceKind` enum
(`Unknown, ExactDated, StartingFrom, StandardCatalog, Sale, ComparisonWas, PerPerson, Bundle`),
computed at composition time from already-persisted fields — no schema migration.

### 4–5. Starting From / Exact Dated behavior; 6. source precedence

`PriceKind.StartingFrom`/`ExactDated` are now explicit, structured signals alongside the qualifier
text itself (which was already, and remains, preserved verbatim end to end). Precedence (exact
dated always wins for the selected date; a catalog value is never silently substituted for it) is
unchanged from 13A's design and was already true mechanically (`CatalogProductKey`'s
category-scoped identity means a dated and a catalog row for "the same" product were always two
independent rows) — this phase makes that precedence *observable* via `PriceKind` rather than
changing the underlying mechanism.

### 7. Aug 29 BAF regression result: **PARTIAL — mechanism PASS, literal production adapter DEFERRED BY DESIGN**

Live investigation found ordinary Bring-A-Friend's exact dated price is gated behind an
authenticated passholder login inside the shared `SnapWizard` checkout — not reachable by any
anonymous official request, and CLAUDE.md prohibits entering credentials. No production adapter
produces the literal $39.00/$80.00 pair. The precedence *mechanism* the regression exists to prove
is fully implemented and tested (`AssociatePassdownServicePriceSemanticsTests`, 3 tests) using the
existing generic dated/catalog composition path fed representative data shaped like the documented
manual observation — an exact dated value and a generic "Starting From" value for the same
real-world product remain two distinct, correctly-classified rows, never merged, never
cross-date-substituted. Full honest accounting in `docs/retrieval/special-ticket-sources.md`.

### 8. comparison/was price: PASS (unchanged, re-verified)

`ComparisonPriceAmount` semantics, strikethrough PDF rendering, and the existing test coverage
(`ComparisonPrice_AppearsOnlyWhenSourceBacked` etc.) were not touched. New coverage added for the
Military/First Responder "$80 Gate Price" case.

### 9. BAF Duo discovery: PASS — genuinely live, genuinely dated

"Celebrate America! Bring-A-Friend Duo" discovered live on `daily-tickets` (catalog: $50 total,
qualifier "Two Friends Just", accesso keyword `SUBAF50`) *and* independently confirmed as a real,
date-scoped `getkeywordcalendar` keyword returning $25.00/friend across 5 tested dates. Both are now
implemented: `PromotionsCatalogSource` discovers the catalog card generically (no name hardcoded —
Part O), `DatedBringAFriendDuoSource` (new) retrieves the exact per-date price under a new
`SourceCategory.BringAFriendDuo`. Distinct product identity from ordinary BAF confirmed (different
Sanity id, different accesso keyword, different category).

### 10. bundle/per-person semantics: PASS

New `AssociateProduct` fields (`BundleTotalPriceAmount`, `BundleQuantity`, `PerPersonPriceAmount`),
transient/composition-time only. `PriceSemanticsClassifier.TryParseBundle` recognizes the
structural, name-agnostic shape `"<number word> <noun> Just"` and derives quantity/per-person price
explicitly — never collapsing the bundle total. 13 dedicated unit tests
(`PriceSemanticsClassifierTests`) plus composition/PDF coverage.

### 11. Military/First Responder discovery: PASS

Live-discovered on `sixflags.com/carowinds/military-discounts` (site's own nav tab). New,
dedicated `MilitaryFirstResponderHtmlParser` (shape evidenced on exactly one page, so not merged
into either existing shared parser per the established "2+ pages" rule) and
`MilitaryFirstResponderCatalogSource`, tracked under a new `SourceCategory.MilitaryFirstResponder` —
never overwrites or is confused with `DatedAdmissionTickets`.

### 12. eligibility semantics: PASS (structural, not fabricated)

Both new eligibility-restricted families are tracked under their own dedicated
`SourceCategory` (never a shared/generic one) and keep their own display name
("Military & First Responder Tickets") as the eligibility signal. Page-level eligibility prose
(ID.me verification, "must be accompanied by a valid passholder") is deliberately **not** attached
to the per-product record — no per-card field for it exists in the live evidence, and inventing one
would violate the never-fabricate rule (spec §5.2).

### 13–14. product identity decision; collision-risk resolution: PASS

No new identity strategy was needed — 13A's existing precedence (official id > URL >
source+category+name, scoped by `SourceCategory`) already handles every new product correctly.
Every new id/keyword (`SUBAF50`, the Duo's Sanity id, `MilitaryWizard`) was checked against every
existing id in use before onboarding (table in `docs/retrieval/special-ticket-sources.md`) — no
collision found in either tier.

### 15. reconciliation/removal behavior: PASS (unchanged)

Two-strike confirmed-absence, `Error`-never-absence, and reactivation are untouched — the new
sources reuse `IProductReconciliationService`/`CatalogProductLifecycle` exactly as every existing
catalog source does.

### 16. new-deal discovery: PASS

`PromotionsCatalogSource` now fetches `daily-tickets` in addition to `special-offers`, both parsed
by the same unmodified, name-agnostic `ProductCardHtmlParser` — any future new card on either page
is discovered automatically on the next successful refresh, with no code change required. Proven by
`RetrieveAsync_FetchesBothPagesInOrder_AndDiscoversBringAFriendDuoGenerically`, which asserts the
Duo is found via the generic mechanism, not a name lookup.

### 17–18. fixture strategy; parser robustness: PASS

New fixture families: `fixtures/promotions/daily-tickets-page.html` (real capture),
`fixtures/dated-bring-a-friend-duo/` (8 files, every "published"/"horizon" fixture a real, unmodified
capture of the live `SUBAF50` response), `fixtures/military-first-responder/` (4 files, real capture
plus synthetic malformed/empty-page defensive fixtures matching existing project convention). A real
shared-plumbing gap was found and fixed: `AccessoKeywordCalendarPackageList.P` lacked the same
single-object/array normalization `DATES.D` already had — real `SUBAF50` single-package responses
surfaced it; fixed with the same `SingleOrArrayConverter<T>`, benefiting every dated source.

### 19–21. daily refresh measurements; final cadence; scheduled 6 AM behavior: PASS

Measured (`curl`, this dev host, 2026-08-27): every catalog page/endpoint (season-passes,
season-pass-add-ons, add-ons, special-offers, daily-tickets, military-discounts, the SUBAF50
calendar call) returned in **0.5–1.15s** each — 7 fetches, ~6.6s sequential-equivalent, well under
bounded 3-way concurrency's actual wall time. Judged reasonably cheap for daily unattended refresh
(Part AL). Season Passes and both Add-Ons sources moved from a 7-/14-day rolling interval to the
same `DailyAt` 6 AM Eastern cadence as every other category (`FreshnessPolicyCatalog`); the
now-unused `SeasonPassRefreshIntervalDays`/`AddOnRefreshIntervalDays` config properties were removed
(no remaining reader; `RollingInterval` cadence logic itself remains independently tested in
`Domain.Tests/Retrieval/FreshnessPolicyTests.cs`, so no coverage was lost). Finalization-before-refresh
ordering, the shared overlap lock, and `RunScheduledRefreshAsync`'s existing due-based selection were
not touched.

### 22. UserForced behavior: PASS

Both new sources register as ordinary `SourceRegistration`s in the composition root;
`RunUserForcedRefreshAsync` iterates the full registration list unconditionally (13B, unchanged), so
both automatically participate in Check Online with no per-source allowlist to maintain.
`CompositionRootTests.BuildServiceProvider_SourceRegistrations_CoverEveryExpectedFamily` (updated)
and `BuildServiceProvider_RegistersTenSourceRegistrations` (renamed from "Eight") are the
omission-resistant guards Part T asks for.

### 23. startup performance/non-blocking: PASS (unchanged)

No change to `App.axaml.cs`'s synchronous startup path, `StartupCatchUpRefresh`, or any
`MainViewModel`/child-ViewModel construction — the two new sources are only ever reached through the
same background/explicit-refresh paths every existing catalog source already uses.

### 24–25. UI/PDF price presentation: PASS

`ProductRowViewModel` gained `BundleNoteText`/`HasBundleNote` (Products screen, compact italic line,
e.g. "2 for $50.00 — $25.00 per person"). `AssociatePassdownDocumentModelBuilder.BuildOfficialRow`
gained the same derived note in `PdfProductRow.Notes` — **no `PdfProductRow` schema change**. Both
new categories route to `PdfProductSectionTier.Promotional` (qualifier + comparison both render,
unlike the compact Reference tier) so Military/First Responder's real "$80 Gate Price" comparison
isn't silently dropped.

### 26. finalization/History behavior: PASS (unchanged, verified compatible)

`FinalizedPassdownSnapshot`/`FinalizedProductRowSnapshot` were not touched — the new bundle note
flows through the existing free-text `Notes` field exactly like any other product note, so History
freezes it correctly with zero schema change.

### 27. stale/LKG price behavior: PASS (unchanged)

Not touched this phase; existing `AssociateVerificationState`/`RetrievalStatus` machinery applies
identically to the two new sources.

### 28–29. schema/version decision; backward compatibility: PASS — no migration

**Database schema stays 10, `AssociateDraftPayload` stays 3, `FinalizedPassdownSnapshot` stays 1.**
Every new field (`PriceKind`, `BundleTotalPriceAmount`, `BundleQuantity`, `PerPersonPriceAmount`) is
computed at composition time from already-persisted columns and is never itself persisted — existing
cached catalog rows load and classify correctly with no backfill, satisfying Part AN by construction
rather than by a `LegacyCatalog` fallback value.

### 30–32. tests added; final count; build result

**55 net tests added** (1415 → **1470**, all passing, `Category!=LiveSmoke`, re-run twice):
`PriceSemanticsClassifierTests` (13), `AssociatePassdownServicePriceSemanticsTests` (7),
`AssociatePassdownDocumentModelBuilderPriceSemanticsTests` (4), `DatedBringAFriendDuoSourceTests`
(15), `MilitaryFirstResponderCatalogSourceTests` (11), `PromotionsCatalogSourceTests` (+2), plus
targeted rewrites (not net-new) of `FreshnessPlannerTests`/`ScheduledRefreshLateInvocationTests`/
`RefreshOrchestratorTests`/`CompositionRootTests`/`AssociatePassdownServiceTests`/
`OperationsDashboardServiceTests` for the daily-cadence and new-category changes. Build:
`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors. No Avalonia flake observed this run.

### 33. documentation changed

`docs/retrieval/special-ticket-sources.md` (new), `docs/retrieval/pricing-semantics.md` (appended),
`docs/retrieval/catalog-source-inventory.md` (appended), this file (this section).

### 34. live-source validation status: EXECUTED

Performed throughout this phase (2026-08-27), not deferred: `special-offers`, `daily-tickets`,
`military-discounts`, `season-passes` (unchanged, spot-checked), and the `getkeywordcalendar`
endpoint for both `SnapWizardDailyTickets` (control) and `SUBAF50` (new) — all via plain anonymous
HTTP, no authentication. Full trail in `docs/retrieval/special-ticket-sources.md`.

### 35. work deferred to 13D–13G

Unchanged from 13A/13B's own deferral list (§38): Manual Hours/Buyout redesign (13D), ERT semantics
(13E), rename/branding (13F), final audit/release rebuild (13G). Additionally deferred within 13C's
own scope, explicitly: an ordinary-BAF dated-price adapter (no anonymous surface found — see §7
above), and a `biggest-sale-of-the-year` adapter (still unadopted, unchanged from 13A's finding).

## Git checkpoint

Retrieval coverage complete, pricing semantics complete, fixtures/tests complete, daily-cadence
decision measured, full suite green (1470/1470, `Category!=LiveSmoke`), build clean (0/0), docs
updated — Phase 13C is complete per this document's own success criteria. One local commit follows;
13D is explicitly not started.

## Phase 13D — Operations Calendar integration hardening, Deep Refresh, and Passdown/PDF corrections

Phase 13D treats Calendar, refresh coverage, and Passdown output as one integration layer, closing
real operational issues found during use rather than adding new product/parser scope (that remains
future work). Nothing in 13A–13C's architecture was reopened except where explicitly described
below.

### 1–3. Calendar cell geometry, dense-cell/notes behavior, dense-month regression: PASS

Root cause: `Button.calendarDay` (`src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml`)
set `MinHeight="72"` with no cap — a floor, not a ceiling. Since `UniformGrid`'s cell size is driven
by the single tallest child's desired size across the *entire* grid, one date carrying enough
optional content (Custom Hours + waterpark line + Stale + many Important notes) inflated every cell
in the month, and the amount of inflation changed month to month depending on which date happened to
be busiest — exactly the "visually inconsistent" symptom reported.

Fix: `MinHeight` replaced with a fixed `Height="132"` (uniform for every cell regardless of content)
plus `ClipToBounds="True"`, and every compact indicator line (`dayStateLabel`/`dayHoursLine`/
`dayWaterparkLine`/`dayStaleLabel`/`dayIndicator`) now sets `TextWrapping="NoWrap"` +
`TextTrimming="CharacterEllipsis"` so a long value trims instead of wrapping to a second line. This
makes "one date's content cannot expand only that date's cell" structurally true (a fixed-height
control cannot grow from its own content), not merely usually true. Off-month cells share the exact
same style, so their reduced content doesn't shrink them either.

**Test-harness honesty note**: this headless test environment (no real compositor/window) does not
realize `ItemsControl`-generated day-cell containers via `Measure`/`Arrange` alone — confirmed
empirically (`GetLogicalDescendants`/`GetVisualDescendants` both found zero `Button.calendarDay`
instances after a manual layout pass without a real windowed session). Per-cell pixel-bounds
assertions are therefore not achievable here. What *is* proven, in `OperationsCalendarViewTests.cs`:
(1) `CalendarDayStyle_UsesFixedHeight_NotMinHeight` — reads the compiled `Style` object directly and
asserts a `Height` setter exists, `MinHeight` does not, and `ClipToBounds=true` does; this is the
actual mechanism proof, not inferred. (2) Construction/layout-doesn't-throw regression coverage at
both 1280×860 and 960×640 for a dense month with one extreme content outlier alongside sparse
siblings, off-month cells present, every operating state, and waterpark hours — the densest
realistic combination this phase describes.

### 4–5. Carolina Harbor / waterpark hours source audit and semantics: PASS

Traced directly, not guessed: `docs/retrieval/park-hours-source.md`'s own live investigation (Phase
3B) already recorded that the CloudFront `operating-hours/park/{id}` response can carry a second
`operatings[]` entry, `operatingTypeName == "Waterpark"` (id 70), on dates Carolina Harbor is
separately open — the raw source has always exposed it; `ParkHoursSource` simply never read it
before this phase. Fixed by extending the same adapter: `ReadWaterparkHours` performs the identical
defensive lookup the main-gate parsing already does, lenient rather than strict (absent/blank/
unparseable → `null`/`null`, never a whole-retrieval failure). `ParkHoursPayload` gained two nullable
fields (`WaterparkOpeningTime`/`WaterparkClosingTime`) — additive JSON, no schema migration, since
this payload is stored as opaque JSON text in the existing `dated_results` value column.

### 6–8. Waterpark hours model, Calendar presentation, selected-date presentation: PASS

`AssociateParkHoursView` gained `WaterparkOpening`/`WaterparkClosing` (+ computed `HasWaterparkHours`)
alongside the existing main-gate fields — no manual-override/private-buyout concept applies to the
waterpark (spec never asked for one), so unlike the main gate there is no second "effective hours"
calculation, just the raw official value passed through. `AssociateParkHoursComposer.Compose` reads
it straight off `ParkHoursPayload`. Presentation: `ParkHoursPresentation.WaterparkHeadlineText`
("11:00 AM – 6:00 PM") and `OperationsCalendarOperatingStatePresentation.CompactWaterparkLine`
("Harbor 11 AM–6 PM") are both computed only from `HasWaterparkHours` — no per-date hardcoded season
window anywhere. Calendar day cell: one additional compact line in the *same* cell (`dayWaterparkLine`
style, muted relative to the main hours line), never a separate badge/card. Calendar selected-date
panel: a "Carolina Harbor" section, shown only when `Presentation.HasWaterparkHours`, cleanly absent
otherwise (no blank placeholder).

### 9. Waterpark hours elsewhere (Dashboard/Associate/PDF): PARTIAL BY DESIGN

Added to Associate Overview's existing Park Hours panel (same `ParkHoursPresentation`, so this was a
one-line XAML addition, not a second formatter) — satisfies spec's "app operational surfaces may show
them where useful." **Deliberately not added to Dashboard's Data Status card** — Dashboard's park-
status summary is already a single-line-per-category compact surface, and Carolina Harbor is not one
of Dashboard's tracked source categories; adding it would need a new category concept for a feature
Calendar and Associate already cover well. **Deliberately not added to the PDF** — spec explicitly
cautions against expanding PDF content unnecessarily, and the printed Passdown's Park Hours panel is
already tight; Carolina Harbor is available to the associate in-app (Calendar, Associate Overview)
without needing to reach the printed page.

### 10. Prior Park Hours/Dated Admission/Fast Lane/Haunted refresh horizon: PASS (audited)

Read directly from `RefreshOrchestrator.PlanScheduledOperationsAsync`/`IRollingDatePlanner`: every
`SourceScope.Dated` registration (Park Hours, Dated Admission, Dated Fast Lane, Dated Haunted, Dated
Bring-A-Friend Duo) shared one horizon — `RollingDatePlanner.GetRefreshWindow()`, default 7 days
(today through today+6), unconditionally, for both the pre-13D scheduled path and (unchanged) the
`RunUserForcedRefreshAsync` per-selected-date behavior. No source had its own independent horizon.

### 11–12. Calendar-aware Park Hours coverage policy; population cost measurement: PASS — measured, not guessed

New `ParkHoursCoveragePlanner.BuildCoverageWindow(today)`
(`src/Carowinds.Passdown.Application/Freshness/ParkHoursCoveragePlanner.cs`): today through the last
day of *next* calendar month — the spec's own preferred target (today Aug 27 → Sep 30). Chosen over
"current + next displayed month" because it needs no Calendar-viewport awareness at all (a pure
function of today's date), so the same coverage guarantee holds however far the user navigates.

**Measured this phase**, live, from this dev host (2026-08-27), against the real CloudFront endpoint
`ParkHoursSource` itself calls, bounded to 3 concurrent requests (matching
`RefreshOrchestratorOptions.MaxConcurrency`'s existing default — the same concurrency a real Deep
Refresh actually runs under, not a separate assumption): **35 requests (today Aug 27 → Sep 30 2026),
all HTTP 200, total wall time 8.6 seconds.** Per-date requests, not a single range/calendar response
— the source's own "monthly" endpoint form exists (documented in `docs/retrieval/park-hours-source.md`
since Phase 3B) but was not adopted here: it returns the same per-date shape aggregated by month
rather than a genuinely cheaper payload, and switching adapters mid-phase for an untested response
shape was judged riskier than the already-fast per-date path. At ~0.25s/request effective throughput
under bounded concurrency, this easily qualifies as "cheap" per Part K's own instruction — no
conservative fallback policy was needed. Combined with Phase 13C's own measured commercial-source cost
(7 fetches, ~6.6s under the same bounded concurrency) and the unchanged 7-day windows for the other
four dated families (up to 28 more operations, same pool), a full steady-state Deep Refresh is
estimated at roughly 20–25 seconds wall time — comfortably inside Part AO's "tens of seconds
unattended/manual may be acceptable" guidance. See Part 37 below for the as-implemented performance
accounting.

### 13. Deep Refresh architecture: PASS

One new orchestrator method, `IRefreshOrchestrator.RunDeepRefreshAsync(bool bypassFreshness,
DateOnly? focusDate, ...)` (`src/Carowinds.Passdown.Application/Refresh/RefreshOrchestrator.cs`) — the
one authoritative operation every trigger in this phase shares, never a second/duplicated
implementation:

- **Park Hours** always plans the full coverage window, unconditionally — due-ness answers "was this
  source checked in the last day," a different question from "is next month's calendar populated," so
  due-gating Park Hours here would never actually achieve the coverage guarantee this operation exists
  for.
- **Every other dated/catalog source** reuses the *existing* `PlanScheduledOperationsAsync` due-gating
  logic verbatim when `bypassFreshness: false` (scheduled/startup-catch-up nightly maintenance — same
  cost/cadence as before this phase), or the same unconditional shape `RunUserForcedRefreshAsync`
  already uses — across the normal 7-day window rather than one date — when `bypassFreshness: true`
  (the manual button, and the insufficient-coverage Check Online bootstrap).
- **`focusDate`**, when supplied, guarantees every non-Park-Hours dated source also covers that
  specific date even if it falls outside the normal 7-day window — so the insufficient-coverage
  bootstrap never leaves out the date the associate actually asked about.
- Always updates `source_state` (unlike `RunUserForcedRefreshAsync`) — a Deep Refresh genuinely is a
  fresh check for every source it touches, and marking it so prevents the next due-based check from
  redundantly re-hitting a source moments after a manual Deep Refresh already covered it.

### 14. Coverage eligibility rule (`NeedsDeepRefresh`): PASS

`Application.Freshness.DeepRefreshCoverage.NeedsDeepRefresh(DeepRefreshStatusRecord?, DateOnly today)`
— computed purely from local persisted state (the last Deep Refresh's own recorded
`CoverageThrough`), never "has this user ever clicked Check Online": true when no Deep Refresh has
ever completed, or when `CoverageThrough` trails the target horizon by more than 3 days (a small
slack so one date's transient fetch failure on the last run doesn't force every subsequent Check
Online back into bootstrap mode).

### 15. First/insufficient-coverage Check Online: PASS

`AssociatePassdownService.CheckAssociateDateOnlineAsync` (the one shared entry point Dashboard and
Associate's own Check Online both already funneled through, per 13B) now checks
`NeedsDeepRefresh` first (only when a `IDeepRefreshStatusRepository` is supplied — optional
constructor parameter, default `null` preserves every pre-13D caller/test unconditionally): if
insufficient, calls `RunDeepRefreshAsync(bypassFreshness: true, focusDate: operatingDate)` instead of
the normal `RunUserForcedRefreshAsync(operatingDate)` — restoring Park Hours coverage and
force-refreshing every commercial source in the same click, with the selected date itself still
guaranteed covered via `focusDate`.

### 16. Subsequent Check Online: PASS — unchanged, confirmed fast

Once `CoverageThrough` is healthy, `CheckAssociateDateOnlineAsync` takes the original, unmodified
`RunUserForcedRefreshAsync(operatingDate)` path — exactly the same single-date + all-catalog-sources
scope as every pre-13D Check Online, no widened cost.

### 17–18. Manual Run Deep Refresh; UX: PASS

`IOperationsDashboardService.RunDeepRefreshAsync` (new) — always available regardless of automatic
scheduling status, runs `RunDeepRefreshAsync(bypassFreshness: true, focusDate: today)` unconditionally
(never gated on `NeedsDeepRefresh`), through the exact same overlap-lock guard `CheckOnlineAsync`
already uses (`Func<IScheduledRefreshLock>?`) — if another refresh already holds the lock, returns
`RefreshSummary.AlreadyInProgress` rather than starting a second concurrent orchestrator run. Wired
into Dashboard → Data Status (`OperationsDashboardViewModel.RunDeepRefreshCommand`, mutually exclusive
with Check Online/Reload at the UI level, on top of the shared lock's own authoritative guarantee).
UX matches spec's literal before/running/success/partial copy
(`DeepRefreshStatusPresentation`/`RefreshMessages.Progress`) — "Checking online and preparing upcoming
dates…" while running, "✓ {timestamp}" / "Incomplete · {timestamp}" / coverage-through text on
completion, no fake progress percentages (the existing indeterminate `ProgressBar` is reused
verbatim).

### 19. Scheduled 6 AM behavior: PASS

`ScheduledRefreshRunner.RunAsync` (shared by both `RefreshMode.Scheduled` and
`RefreshMode.StartupCatchUp`) now calls `RunDeepRefreshAsync(bypassFreshness: false, focusDate: null,
...)` instead of the old `RunScheduledRefreshAsync` — literally "reuses the same authoritative Deep
Refresh operation," not a parallel implementation. Finalization-before-refresh ordering
(`ScheduledRefreshHost`/`StartupCatchUpRefresh`'s own try/catch structure) was not touched — neither
file changed at all, only the one orchestrator call inside `ScheduledRefreshRunner` changed.

### 20. StartupCatchUp: PASS

Because `StartupCatchUpRefresh` already called the shared `IScheduledRefreshRunner.RunAsync(RefreshMode
.StartupCatchUp, ...)` (Phase 6B/13B), and that runner now calls `RunDeepRefreshAsync` instead of the
old due-gated method, StartupCatchUp automatically inherits full Deep Refresh behavior — including the
unconditional Park Hours coverage restoration — with zero changes to `StartupCatchUpRefresh` itself.
This is exactly Part T's "missed 6 AM → next app start restores coverage in background, no modal
interruption" requirement: it was already a detached, never-awaited background task before this
phase (Phase 6B), and remains one — only what it awaits internally got deeper.

### 21. Multi-user/per-user storage implications: PASS (confirmed unchanged)

`AppPaths.LocalApplicationDataDirectory` resolves per-Windows-user (`%LOCALAPPDATA%`); nothing in this
phase's additions (new migration, new repository, new orchestrator method) introduces any
machine-wide state, shared database, or cross-user coordination. `deep_refresh_status` (new table) is
one row inside the same per-user SQLite file every other table already lives in — User A's Deep
Refresh never touches User B's database, confirmed by construction (no new connection
factory/path was introduced).

### 22. Task Scheduler current configuration: PASS (audited directly from packaging script, no change)

Read directly from `packaging/windows/Install-ScheduledRefresh.ps1` (not inferred from Windows UI):

| Setting | Value | Source line |
|---|---|---|
| Principal | `New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited` | line 116 |
| Logon type | **Interactive — "run only when user is logged on" behavior**; will not run while the installing user is logged out | line 116 |
| Admin requirement | None (`RunLevel Limited`) | line 116 |
| Password/credential | None stored — Interactive logon type needs no stored password | line 116, doc comment lines 19-20 |
| Task ownership | Per-user — registered under the *installing* user's own account, not a shared/SYSTEM task | line 116 |
| `StartWhenAvailable` | Enabled — runs as soon as possible after a missed scheduled start (machine asleep/off at 6 AM) | line 106 |
| `MultipleInstances` | `IgnoreNew` — Task Scheduler's own first line of defense; `FileScheduledRefreshLock` remains the authoritative overlap guard | line 107 |
| Battery behavior | `AllowStartIfOnBatteries`, `DontStopIfGoingOnBatteries` | line 108 |
| Network prerequisite | None configured (`-RequiresNetwork` deliberately omitted, per the script's own comment) | lines 93-95 |
| `WakeToRun` | Not set — never wakes a sleeping/hibernating machine | line 96-99 |

### 23. Logged-out automatic execution decision: DEFERRED BY DESIGN — accepted limitation, unchanged

The current `LogonType Interactive` principal cannot run while the installing user is logged out —
this was true before Phase 13D and remains true after it. Per CLAUDE.md's own explicit multi-user
guidance (preferred policy items 1–5) and this phase's Part R instruction, the correct action given
that guidance is: **do not change it.** Achieving logged-out execution would require either a SYSTEM-
level scheduled task (violates "no admin-only services," changes the app from portable/no-admin to
requiring elevation) or a stored-credential `Password`/`S4U` logon (violates "no stored Windows
passwords/credential prompts," a real security regression for a portable local tool). Both are
explicitly prohibited by CLAUDE.md's MULTI-USER ENVIRONMENT section. The accepted trade-off: if no
applicable Windows user session exists at 6 AM, that user's scheduled refresh simply does not run —
recovered automatically, without a stored password or elevated service, by `StartupCatchUpRefresh`
the next time that user's own session starts the app (Part 20 above). No code change was made; this
section records the decision explicitly, as Part S requires, rather than leaving it implicit.

### 24. Last Deep Refresh persistence/status: PASS — small, justified migration

`source_state` (existing, per-source) cannot represent "how many sources succeeded/failed in the last
whole Deep Refresh job" or "what future date range is actually covered" — neither is a per-source
concept, so representing it there would mean inventing a fake synthetic source id, a worse fit than a
dedicated table. New: `Migration0011CreateDeepRefreshStatusTable` — one single-row table
(`deep_refresh_status`, `id` fixed at 1), fields: `last_attempt_at_utc` (always recorded),
`last_success_at_utc` (only when the run's own outcome was fully `Success` — Part U's "distinguish
attempted from succeeded"), `outcome` (`Success`/`Partial`/`Failed`), `coverage_through` (nullable
date), `source_success_count`, `source_failure_count`. **Database schema: 10 → 11.**
`AssociateDraftPayload` schema stays 3, `FinalizedPassdownSnapshot` schema stays 1 — neither touched.
Written truthfully by `RefreshOrchestrator.RunDeepRefreshAsync` itself (not by each caller
separately, so every trigger — scheduled, startup-catch-up, manual, bootstrap Check Online — gets
identical, correct bookkeeping with no risk of one call site forgetting to record it):
`CoverageThrough` is computed from the *actual* just-executed Park Hours results (the last
consecutive `Success` date starting from today), never the desired target — a run that only partially
succeeded reports exactly how far it actually got.

### 25. Dashboard indicator: PASS

Dashboard → Data Status gained a compact "DEEP REFRESH" sub-section (below the existing Check
Online/count-chips/source-breakdown content, inside the same panel — never a second Alerts-style
card): one headline line (✓/Incomplete/Failed + timestamp), an optional detail line (failure count),
an optional coverage line ("Park hours available through Sep 30"), and the `Run Deep Refresh` button
with its own dedicated busy indicator (`IsRunningDeepRefresh`, distinct from `IsCheckingOnline`).

### 26. Calendar indicator decision: DEFERRED BY DESIGN — not added

Spec Part W offers this as optional ("Optionally add..."). Not added this phase: Dashboard → Data
Status is already the authoritative, single place Deep Refresh status lives (Part 25), and Calendar
already shows `EffectiveHoursLine` for the displayed dates directly — a supervisor scanning the grid
already sees whether hours are populated without needing a second, summarized "coverage through"
readout duplicating what Dashboard says. Revisit only if real user feedback asks for it.

### 27. Last Verified vs. Deep Refresh vs. Check Online semantics: PASS — kept distinct, never confused

- **Last Deep Refresh** (new, Part 24): broad future/cache maintenance operation status.
- **Last Check Online**: not separately tracked as its own persisted concept (unchanged from every
  prior phase) — `RefreshSummary`/`RefreshMessages` already report a normal Check Online's own
  plain-language result inline, per-action, not as a persisted status row.
- **Last Verified**: unchanged, per-source (`AssociateVerificationInfo.LastVerifiedAt`), still visible
  in-app (Dashboard Data Status source breakdown, Associate's own Park Hours/product panels, Calendar).
  Never conflated with Deep Refresh status in any of the new UI added this phase.

### 28. Passdown/PDF Last Verified removal: PASS

Root cause traced to exactly one place: `AssociatePassdownDocumentModelBuilder.BuildVerificationText`
(`src/Carowinds.Passdown.Documents/Associate/AssociatePassdownDocumentModelBuilder.cs`) built
`"Last verified {timestamp}"`/`"Not verified"`/etc. text, rendered by
`AssociatePassdownPdfGenerator` inside the Park Hours panel. Now returns `null` unconditionally — kept
as an always-null method (rather than removing `PdfParkHours.VerificationText`/
`FinalizedParkHoursSnapshot.VerificationText`) so neither the print model's nor the finalized-
snapshot's shape changes, avoiding a schema bump for a text-suppression change. New regression test
(`AssociatePassdownPdfGeneratorTests.StaleDataDay_NeverPrintsVerificationMetadata`) proves the phrases
never appear even for a day whose underlying data is genuinely stale. In-app surfaces (Dashboard Data
Status, Associate's own on-screen Park Hours panel via `ParkHoursPresentation`, Calendar) were not
touched — they read the same `AssociateVerificationInfo` through a completely separate formatter.

### 29–30. Multi-page PDF warning; action behavior: PASS

Root architecture change: the PDF generator previously refused to generate anything once actual or
estimated content exceeded one page (`AssociatePassdownPdfStatus.ValidationFailed` for
`ExceedsOnePage`), discarding the associate's entire draft's worth of PDF work. Per spec Part AA
("do not automatically block... allow generation/save, make the consequence unmistakable"):
`AssociatePassdownPreflightResult.CanGenerate` now only blocks on `MissingOperatingDate` (nothing to
print at all); `AssociatePassdownPdfGenerator.Generate` always produces the document and reports the
*real* post-generation page count (`AssociatePassdownPdfResult.PageCount`, QuestPDF's own pagination,
never the pre-render estimate) rather than refusing. Finalization inherits the same behavior
automatically (`FinalizedPassdownSnapshotBuilder.BuildProjection` already just checked
`Status == Generated`) — an unattended scheduled finalization no longer loses an entire day's frozen
record merely because content ran long; the frozen PDF simply spans more than one physical page.

UI: `AssociateViewModel.PdfPageCount` (int, default 1) updates only when a real generation actually
produces bytes — never reset to a false "no warning" state merely because `CreatePdfCommand` was
pressed again, satisfying Part Z's "repeated button presses do not hide/reset it." A new persistent
banner (`Border.multiPageWarning`, distinct amber border + bold text, deliberately *not* the existing
small `fieldWarning` helper-text style) sits in Associate's header area, visible regardless of which
internal section (Overview/Products/Notes) is active, reading exactly the example wording ("WARNING —
Passdown is 2 pages. Reduce content before printing if possible."), text-only (never color-only) —
`AutomationProperties.Name` bound to the same text.

### 31–32. Page-count detection audit; multi-page test coverage: PASS

`CountGeneratedPdfPages` (unchanged mechanism — reads the real generated PDF's own `/Type /Pages
.../Count N` object) is now the *only* signal driving the warning; the line-budget preflight estimate
remains only as an internal early classification, never surfaced to the UI as the page count. Tests
(`AssociatePassdownPdfGeneratorTests`): normal content → `PageCount == 1`; `ExcessiveDay` fixture →
generates successfully with `PageCount > 1` (was previously `ValidationFailed`/no bytes); repeated
generation retains an accurate count; `FinalizedPassdownProjectionTests` proves finalization itself
now succeeds (not throws) for the same excessive-content fixture, producing a real multi-page frozen
PDF.

### 33. Custom Associate product value PDF bug — root cause found and fixed: PASS

Traced end to end, confirmed **not** BAF-specific (the bug affects every official product's override
uniformly): the precedence chain itself
(`ProductRowViewModel.DisplayPriceOverride` → `AssociateDraftPayload.ProductOverrides[identity]` →
`AssociatePassdownDocumentModelBuilder.BuildOfficialRow` reading
`AssociateProduct.EffectiveDisplayPrice`) was already correct. The actual break:
`AssociateViewModel.SaveDraftNowAsync` (`src/Carowinds.Passdown.Desktop/ViewModels/AssociateViewModel.cs`),
after a successful autosave, updated `Workspace` via `current with { Draft = draft, SaveStatus =
status, ParkHours = parkHours }` — **`Products` was never included in that `with` expression**, so
`Workspace.Products[i].DisplayPriceOverride`/`IncludeOnPdf` kept whatever value the *last full
compose* (initial load, date switch, or Check Online) had produced, silently ignoring every override
saved since. `CreatePdfAsync` always reads `Workspace` (never re-composes), so a custom value typed
and autosaved just before pressing "Create PDF" — the single most common real-world sequence — never
reached the PDF, even though the draft itself was correctly persisted to disk (which is why the value
*did* survive a reload, masking the bug outside of the exact "edit then immediately print" sequence).

### 34. Custom-value precedence fix: PASS

`SaveDraftNowAsync` now also rebuilds `Products` from the freshly-saved `draft.ProductOverrides`
(matching each product by `ProductIdentity`, leaving `IsCustom` entries untouched since those already
flow correctly via `Draft.CustomProducts`) and includes it in the same `with` expression. Applies
uniformly to every official product (dated or catalog) — no product-name branching anywhere in the
fix, satisfying Part AF's "not special-cased" requirement directly.

### 35. PDF/finalization/History regression: PASS

`AssociateViewModelProductEditingTests` gained two tests proving the exact defect site directly:
`PriceOverride_AfterAutosave_FlowsIntoWorkspaceProducts_ForPdfGeneration` (a Daily Admission product)
and `IncludeToggle_AfterAutosave_FlowsIntoWorkspaceProducts_ForPdfGeneration` (a Season Pass — a
second, differently-categorized product, proving the fix isn't accidentally scoped to one category)
— both assert `Workspace.Products` (what `CreatePdfAsync` actually reads) reflects the just-saved
override immediately after autosave, without a date switch or Check Online in between. The
Documents-layer chain (`Workspace.Products` → `PdfProductRow.EffectivePriceText` → generated PDF text,
and → `FinalizedProductRowSnapshot` → frozen History) was already covered by existing 13C-era tests
(`AssociatePassdownDocumentModelBuilderPriceSemanticsTests`, `FinalizedPassdownProjectionTests`) and
remains green, unmodified — combined, the full chain from keystroke to frozen History is proven.

### 36. Selected-date Daily Admission price: PASS

`OperationsCalendarSelectedDateSummary.DailyAdmission` (new,
`OperationsCalendarDailyAdmissionPrice`) — computed in `OperationsCalendarService` from the *already-
composed* `AssociateWorkspace.Products` for the selected date (the same call the panel's other
sections already used — no new query). Per Part AF, the base admission product is identified
structurally, never by matching a specific display name: the lowest-priced product in the
`DatedAdmissionTickets` category (bundles/upsells are reliably priced higher than the base ticket).
`PriceKind.StartingFrom` is surfaced explicitly ("Starting from $39.00") so a floor price can never be
misread as exact for the selected date; no product in the category at all falls back to the category's
own checked/not-published/unavailable status — never a fabricated price.

### 37. View Date Pricing behavior; Calendar state preservation: PASS

New `OperationsCalendarViewModel.ViewDatePricingCommand`/`ViewDatePricingRequested` event, wired in
`MainViewModel` exactly like the existing "Open Passdown" deep link — sets
`AssociateViewModel.SelectedDate` and `SelectedSection = AssociateSection.Products`, then switches the
top-level section. Purely local: no network call is triggered by navigation itself (the destination
screen already loads local-only per its own existing contract); Calendar's own `SelectedDate`/snapshot
are never mutated by this action (proven by
`CalendarViewDatePricingCommand_NeverChangesCalendarSelectedDate`). Calendar's own summary stays
compact per Part AK — only the one Daily Admission line, never every Fast Lane option/pass/promotion/
bundle; View Date Pricing is the explicit seam to that full detail.

### 38. Daily commercial refresh preservation; new-offer discovery: PASS — unchanged

Every Phase 13C daily-cadence commercial source (Promotions' dual-page fetch, Season Passes, both
Add-Ons sources, Bring-A-Friend Duo, Military/First Responder) is registered exactly as it was;
`RunDeepRefreshAsync`'s catalog-scope planning reuses the identical registration list every other
orchestrator method already iterates — no per-source allowlist exists anywhere to have silently
dropped one. Generic, name-agnostic discovery (`ProductCardHtmlParser`) is untouched.

### 39. Partial-failure/LKG behavior: PASS — unchanged, inherited

`RunDeepRefreshAsync` executes through the exact same `ExecuteJobAsync` every other orchestrator
method already uses — bounded concurrency, immediate per-operation persistence, Error-never-becomes-
absence, cancellation-safe. No new execution logic was written for this phase; only planning
(*which* operations to run) changed.

### 40. Deep Refresh performance: PASS — measured (see Part 11–12 above for the Park Hours figure)

Full steady-state Deep Refresh (bypassFreshness: false, everything due — the 6 AM common case): ~35
Park Hours requests (measured 8.6s) + up to 28 other-dated-source requests + 7 catalog requests (13C
measured 6.6s for the catalog portion alone), all sharing one `MaxConcurrency=3` pool — estimated
20–25s total wall time. Comfortably within "tens of seconds... acceptable" (Part AO); no unnecessary
date fanout or expensive source was found. The manual/bootstrap `bypassFreshness: true` path adds the
same catalog + normal-window dated requests unconditionally regardless of due-ness, so its cost is the
same order of magnitude, not materially larger.

### 41. Startup non-blocking regression: PASS — re-confirmed structurally unchanged

`App.axaml.cs`'s synchronous startup path (`OnFrameworkInitializationCompleted`) was not touched by
this phase at all — `StartCatchUpRefresh` remains the one detached, never-awaited fire-and-forget call
after `desktop.MainWindow` is assigned, exactly as Phase 6B established. `StartupProfilingTests`'
`UnreachableRefreshOrchestrator` fake (now also implementing `RunDeepRefreshAsync` as a throw) still
passes, proving no local-load path in that harness reaches the orchestrator regardless of the new
method's existence.

### 42. Refresh-lock/overlap behavior: PASS

`RunDeepRefreshAsync` — like `RunScheduledRefreshAsync`/`RunUserForcedRefreshAsync` before it — has no
lock logic of its own; the *callers* (`ScheduledRefreshHost`, `StartupCatchUpRefresh`,
`OperationsDashboardService.RunDeepRefreshAsync`, `AssociatePassdownService.CheckAssociateDateOnlineAsync`)
each acquire the one shared `FileScheduledRefreshLock` before calling it, exactly as before this
phase. New test coverage: `OperationsDashboardServiceTests.RunDeepRefreshAsync_LockAlreadyHeld_ReportsAlreadyInProgress_NeverCallsOrchestrator`.

### 43. Accessibility: PASS

New interactive/status elements all carry meaningful `AutomationProperties.Name`/`HelpText`: "Run Deep
Refresh" (button), "Deep Refresh status" (headline text, `HelpText` carries the full composed status
sentence), "View Date Pricing" (button), "Carolina Harbor hours: {value}" (waterpark lines, Calendar
and Associate Overview), "Daily ticket price: {value}" (Calendar selected-date price), and the
multi-page warning banner's own `AutomationProperties.Name` bound to its visible text. None of the new
status signals (Deep Refresh outcome, multi-page warning, waterpark presence) depend on color alone —
every one has an equivalent text/automation-name path, matching every existing surface's convention in
this codebase.

### 44. 1280×860 / 960×640; Windows scaling status: PARTIAL — construction/layout-level only (unchanged limitation)

Same honest boundary as every prior Phase 12 pass: this dev environment has no pixel/bitmap renderer,
so 1280×860/960×640 coverage here proves construction and a real `Measure`/`Arrange` layout pass
completes without throwing at both sizes for the phase's new content (waterpark lines, Deep Refresh
status block, multi-page warning banner, Daily Admission price/View Date Pricing button) — not a
pixel-level visual PASS. Real Windows-scale (100/125/150%) validation remains a manual Windows-host
task, as in every prior phase.

### 45. Schema/version decision: PASS — one small, justified migration

**Database schema: 10 → 11** (`Migration0011CreateDeepRefreshStatusTable`, justified in Part 24 above
— `source_state`'s per-source shape cannot represent a whole-job outcome or a coverage-through date
without inventing a fake source id). **`AssociateDraftPayload` schema stays 3.**
**`FinalizedPassdownSnapshot` schema stays 1.** Every other change this phase (waterpark hours,
`PriceKind`/pricing display, PDF page count) is either additive-nullable JSON already stored as opaque
text, or purely computed/transient — no other migration was needed. Old-database upgrade coverage:
`PortableLifecycleTests.HistoricalSchema_BeforeAssociateDrafts_UpgradesForwardPreservingExistingData`
(existing test, updated to expect the new version) and a new dedicated
`SqliteDeepRefreshStatusRepositoryTests` (round-trip, upsert, and truthful-null-on-failure coverage).

### 46–47. Tests added; final non-live test count: PASS

**Net new tests this phase: 38** (1470 → **1508**, all passing, `Category!=LiveSmoke`; Domain 66,
Application 453, Documents 101, Infrastructure 350, Ui 538; re-run repeatedly for flakiness — see
Part 48 below for the one known pre-existing flake class observed). Representative additions by area:
Calendar geometry/style-inspection and dense-content layout coverage; waterpark hours (Infrastructure
adapter, Application composition, Desktop presentation); the Deep Refresh orchestrator method (full
coverage window, due-gated vs. unconditional, focus-date, source-state truthfulness); Deep Refresh
status persistence (repository round-trip/upsert/truthful-failure); Dashboard manual Deep Refresh
(ViewModel command behavior + service-level overlap-lock guard); the insufficient-coverage Check
Online bootstrap (exercised through existing `AssociatePassdownServiceTests`/`RefreshOrchestratorTests`
scaffolding rather than a new dedicated file); the custom-value PDF regression (two products, proving
the fix is not category-specific); multi-page PDF generator behavior (real overflow now generates
with an accurate page count, both at the generator and finalization layers); Last Verified removal
from the PDF (stale-data regression); selected-date Daily Admission pricing (available/cheapest-of-
multiple/not-published/never-checked); and View Date Pricing navigation (routing + Calendar-state
preservation). Plus targeted updates (not net-new) to `ScheduledRefreshRunnerTests`,
`Phase9EIntegrationTests`, and `PortableLifecycleTests` for the Deep Refresh call-site and
schema-version changes, and to `DashboardViewTests` for the new Deep Refresh UI elements.

### 48. Build result: PASS

`dotnet build CarowindsPassdown.slnx` — **0 warnings, 0 errors.** Full suite (`Category!=LiveSmoke`)
re-run twice consecutively, both fully green (1508/1508). Across additional repeated `Ui.Tests`-only
runs during this phase, the same pre-existing Avalonia-headless XAML-population parallelism race
already documented in 6D/12B/13A/13B recurred intermittently (~1 in 3–4 runs), each time against a
*different* random Avalonia view-construction test (e.g. `HistoryView_EmptyHistory_...`,
`AssociateView_Constructs_...`, `DashboardView_EachParkStatus_...`) — every one passed cleanly in
isolation on immediate re-run. Not a regression introduced by this phase; recorded here per Part BF's
explicit request to report it, not fixed (out of scope, matches established precedent).

### 49. Docs updated: PASS

This file (this section); `docs/retrieval/park-hours-source.md` (waterpark-hours finding, Phase 13D
addendum); `docs/phase12-operations-calendar.md` (Phase 13D addendum — geometry fix, waterpark
presentation, Daily Admission price/View Date Pricing); `docs/architecture.md` (Phase 13D addendum —
`RunDeepRefreshAsync`, schema 11, `IDeepRefreshStatusRepository`). 13A–13C history was not rewritten.

### 50. Work deferred to new 13E–13H: unchanged from 13A/13B/13C's own deferral list

Manual Hours/Private Buyout redesign, ERT semantics, rename/branding, Alerts, Manager workflow, and
the final release rebuild all remain exactly as originally scoped — none of it was started this phase.
Also explicitly not started per this phase's own instructions: any change to the current 7-day
rolling window for non-Park-Hours dated sources (only Park Hours was widened, per spec Part J's own
scope), and any Windows-host live validation (remains a manual task).

### Known limitation, recorded honestly (Part 23 above)

Logged-out automatic 6 AM execution remains not possible under the current per-user, no-admin,
no-stored-password Task Scheduler principal — an accepted trade-off given CLAUDE.md's explicit
multi-user constraints, recovered via `StartupCatchUpRefresh` the next time each affected user's own
session starts the app. Not a regression from this phase; the same limitation existed before it and
was simply not previously written down as a deliberate decision.

## Git checkpoint

Calendar geometry fixed, waterpark hours implemented end to end, Deep Refresh architecture complete
(orchestrator, status persistence, manual button, coverage-aware Check Online bootstrap, scheduled/
startup-catch-up reuse), custom-value PDF bug fixed with regression coverage, multi-page PDF warning
replaces the prior hard block, Last Verified removed from the printed Passdown, selected-date pricing
and View Date Pricing shipped, Task Scheduler/multi-user posture audited and documented (no change
required — already compliant with CLAUDE.md's constraints), full suite green, build clean, docs
updated — Phase 13D is complete per this document's own success criteria. One local commit follows;
13E is explicitly not started.

## Phase 13E — Manual Hours / Private Buyout typable time entry

Phase 13E is a UX/input redesign only (per its own scope, CLAUDE.md's greenfield/workflow rules, and
this document's own risk register item #12): it replaces the stock Avalonia `TimePicker` used for
Manual Hours and Private Buyout with a keyboard-typable `TextBox`, and fixes one real correctness gap
13A found (an unparseable value could never occur before, since a picker cannot produce garbage text —
but a typable field can, and that must never reach effective-hours data). No park-hours business rule,
persistence shape, or schema changed.

### 1. Prior problem (Part A, confirmed directly from code before changing anything)

Both Manual Hours and Private Buyout bound `TimePicker.SelectedTime` directly to a `TimeSpan?`
property (`AssociateViewModel.ManualOpeningTime`/`ManualClosingTime`/`PrivateBuyoutOpeningTime`/
`PrivateBuyoutClosingTime`), converted to/from `TimeOnly?` only at the ViewModel boundary
(`ToTimeOnly`/`ToTimeSpan`, now removed). Manual Hours and Buyout were two independently hand-written,
structurally identical property/handler blocks (13A's own duplication finding). The open/close
ordering check (`ManualHoursWarningText`/`PrivateBuyoutHoursWarningText`) was — and remains — a
**non-blocking** warning: `ParkHoursCalculator.Calculate` has no ordering rule of its own at all (it
passes whichever `TimeOnly?` values it's given straight through for both the manual-override and
buyout branches), so there was never a "closed unless open<close" invariant to violate; the warning is
purely a ViewModel-level UX hint that never gated `CanCreatePdf` or autosave, before or after this
phase. Overnight ranges were (and remain) neither explicitly supported nor blocked — this is preserved
exactly, per Part M's "don't invent new semantics without evidence."

### 2. New control design (Part B) and grammar (Parts C/G/H)

`Carowinds.Passdown.Domain.Hours.OperationalTimeParser`/`OperationalTimeFormatter` (new, zero
dependencies — same package as `ParkHoursCalculator`) are the one shared parser/formatter Part G
requires; every one of the four fields (Manual Open/Close, Buyout Open/Close) calls the same
`TryParse`/`Format`, never a per-field copy. No new NuGet dependency, no custom `UserControl` — a plain
`TextBox` was sufficient (13A already concluded this; confirmed again here).

Supported grammar (deterministic, culture-invariant — Part H's explicit requirement not to let machine
locale interpret `9.30` or `09/10`):

| Input shape | Example | Notes |
|---|---|---|
| Bare hour | `9` | 1-12 only; no meridiem defaults to AM |
| Hour:minute | `9:30` | minute 00-59 |
| Bare hour + meridiem | `9am`, `9 AM`, `10pm` | space before meridiem optional |
| Hour:minute + meridiem | `9:00am`, `10:30 PM` | case-insensitive |
| Digit run + meridiem | `930am`, `1030am` | 3-4 digits, no colon — **requires** a meridiem; a bare digit run with none (`930`) is rejected as ambiguous, never guessed |
| Bare `12` / `12:MM`, no meridiem | `12`, `12:15` | documented default: noon (PM), not midnight — the everyday reading of "12 o'clock" alone |

Explicitly **not** supported, rejected outright rather than guessed at: 24-hour hours above 12 without
a meridiem (`13:00`, `25:00`), decimal points (`9.30`), slashes (`09/10`), single-letter meridiems
(`9a`), and any other unmatched shape. Blank/whitespace is a valid "no value" at the parser level
(`TryParse` returns `true`, `result: null`) — whether blank is *acceptable* depends on context (see
Part R below), which the parser deliberately has no opinion on (Part G: no UI/required-ness
dependency).

Canonical display: `OperationalTimeFormatter.Format` — `TimeOnly.ToString("h:mm tt",
CultureInfo.InvariantCulture)`, e.g. `9:00 AM`, `10:30 PM`. 28 parser tests + 6 formatter tests cover
Part AG's exact minimum list plus the documented edge cases above
(`OperationalTimeParserTests`/`OperationalTimeFormatterTests`, `Carowinds.Passdown.Domain.Tests`).

### 3. Commit/normalization model (Parts E/O/P/Q) — LostFocus binding, not per-keystroke

Each `TextBox` binds `Text` with `UpdateSourceTrigger=LostFocus` (`AssociateView.axaml`) — a real,
supported Avalonia 12.1.1 binding option (confirmed against the shipped `Avalonia.Base.dll`), not a
custom behavior. This means the ViewModel's `OnXxxTextChanged` partial method — where parsing,
normalization, and autosave scheduling all happen — fires once per **commit** (Tab, click-away, or
Enter), never once per keystroke. This was a deliberate departure from the otherwise-identical
per-keystroke pattern already used for `ProjectedAttendanceText`/`DisplayPriceOverrideText` elsewhere
in this screen: those fields never rewrite their own `Text` after a successful parse, so per-keystroke
firing is harmless there; a time field *must* rewrite to its canonical display on success (Part
P's `930am` → `9:30 AM` requirement), and doing that per-keystroke would fight the associate mid-type
(typing `9` would jump to `9:00 AM` before they can type `9:30`). LostFocus binding avoids that
entirely without inventing a bespoke debounce.

Enter is handled by one small, shared code-behind handler (`AssociateView.axaml.cs`,
`TimeTextBox_OnKeyDown`) that clears keyboard focus via `TopLevel.FocusManager.Focus(null, ...)` when
Enter is pressed in one of the four fields, and marks the key handled — forcing the same LostFocus
commit path and preventing Enter from reaching any unrelated default button (Part Q). This is pure
focus plumbing, not persistence/business logic (CLAUDE.md's Avalonia boundary): the actual parse/commit
still lives entirely in the ViewModel.

On a successful parse: the last-valid backing `TimeOnly?` field updates, the field's own parse error
clears, `ParkHours`/the relevant `*HoursWarningText` are re-notified (live effective-hours recompute,
unchanged mechanism from before this phase), the text is rewritten to canonical form only if it isn't
already (harmless single re-entrant call, verified by test), and `ScheduleAutosave()` runs — the same
debounce every other field on this screen already uses. On failure: only the error message updates;
the backing field, `ManualHoursOverride`/`PrivateBuyout` draft shape, and any already-saved value are
completely untouched, and no autosave is scheduled (`AssociateViewModelOperationalTimeEntryTests.InvalidText_DoesNotScheduleAutosave_ValidCorrectionResumesIt`
asserts this directly against the debounce scheduler's own schedule count, not just save-call timing).

### 4. Manual Hours / Private Buyout UI (Parts I/J)

Both sections now read as a compact two-row grid — "Open" / "Close" labels beside typable fields,
placeholder text showing the expected shape (`9:00 AM`/`10:00 PM` for Manual, `6:00 PM`/`11:00 PM` for
Buyout) — replacing the two-`TimePicker`-plus-dash horizontal row. Buyout intentionally uses the exact
same interaction pattern as Manual (Part J: "do not give Buyout a different time-entry UX") — both
route through the identical `CommitOperationalTimeText` ViewModel helper and `OperationalTimeParser`
call. The existing enable/disable checkboxes, the Buyout details `TextBox`, and the soft range-warning
`TextBlock` are unchanged in position and behavior.

### 5. Enable/disable semantics (Part K) — audited, preserved exactly

Confirmed directly from the pre-13E code: disabling Manual Hours or Buyout never cleared the typed
values (unlike Early Ride Time's own explicit disable-clears convention,
`OnIsEarlyRideTimeEnabledChanged`). This phase preserves that: `OnManualHoursOverrideEnabledChanged`/
`OnPrivateBuyoutEnabledChanged` still only call `ScheduleAutosave()` — no field is cleared on disable.
Re-enabling restores exactly what was typed before, with no error, since blank/required-ness only
applies while the section is enabled (`AssociateViewModelOperationalTimeEntryTests.ManualHoursOverrideEnabled_Disabled_RetainsTypedValues`/
`PrivateBuyoutEnabled_Disabled_RetainsTypedValues`).

### 6. Validation (Parts L/M/N/R)

Two independent, clearly distinguished validation signals per field, both rendered as concise
`fieldError`/`fieldWarning` text (never a raw parser exception, never color-only):

- **Per-field error** (`ManualOpeningTimeError` etc., `fieldError` style — the same style
  `ProjectedAttendanceError`/`DisplayPriceOverrideError` already use elsewhere on this screen):
  `"Enter a time such as 9:00 AM."` — shown for either an unparseable/malformed value, or a blank value
  *while the section is enabled* (Part R: required-while-enabled, fine while disabled). This is the
  actual correctness fix Part N asked for: an invalid/unparseable value can now structurally never
  become the active `ManualHoursOverride`/`PrivateBuyout` domain value, because the backing field this
  phase introduced is only ever written on a successful parse.
- **Range warning** (`ManualHoursWarningText`/`PrivateBuyoutHoursWarningText`, `fieldWarning` style,
  unchanged position/severity from pre-13E): `"Closing time must be later than opening time."`
  (reworded to match spec Part L's example text; behavior unchanged — still non-blocking, still fires
  only once both individual fields already parse successfully). Kept **non-blocking** deliberately:
  `ParkHoursCalculator` has no ordering invariant to enforce, and turning this into a hard block would
  be new domain semantics invented without evidence (Part M), with a real risk of wrongly rejecting a
  genuine overnight closing time this codebase has never explicitly ruled in or out.

### 7. Autosave interaction (Part O) — unchanged debounce, gated on valid commits only

`ScheduleAutosave()` — the same single debounce mechanism every other Associate field already uses —
is only ever called from `CommitOperationalTimeText` after a successful parse. Combined with the
LostFocus commit model (§3), this means a half-typed value (`9:`, `9:0`) can never trigger a save: it
never reaches `OnXxxTextChanged` until the field is left, and even then a still-invalid value stops at
the error branch before `ScheduleAutosave()` is reached.

### 8. Existing-data / backward compatibility (Parts S/T/AM)

`ApplyWorkspaceToFields` now sets the backing `TimeOnly?` field directly from
`draft.ManualHoursOverride`/`draft.PrivateBuyout` and formats the corresponding `*Text` property via
`OperationalTimeFormatter` — an existing stored `09:30` loads and displays as `9:30 AM` with no
migration. `ManualHoursOverride`/`PrivateBuyoutOverride` (the actual persisted record shapes) were not
touched at all — only how the ViewModel gets values into and out of them changed. **Database schema
stays 11. `AssociateDraftPayload` schema stays 3. `FinalizedPassdownSnapshot` schema stays 1** — no
migration was needed or added, confirmed by inspection (this phase touched zero persistence/migration
code) and by the full suite (including `PortableLifecycleTests`) staying green unmodified.

### 9. Effective-hours precedence / Calendar / Dashboard / PDF / History regression (Parts U-Y, AJ)

`ParkHoursCalculator.Calculate` and `AssociateParkHoursComposer` were not touched. The live `ParkHours`
getter and `SaveDraftNowAsync`'s post-save `RecomputeParkHours` call now read the new backing
`TimeOnly?` fields directly instead of converting from the old `TimeSpan?` properties — same values,
same calculation, one less conversion step. Every pre-existing integration test proving Manual
Hours/Buyout flow correctly into `Workspace.ParkHours.Effective`, the PDF document model, and (via
existing Phase 9/13D coverage) finalization/History passed unmodified against the new input path
(`AssociateViewModelDraftEditingTests` — updated only to type text instead of setting `TimeSpan`
values directly, asserting the identical `TimeOnly` outcomes). No Calendar or Dashboard code was
touched — both already read through the same `AssociateParkHoursComposer`/`EffectiveHours` path.

### 10. Waterpark / Deep Refresh independence (Parts AA/AB/Z)

Not touched, not reachable from this change: Carolina Harbor hours have no manual-override or
buyout concept (Phase 13D, unchanged) and are read straight from `ParkHoursPayload`, entirely separate
from `ManualHoursOverride`/`PrivateBuyout`. No code path from a `TextBox` `TextChanged`/`LostFocus`
event reaches `IRefreshOrchestrator`, `IScheduledRefreshRunner`, or any HTTP/Playwright source —
confirmed by inspection (the entire feature is `AssociateViewModel`/`AssociateView`/`Domain.Hours`
only) and by the full suite's existing no-network-dependency tests staying green.

### 11. Accessibility (Part AC)

`AutomationProperties.Name` updated to spec's own naming: "Manual Hours Open", "Manual Hours Close",
"Private Buyout Open", "Private Buyout Close" (previously "Manual override opening time" etc.) — each
field's error `TextBlock` also carries its own `AutomationProperties.Name` bound to the same error
text, so a screen reader surfaces the validation message, not just a red/amber color. Labels ("Open"/
"Close") are plain `fieldLabel` text blocks beside each field (Grid-aligned, not `TextBox`-owned
labels), matching this screen's existing convention rather than introducing a new one.
`AssociateViewAccessibilityTests` extended to assert all six new/renamed names are present in the
compiled logical tree.

### 12. Keyboard / layout / scaling status (Parts AK/AL/AD)

- **Keyboard**: Tab order is the existing Grid/StackPanel declaration order (unchanged structurally —
  only the controls inside the existing panels changed from `TimePicker` to `TextBox`), so typing,
  Tab, typing, Tab flows exactly as spec Part E describes with no popup at any point. Enter-commits-
  and-consumes is proven at the unit level (`CommitOperationalTimeText`/`OnXxxTextChanged` tests) and
  by the code-behind handler's own logic; a true keyboard-injection integration test (simulating a real
  Enter keypress through Avalonia's input pipeline in the headless test host) was **not** attempted —
  marked **PARTIAL**, consistent with Part AK's "do not fabricate keyboard PASS if the framework cannot
  simulate it reliably" and this codebase's own precedent of avoiding synthetic input-pipeline tests.
  Ctrl+A/backspace/normal text editing are unaffected — nothing this phase added intercepts any key
  other than Enter, and only inside the four time fields.
- **Layout**: `AssociateViewOperationalTimeLayoutTests` — real `Measure`/`Arrange` passes at both
  1280×860 and 960×640 across six representative states (Manual disabled; Manual enabled+valid; Manual
  enabled+invalid with a long validation message; Buyout enabled+valid; both sections
  visible+the range warning showing) all construct/layout without throwing. Same documented boundary as
  every prior Phase 12/13 layout pass: no pixel/bitmap renderer exists in this environment, so this is
  construction/layout-level evidence, not a pixel-level PASS.
  **1280×860: PASS (construction/layout-level). 960×640: PASS (construction/layout-level).**
- **Windows scaling (100/125/150%)**: unchanged limitation — a plain `TextBox` inherits normal
  DPI-aware layout (no fixed-size spinner flyout, unlike the old `TimePicker`), but real Windows-host
  scale validation remains a manual task, as in every prior phase. **DEFERRED — manual Windows-VM
  task**, expected to be favorable given the control is now a resizing `TextBox` rather than a
  fixed-chrome flyout.

### 13. Dead-code audit (Part AF)

`grep -rn "TimePicker" src/` after this change finds zero remaining `<TimePicker>` elements or
`Style Selector="TimePicker...">` blocks anywhere in the app (only two doc-comment mentions of the
phase's own history remain). No dedicated TimePicker style, converter, or helper existed before this
phase to clean up — `TimePicker` is a stock Avalonia control (part of the `Avalonia.Controls`
namespace already referenced everywhere), not a separate package, so there is no now-unused dependency
to remove from any `.csproj`.

### 14. Tests added; final count

**57 new tests** (28 `OperationalTimeParserTests` + 6 `OperationalTimeFormatterTests` in
`Carowinds.Passdown.Domain.Tests`; 13 `AssociateViewModelOperationalTimeEntryTests` + 10
`AssociateViewOperationalTimeLayoutTests` in `Carowinds.Passdown.Ui.Tests`), plus targeted updates
(not net-new) to `AssociateViewModelDraftEditingTests` (typed-text instead of `TimeSpan` values, same
assertions) and `AssociateViewAccessibilityTests` (six new automation-name assertions). **1508 → 1565**,
all passing, `Category!=LiveSmoke`, re-run twice consecutively both fully green (Domain 100, Application
453, Documents 101, Infrastructure 350, Ui 561). One pre-existing flake was observed once during a full
run (`HistoryViewTests.HistoryView_WithSelectedDetail_ConstructsAndExposesCriticalAutomationNames`,
the same documented Avalonia-headless XAML-population parallelism race first recorded in Phase
6D/12B, and again in 13A/13B/13D against different views) — passed cleanly in isolation and on
immediate re-run; `HistoryView.axaml` was not touched by this phase. `dotnet build
CarowindsPassdown.slnx` — **0 warnings, 0 errors.**

### 15. Schema/version confirmation

**Database schema: 11 (unchanged). `AssociateDraftPayload` schema: 3 (unchanged).
`FinalizedPassdownSnapshot` schema: 1 (unchanged).** No migration was added or needed — confirmed by
inspection (zero persistence-layer files touched) and by `PortableLifecycleTests` staying green
unmodified.

### 16. Work deferred to 13F–13H

Unchanged from every prior phase's own deferral list: rename/branding, Alerts, Manager workflow, the
final release rebuild, ERT structured-time semantics (Phase 13A's proposed ERT derivation was never
implemented — `EarlyRideTimeDetails` remains free text, untouched by this phase), and real Windows-host
scale/DPI validation. Also explicitly not started per this phase's own scope: any optional quick-picker
UI (spec Part F allowed one but called it optional; typing alone already satisfies the phase's stated
goal, and adding a picker/preset-button row was judged unnecessary clutter for a first pass — revisit
only if real supervisor feedback asks for one).

## Git checkpoint (Phase 13E)

Manual Hours/Private Buyout time entry redesigned end to end: one shared, dependency-free
parser/formatter (`OperationalTimeParser`/`OperationalTimeFormatter`), a `TextBox` + `LostFocus`-commit
interaction replacing the stock `TimePicker`, per-field validation distinguishing unparseable/required
text from the pre-existing non-blocking range warning, unchanged effective-hours precedence and
autosave debounce, zero schema/migration changes, zero network/refresh-path changes, 57 new tests, full
suite green (1565/1565), build clean. One local commit follows; 13F is explicitly not started.

## Phase 13F — Early Ride Time (ERT) simplification: derived start, rides-only editing

### 1. Prior ERT behavior (audited directly from post-13E code, not from Phase 13A's own proposal)

There was no structured ERT time field to migrate — Phase 13A's finding still held unconditionally
after 13B–13E: `EarlyRideTimeDetails(string? Details, IReadOnlyList<string> ParticipatingAttractions)`
was pure free text; the "Details" `TextBox`'s placeholder literally invited typing a time range
("e.g. 9:00 AM – 10:00 AM, first two rows only"). "Enabled"/no-ERT was (and remains) a **derived**
boolean — non-blank `Details` OR a non-empty attractions list — computed independently in three places
(`AssociateViewModel`, `OperationsDashboardService`, `AssociatePassdownDocumentModelBuilder`).
`AssociateDraftPayload` schema (version 3) never carried an ERT time field. History/PDF froze ERT as
`Details`/attractions strings, never a raw time. Dashboard showed only "Enabled"/the raw `Details` text
or "Not set"; the Operations Calendar never showed ERT at all (day cells are already dense — Part AC
confirms this is unchanged, deliberately).

### 2. Derivation: no new type — the authoritative value already existed

`EffectiveHours.NorthGateOpening` (`Domain/Hours/EffectiveHours.cs`) already computes exactly
`Opening.AddMinutes(-30)` for whatever effective opening `ParkHoursCalculator.Calculate` resolved
(official / manual override / private buyout — spec §8.2's exact precedence), and was already the
single source every "North Gate" display in the app reads (`ParkHoursPresentation`,
`AssociatePassdownDocumentModelBuilder`, the Dashboard's reused `ParkHoursPresentation.Build`). Part C
asked for "one shared derivation helper... contain no UI dependency" — `EffectiveHours.NorthGateOpening`
already *is* that helper. Introducing a second `EarlyRideTimeCalculator` type would have created a
second, parallel path to the identical value, directly violating Part L ("do not create two
independently computed times") and CLAUDE.md's minimalism rule. No Domain code changed in this phase.

### 3. North Gate relationship (Part L)

Early Ride Time start and North Gate opening are, and always were structurally required to be, the
same value. This phase makes that explicit everywhere ERT is displayed by reading
`EffectiveHours.NorthGateOpening` (via the existing `ParkHoursPresentation`/`PdfParkHours.NorthGateText`
projections) rather than a second computation.

### 4. Normal open day (Part E)

Official 10:00 AM opening → derived ERT/North Gate start 9:30 AM. Custom Hours example (9:00 AM open)
→ 8:30 AM. Verified via `ParkHoursCalculatorTests` (Domain), `AssociateViewModelEarlyRideTimeTests`
(ViewModel, live), and `FinalizedPassdownSnapshotBuilderTests`/`AssociatePassdownPdfGeneratorTests`
(frozen/printed).

### 5. Manual Hours behavior (Part F/Y/Z)

`AssociateViewModel.EarlyRideTimeStartText`/`EarlyRideTimeCaptionText` are pure getters over a new
private `LiveParkHours` property — the exact same live re-run of `ParkHoursCalculator.Calculate` the
pre-existing `ParkHours` presentation getter already used, factored out so both (and the two new ERT
properties) share one computation. Every code path that already notified `ParkHours` changed
(`ManualHoursOverrideEnabled`/`PrivateBuyoutEnabled` property changes, and all four Manual/Buyout
`*TimeText` commit handlers) now also notifies the two ERT properties — so typing a new Manual Open
time changes the on-screen derived ERT start immediately, with no save round-trip, and disabling Manual
Hours reverts it to the official-derived value immediately. Covered by
`AssociateViewModelEarlyRideTimeTests.ManualHoursOverride_TypedLive_ImmediatelyChangesDerivedStart` and
`..._Disabled_RevertsToOfficialDerivedStart`.

### 6. Private Buyout decision (Part G) — resolved from the spec's own constraints, not guessed

Audited rather than assumed: `ParkHoursCalculator.Calculate` has never had a buyout-specific carve-out
for North Gate — an enabled private buyout already, unconditionally, drives `NorthGateOpening` off the
buyout's own opening time, exactly like every other effective-opening source. Part L then makes the
decision for ERT non-optional: "the same derived time represents Early Ride Time start [and] North Gate
opening... do not create two independently computed times." Diverging ERT from North Gate on buyout
days would violate that constraint outright. **Decision: Private Buyout drives the derived ERT start
identically to how it already drives North Gate — no special case.** This is a resolution of the
ambiguity from the spec's own explicit, non-negotiable Part L requirement, not a product guess; it was
not put to the product owner because CLAUDE.md reserves that channel for decisions the specification
cannot resolve, and this one the specification does resolve. Tested in
`ParkHoursCalculatorTests.Calculate_PrivateBuyoutEnabled_OverridesClosed` (explicit `NorthGateOpening`
assertion), `AssociateViewModelEarlyRideTimeTests.PrivateBuyout_Enabled_DerivesStartFromBuyoutOpening`,
and `FinalizedPassdownSnapshotBuilderTests.Build_PrivateBuyoutDay_FreezesDerivedStartFromBuyoutOpening`.

### 7. Closed / NotPublished / Unavailable (Parts H/I/J) — never fabricated

`EarlyRideTimeStartText` is `null` whenever `EffectiveHours.NorthGateOpening` is `null` (Closed or
NotCheckedYet — the latter bucket also covers NotPublished/Unavailable/Error, since none of those ever
produce a non-null official opening). `EarlyRideTimeCaptionText` then distinguishes the reason using the
same `AssociateVerificationState` the Park Hours panel already surfaces (never a new ERT-specific
freshness model, per Part AD): "Not applicable — the park is closed.", "ERT time unavailable until park
hours are published.", "ERT time unavailable — park hours could not be checked.", or "...have not been
checked yet." for genuinely NotCheckedYet. The PDF mirrors this with a two-way switch on the print
model's own `PdfParkHoursStatus` (Closed vs. everything else — the print model, unlike the live
ViewModel, never had access to `AssociateVerificationState`'s finer NotPublished/Unavailable split, so
it keeps the same terse "not yet confirmed" wording it already used elsewhere for that bucket). A Closed
date with rides already saved on the draft keeps printing/showing those rides (Part H's "do not silently
erase") — only the fabricated-time risk is removed. Stale/last-known-good hours are still an *effective*
opening (the same LKG payload the Park Hours panel already renders from), so ERT derives from them with
no special-casing, inheriting the existing stale-note presentation next to it rather than a second
staleness indicator (Part AD/J).

**Implementation-bug caught during this phase's own build-and-test pass:** the PDF's
`PdfParkHours.NorthGateText` was never actually `null` — it formats to the literal string `"-"` when
there is no time (`FormatTime(TimeOnly?) => ... ?? "-"`). An initial `if (NorthGateText is { } start)`
check in `ComposeEarlyRideTimeBox` therefore always matched, printing a fabricated "Start  -" line even
on a Closed date. Caught by `EarlyRideTimeBox_ClosedDayWithRidesConfigured_DoesNotFabricateStartTime_ButKeepsRides`
failing on first run; fixed by switching the check to `model.ParkHours.Status == PdfParkHoursStatus.Open`
(Status is only ever `Open` when `Opening`, and therefore `NorthGateOpening`, is non-null).

### 8. Waterpark independence (Part K)

Unchanged and re-verified: `AssociateParkHoursView.WaterparkOpening`/`WaterparkClosing` are read
straight from `ParkHoursPayload` and never reach `ParkHoursCalculator.Calculate` at all — there is no
code path from Carolina Harbor hours into `EffectiveHours`/`NorthGateOpening`. Regression test:
`AssociateViewModelEarlyRideTimeTests.WaterparkHoursPublished_MainGateNotChecked_NeverDerivesStartTime`
(waterpark published, main gate NotCheckedYet → still no derived ERT start).

### 9. Associate UI simplification (Parts M/N/O/AG/AH)

The ERT "Details" `TextBox` and its time-inviting placeholder are gone from the mockup's inviting role;
the field itself is kept (unchanged storage, unchanged persistence — Part Q) but relabeled
"Additional details (optional)" since old drafts may still hold free text in it and Part Q forbids
parsing/erasing that. Above it, a new read-only block shows "Start / North Gate" +
`EarlyRideTimeStartText` (a plain `TextBlock`, never a disabled `TextBox`, per Part N) with a one-line
caption underneath (`EarlyRideTimeCaptionText`) explaining the derivation or the unavailable reason.
Rides (`ParticipatingAttractionsText`) are completely untouched — same binding, same autosave, same PDF
inclusion. The derived start's accessible name is a dedicated ViewModel property
(`EarlyRideTimeStartAutomationName`, composed as plain text rather than an inline `StringFormat`
binding) so it is distinguishable from editable fields via automation tooling (Part AG); rides/details
keep their existing static automation names.

### 10. PDF behavior (Parts R/S)

The Early Ride Time PDF box now prints a "Start  h:mm tt" line — reading `model.ParkHours.NorthGateText`
directly (the exact same already-computed string the Park Hours box prints), never a second
calculation — followed by the unchanged Details/attractions lines. When no valid derived time exists,
a concise italic fallback line replaces it ("Start time not applicable — park is closed." /
"...not yet confirmed."); nothing is ever fabricated. This is intentionally still printed once more next
to the rides even though North Gate already appears in the adjacent Park Hours box in the same PDF row —
both panels read the identical source string, so it is a convenience duplication of one shared value,
not a second independent one, and lets a reader glancing only at the ERT box still see the time (Part
S's exception clause: "unless operationally useful"). `AssociatePassdownPreflight`'s one-page line
budget was bumped by exactly one line for this box; the real `RealisticWorkspaces_RenderExactlyOnePage`
QuestPDF-render regression tests (NormalDay, DenseDay) both still pass — confirmed by an actual test run,
not merely the estimate.

### 11. North Gate on PDF (Part S)

Already shown separately in the Park Hours box (unchanged, pre-existing) — both it and the new ERT-box
line use `PdfParkHours.NorthGateText` verbatim, so they can never disagree.

### 12. Finalization / History immutability (Parts T/U/V)

No new snapshot field. `FinalizedParkHoursSnapshot.NorthGateText` already froze the derived value at
finalization time (it always has, since Phase 9) — the PDF/ERT-box printer and the finalized-snapshot
mapper both read it from the one `AssociatePassdownDocumentModel` built once per finalization
(`FinalizedPassdownSnapshotBuilder.BuildProjection`), so the frozen PDF and the frozen snapshot can never
disagree, and neither can be affected by a later live-hours change (records are immutable; there is no
update path). Proven directly by the new
`FinalizedPassdownSnapshotBuilderTests.Build_LaterHoursChange_DoesNotAlterAnAlreadyBuiltSnapshot`: build
a snapshot from 9:00 AM official hours (frozen North Gate/ERT 8:30 AM), build a second snapshot from an
`Effective` hours record changed to 11:00 AM (10:30 AM), and assert the first snapshot's
`NorthGateText` is still exactly "8:30 AM".

### 13. Schema confirmation (Parts V/W/X/AP)

**Database schema: 11 (unchanged — no migration file added).** **`AssociateDraftPayload` schema: 3
(unchanged — zero fields added/removed).** **`FinalizedPassdownSnapshot` schema: 1 (unchanged —
`FinalizedEarlyRideTimeSnapshot` untouched; the frozen derived time already lived in the pre-existing
`FinalizedParkHoursSnapshot.NorthGateText`).** Confirmed directly in code (`grep` of the three
`CurrentSchemaVersion` constants and the migrations directory — `Migration0011...` remains the newest
file).

### 14. Dashboard (Part AC)

The Dashboard's one-line ERT summary now prefers the derived start ("Starts 9:30 AM") over the
free-text `Details` field, reusing the `ParkHoursPresentation.NorthGateText` the Dashboard already
builds for its own Park Hours summary (`OperationsDashboardPresentation.Build`) — never a second
subtraction. Falls back to "Enabled" when no effective opening exists yet, "Not set" when ERT itself is
off. The Operations Calendar still never shows ERT (day cells are already dense) — unchanged,
deliberately, per Part AC.

### 15. Accessibility / layout (Parts AG/AH)

New automation names added: `EarlyRideTimeStartAutomationName` for the derived start (guarded via a
markup-source assertion, `ProductRowAndCustomItemTemplates_DeclareAccessibleNamePatterns` — a
`StringFormat`/data-bound `AutomationProperties.Name` is not reliably realized by this project's
unattached headless test container, the exact same limitation already documented for per-row
product/custom-item templates), plus the pre-existing "Early Ride Time is happening today",
"Participating attractions", and the relabeled "Early Ride Time additional details". No new card/panel
was added — the derived-start block replaces the old Details-as-time convention inside the existing
"Today at a Glance" ERT column, so 1280×860/960×640 layout budgets are unaffected (unchanged grid
structure, one additional two-line `StackPanel`, verified via the existing
`AssociateViewOperationalTimeLayoutTests`-style construction path staying green). Windows-scaling status
unchanged from 13E (no new control type introduced — plain `TextBlock`s inherit normal DPI-aware
layout).

### 16. Tests added / final validation

22 new/changed tests: 6 in `ParkHoursCalculatorTests` (Domain — buyout `NorthGateOpening` assertion +
5-case midnight-crossing `Theory`), 10 in the new `AssociateViewModelEarlyRideTimeTests` (Ui —
normal/Manual live/Manual cleared/buyout/Closed/NotPublished/Unavailable/NotCheckedYet/stale-LKG/
waterpark-independence/no-network), 2 in `AssociatePassdownPdfGeneratorTests` (Documents — derived-start
line, Closed-day fabrication guard), 3 in `FinalizedPassdownSnapshotBuilderTests` (Documents — frozen
North Gate/ERT value, buyout-derived value, later-hours-change immutability), 2 replacing 1 in
`OperationsDashboardPresentationTests` (Ui — known-hours derived text, unknown-hours fallback), plus
markup/automation-name updates in `AssociateViewAccessibilityTests`. Backward compatibility: no
`AssociateDraftPayload`/serializer code was touched at all, so the pre-existing
`AssociateViewModelDraftEditingTests` round-trips of free-typed `EarlyRideTimeDetailsText` (including
legacy strings that happen to embed a time, e.g. `"9:00 AM – 10:00 AM, first two rows only"`) already
prove old drafts keep loading/saving unchanged — no new fixture was needed because nothing in the
persisted shape changed.

Full suite (`dotnet test CarowindsPassdown.slnx --filter Category!=LiveSmoke`), re-run three times
consecutively: **1587/1587 passing** (Domain 105, Application 453, Documents 106, Infrastructure 350,
Ui 573) — up from the 1565/1565 baseline (+22). `dotnet build CarowindsPassdown.slnx` — **0 warnings, 0
errors.** One known pre-existing Avalonia-headless flake was observed during full-suite runs
(`DashboardViewTests` — `AvaloniaPropertyDictionaryPool` "Stack empty" under parallel XAML population, a
different test/parameter case each time), the same documented parallelism race recorded in Phase
6D/12B/13A/13B/13D/13E against other views; `DashboardView.axaml` was not touched by this phase, the
same 3 test classes most relevant to this phase's own changes
(`AssociateViewModelEarlyRideTimeTests`/`AssociateViewAccessibilityTests`/
`OperationsDashboardPresentationTests`) passed cleanly and reliably in isolation across 3 consecutive
runs, and two immediately-following full-suite re-runs were both fully green (573/573 Ui). 1280×860 and
960×640: no new automated DPI test exists in this codebase (unchanged from every prior phase); the ERT
change adds no new control type and stays inside the existing grid, so no separate Windows-VM check is
flagged. Windows scaling status: unchanged/DEFERRED BY DESIGN, same as every prior phase (real
Windows-host validation remains manual, per `docs/phase-7e-windows-validation-checklist.md`).

### 17. Work deferred to 13G–13H

Unchanged from every prior phase's own deferral list: rename/branding, Alerts, Manager workflow, the
final release rebuild, and real Windows-host scale/DPI validation.

## Git checkpoint (Phase 13F)

Early Ride Time simplified end to end: the supervisor no longer types a start time — it derives from
`EffectiveHours.NorthGateOpening` (effective park opening − 30 minutes), the same value already used for
"North Gate" everywhere in the app (no new Domain type). Manual Hours changes it live with no save
round-trip; Private Buyout drives it identically to North Gate (resolved from spec Part L's own
constraint, not guessed); Closed/NotPublished/Unavailable never fabricate a time; Carolina Harbor never
drives it. Rides remain fully editable, autosaved, and printed exactly as before. PDF/finalized History
print the same frozen derived value and are immune to later live-hours changes (explicit regression
test). Zero schema/migration changes (DB 11, draft 3, snapshot 1), zero network/refresh-path changes, 22
new tests, full suite green (1587/1587), build clean (0/0). One local commit follows; 13G is explicitly
not started.

## Phase 13G — Visible rename to "Carowinds Operations" and branding/icon

Executes against §27–30's rename audit and this phase's own task brief. Purely a branding/rename
pass: no persistence, retrieval, business-rule, or UI-layout change. The product's visible name
becomes "Carowinds Operations" everywhere a user actually sees it; every local-data compatibility
identifier the brief calls out as a hard requirement was left byte-for-byte unchanged.

### Visible rename

Every user-facing "Carowinds Passdown" / "Carowinds Admissions Operations" occurrence became
"Carowinds Operations": the main window title and brand band (`Views/MainWindow.axaml` — the band's
second line changed from "ADMISSIONS OPERATIONS" to "OPERATIONS", "CAROWINDS" unchanged), the
Associate Passdown PDF's small-caps header line (`AssociatePassdownPdfGenerator.ComposeHeader`, was
"CAROWINDS ADMISSIONS OPERATIONS"), the diagnostic summary's heading line
(`DiagnosticReportFormatter.BuildSummaryText`), three user-facing suggested export filenames — the
diagnostics ZIP (`Carowinds-Operations-Diagnostics-yyyyMMdd-HHmm.zip`,
`DiagnosticExportService.BuildZipAsync`), the "Create PDF" save-dialog filename
(`Carowinds-Operations-yyyy-MM-dd.pdf`, `AssociateViewModel.ExportPdfCommand`), and the History
"Save PDF Copy" filename (same convention, `FinalizedPassdownPdfRetrievalService.GetPdfAsync`) — the
Task Scheduler task's *description* field text (not its name — see below,
`Install-ScheduledRefresh.ps1`), the two double-click helper scripts' visible console prose
(`Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd`), and the nontechnical
`README-portable-release.md` / `TESTER-GUIDE.txt` titles. `App.axaml`'s brand-identity doc comment
and `Directory.Build.props`' `<Product>` element were updated to match. "Associate Passdown" /
"Passdown History" / "Create PDF" — the document/workflow terminology, not the app's own name — were
deliberately left untouched everywhere, per the brief's own explicit instruction; the PDF still
prints "Associate Passdown" as the document title directly beneath the renamed "CAROWINDS
OPERATIONS" small-caps line.

### Icon / branding

The user-supplied Phase 13G asset pair (`Assets/logo.ico` — a genuine multi-resolution Windows icon,
independently verified by parsing its directory entries: 16/24/32/48/64/72/96/128/256 px, all 32bpp
with alpha, no upstream `identify`/`magick` tool available on this dev host so parsed by hand against
the ICO file-format spec; `Assets/logo.png`, 1254×1254 source) is now the one canonical branding
asset, wired everywhere the app previously used the stock Avalonia template icon or nothing at all:
`Views/MainWindow.axaml`'s `Icon` attribute (was literally `/Assets/avalonia-logo.ico` — the unused
Avalonia project-template default, never previously replaced), and a new
`<ApplicationIcon>Assets\logo.ico</ApplicationIcon>` in `Carowinds.Passdown.Desktop.csproj` (there was
no `ApplicationIcon` property at all before this phase, so the published `.exe` never had its own
icon resource). The now-fully-unreferenced `Assets/avalonia-logo.ico` (the stock template asset) was
deleted rather than left as an orphaned second icon, per the brief's "prefer one canonical source
asset" instruction. The shell's existing small "ascending-track" geometric mark (plain
`Rectangle`s in the brand band) was left as-is — restrained, already original, not a redesign target
this phase — no separate logo image was added into the header itself. No logo/icon artwork was
authored or altered by this phase (brief: "do not redesign the logo").

### Executable icon validation (Windows/manual status)

No Windows host is available this session (same limitation recorded in every prior Phase 7/9/10/12
rebuild). What *was* verified directly, cross-platform: `dotnet build
src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64
--self-contained false -p:PlaywrightPlatform=win-x64` succeeds with the new `ApplicationIcon`
property (0 warnings/errors) and produces a real apphost `Carowinds.Passdown.Desktop.exe`; a direct
byte-string search of that built `.exe` confirmed a distinctive slice of `Assets/logo.ico`'s own
bytes is actually embedded inside it (the same "search the actual built binary for real evidence, not
just trust the build succeeded" method the Phase 12F release manifest used for feature-symbol
verification); the generated `Carowinds.Passdown.Desktop.AssemblyInfo.cs` for that same win-x64 build
confirmed `[assembly: AssemblyProductAttribute("Carowinds Operations")]`. Visual confirmation that
Windows Explorer/the taskbar actually renders the icon correctly at each resolution, and that it
reads well against both light and dark taskbars, remains **NOT EXECUTED** — deferred to real
Windows-host validation per `docs/phase-7e-windows-validation-checklist.md`'s existing pattern, same
as every prior phase's Windows-only gaps.

### Internal names intentionally retained

Per the brief's explicit "internal code/project identity stays Carowinds.Passdown" default: every
`Carowinds.Passdown.*` namespace, project, and folder name; the
`Carowinds.Passdown.Desktop.Carowinds.Passdown.Desktop.exe` build output name; the
`CarowindsPassdown.slnx` solution filename (used as a repo-root marker by multiple tests, including
the new ones this phase added); the `Passdown*` Avalonia resource keys in `App.axaml`
(`PassdownBrandDarkBrush`, `PassdownAccentBrush`, etc.); and the OS-temp folder
`TempFileFinalizedPdfLauncher` uses for launching a History PDF (`%TEMP%\Carowinds Passdown\History`)
— none of these are ever visible to a normal user, so renaming any of them would be pure internal
churn for zero user-facing benefit, exactly as §27–30 recommended deferring.

### AppData / database compatibility decision

**No path changed.** `AppPaths.LocalApplicationDataDirectory` still resolves to
`%LOCALAPPDATA%\Carowinds Passdown` (`AppPaths.cs` — byte-for-byte unchanged), the database filename
stays `carowinds-passdown.db`, and `scheduled-refresh.lock`/the logs directory are untouched. This is
the hard compatibility requirement the brief calls "high priority," and it holds structurally, not by
convention: no code anywhere in this diff constructs a path containing the string "Carowinds
Operations." A new regression test,
`Phase13GAppDataRenameCompatibilityTests.ExistingLegacyAppData_DraftHistoryAndDeepRefreshState_AllLoadUnchanged_UnderTheRenamedApp`
(Infrastructure), seeds a directory literally named `Carowinds Passdown` (standing in for
`%LOCALAPPDATA%`, never the developer's real one) with a draft, a finalized/History row (with frozen
PDF bytes), and Deep Refresh status via the real repositories/migrations — exactly the set the brief's
"drafts / History / Deep Refresh state" list names — then reopens it with a fresh connection factory
(simulating a relaunch under the renamed build, exactly as `App.axaml.cs` does on every real launch)
and asserts all three reload correctly, and that no sibling `Carowinds Operations` directory was ever
created. A second test,
`AppPaths_LocalApplicationDataDirectory_StillUsesTheLegacyFolderName_NotTheRenamedProductName`, pins
the exact literal as a named regression against a future careless find-and-replace.

### Task Scheduler naming/migration decision

**Option A — the legacy task name is preserved internally, unchanged: `"Carowinds Passdown Daily
Refresh"`** (`ScheduledTaskPlan.TaskName`, `ScheduledTaskStatusProvider.TaskName`,
`Install-ScheduledRefresh.ps1`/`Uninstall-ScheduledRefresh.ps1`'s `$TaskName`). This matches §27–30's
own prior recommendation and this phase's own brief ("prefer the simpler solution if both are
functionally equivalent"): the install script's idempotency (`Register-ScheduledTask -Force`) keys
entirely on this exact name, so an upgrading user's existing task is found and updated in place with
zero migration code, zero risk of a stray detect-and-rename step either duplicating the task or
orphaning the old one, and the task name is never shown inside the app itself (only in Windows' own
Task Scheduler UI, which the product owner is not expected to open). What *did* change: the task's
*description* field text (`Install-ScheduledRefresh.ps1`'s `$description`, a Task-Scheduler-UI-only
string, not an identity/matching field) now reads "Runs the Carowinds Operations unattended source
refresh," and a code comment above `$TaskName` documents the decision explicitly for a future reader.
No code change was needed in `ScheduledTaskPlan.cs`/`ScheduledTaskStatusProvider.cs` (both are exactly
the same as pre-13G), so the entire pre-existing `ScheduledTaskPlanTests`/
`ScheduledTaskStatusProviderTests` suite already constitutes this phase's full task-compatibility
regression coverage: task-name stability, the explicit
`Build_DifferentExecutablePaths_ShareTheSameTaskName_...` duplicate-prevention proof, correct
executable/action/working-directory construction, and (by construction — `ScheduledTaskPlan.Build`
takes no credential parameter at all) no admin/password behavior. No new task-compatibility test was
added because nothing on this path changed.

### Script / user-facing wording

`Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd`'s visible console text ("Enabling
automatic refresh for this copy of Carowinds Operations...", "Carowinds Operations will check for due
updates each day...", "Keep this file inside the extracted Carowinds Operations folder...") and
`Install-ScheduledRefresh.ps1`'s Task Scheduler description string were updated; both scripts'
executable-name checks (`Carowinds.Passdown.Desktop.exe`) and task-name targeting are untouched, per
the executable-filename and Task-Scheduler decisions above.

### PDF / Associate Passdown terminology

Unchanged, confirmed correct on inspection: the PDF's document title stays "Associate Passdown"
(`ComposeHeader`, line directly below the renamed small-caps app-name line); the History section, the
"Create PDF" button, and every "Passdown" reference describing the daily document/workflow (not the
app) were left alone throughout the audit — the brief's own explicit instruction, and semantically
correct regardless: the app is Carowinds Operations, the document it produces is still the Associate
Passdown.

### Repository stale-brand search result

Final `grep -rn "Carowinds Admissions Operations\|Carowinds Passdown"` across `src/`, `tests/`,
`packaging/`, and `docs/` (excluding `obj/`/`bin/`/gitignored `artifacts/`) classifies every remaining
hit into exactly three buckets, none left as a stale-branding bug:

1. **Intentional legacy compatibility identifiers** (never renamed, by design this phase):
   `AppPaths.cs`'s `"Carowinds Passdown"` folder literal; `carowinds-passdown.db`; the Task Scheduler
   task name (`ScheduledTaskPlan.cs`/`ScheduledTaskStatusProvider.cs`/both PowerShell scripts'
   `$TaskName`); `TempFileFinalizedPdfLauncher`'s OS-temp subfolder; every literal
   `%LOCALAPPDATA%\Carowinds Passdown` path string in `docs/scheduled-refresh-task-scheduler.md`,
   `docs/support-troubleshooting.md`, `README-portable-release.md`, `TESTER-GUIDE.txt`, and
   `DiagnosticReportFormatter`'s printed summary text; `Carowinds.Passdown.Desktop.exe` and every
   `Carowinds.Passdown.*` namespace/project reference.
2. **Legitimate workflow/document terminology** (never in scope for this rename): every "Associate
   Passdown" / "Passdown History" / "Create PDF" occurrence describing the document or the app's
   internal type names (`IAssociatePassdownService`, `FinalizedPassdown`, etc.), which are not
   user-facing product-name text at all.
3. **Historical record, intentionally not rewritten**: `docs/release-manifest.md` (the Phase 12F
   release manifest — a dated record of a specific already-shipped ZIP, its exact SHA-256, and the
   product name as it was at that time; it already forward-references "the app rename to 'Carowinds
   Operations' is Phase 13 scope"), `docs/release-strategy.md`'s Phase 7–12 narrative sections, and
   `AGENT_BUILD_SPEC.md`/`CLAUDE.md`'s own titles (governance/spec documents, not a rendered product
   surface). Rewriting these would misrepresent what was actually true/shipped at each historical
   point; 13H's own release rebuild is the natural place to add a new, current manifest entry rather
   than editing history. `docs/architecture.md`'s handful of historical mentions (describing the
   pre-13G "Carowinds Admissions Operations" retheme and the pre-13G `<Product>` value) were left for
   the same reason — a developer architecture doc narrating what earlier phases did, not a live
   product surface.

No occurrence was found that this classification could not place — nothing was silently
find-and-replaced without inspection, and nothing was left unaudited.

### Schema / data

Confirmed directly in code (`grep` of the three `CurrentSchemaVersion` constants and the migrations
directory — `Migration0011...` remains the newest file, no new migration file exists): **DB schema
11 (unchanged). `AssociateDraftPayload` schema 3 (unchanged). `FinalizedPassdownSnapshot` schema 1
(unchanged).** No migration was needed or added — this phase touched zero persistence code paths
(only path/filename *constants* used for reading/writing are unaffected — see AppData decision
above).

### Tests added / final validation

15 new tests: 11 in the new `ProductBrandingTests` (Ui/Release — window title, old-branding-removed,
stale-"Carowinds Passdown"-removed, "Associate Passdown" workflow label retained, window icon asset
path + file existence, `ApplicationIcon` csproj property, `Directory.Build.props` `<Product>`, PDF
header renamed + "Associate Passdown" retained, plus two explicit compatibility guards pinning the
AppData folder name and Task Scheduler task name as unchanged) and 2 in the new
`Phase13GAppDataRenameCompatibilityTests` (Infrastructure — the full draft/History/Deep-Refresh
reload-under-rename proof, and the `AppPaths` literal-name guard); plus 9 existing test literals
updated to the new suggested-filename conventions (`FinalizedPassdownPdfRetrievalServiceTests`,
`AssociateViewModelPdfExportTests`, `FakeFinalizedPassdownPdfRetrievalService`,
`PassdownHistoryViewModelTests` ×3, `FakeDiagnosticExportService`, `DiagnosticExportServiceTests`) and
1 (`ReleaseVersionTests.DesktopAssembly_ProductNameIsTheUserFacingApplicationName`) updated to assert
`"Carowinds Operations"` — the "coupled edit required" §27–30 flagged in advance.

Full suite (`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"`): **1600/1600
passing** (Domain 105, Application 453, Documents 106, Infrastructure 352, Ui 584) — up from the
1587/1587 baseline (+13: 2 Infrastructure + 11 Ui; the filename-literal edits above did not add test
count). `dotnet build CarowindsPassdown.slnx` — **0 warnings, 0 errors.**

### Work deferred to 13H

Per the brief: the final release ZIP (and its `Carowinds-Operations-<version>-win-x64.zip` naming —
`Build-PortableRelease.ps1`'s `$releaseName` and `Publish-Windows.ps1`'s console prose were
deliberately left reading "Carowinds Passdown" this phase, since the brief explicitly reserves "final
release ZIP naming" and "final packaging/release" for 13H; changing the naming variable now without
also cutting a new manifest/rebuild would leave an inconsistent half-state) was not built. Also
unchanged from every prior phase's own deferral list: Alerts, the Manager workflow, and real
Windows-host icon/scale/DPI validation (`docs/phase-7e-windows-validation-checklist.md`).

## Git checkpoint (Phase 13G)

The visible product is now "Carowinds Operations" everywhere a user sees it — window title, brand
band, PDF header, diagnostic summary, three export-filename conventions, and every nontechnical
script/doc surface — while "Associate Passdown"/"Passdown History"/"Create PDF" (the actual
document/workflow) were correctly left alone. The approved Phase 13G icon (`Assets/logo.ico`) is now
the canonical window icon and the published `.exe`'s embedded icon resource (verified by a direct
byte-search of a real win-x64 build, since no Windows host is available); the stale unused Avalonia
template icon was deleted. Every hard local-data compatibility identifier — the `%LOCALAPPDATA%
\Carowinds Passdown` folder, `carowinds-passdown.db`, `scheduled-refresh.lock`, and the
`"Carowinds Passdown Daily Refresh"` Task Scheduler task name — was left byte-for-byte unchanged and
is now covered by an explicit new regression test proving drafts/History/Deep-Refresh all reload
correctly from a legacy-named data directory under the renamed app, with no second "Carowinds
Operations" directory ever created. Zero schema/migration changes (DB 11, draft 3, snapshot 1), zero
persistence/retrieval/business-rule changes, 15 new tests, full suite green (1600/1600), build clean

## Phase 13H — final Phase 13 integration audit and release closeout

Phase 13H is the final Phase 13 sub-phase: a release-validation pass over 13A–13G as one integrated
product, followed by the actual final win-x64 portable release rebuild (deferred by every sub-phase
through 13G). No new feature was added; zero production `src/` code changed.

### Audit findings

A targeted audit sampled the cross-phase interaction points most likely to hide a defect no single
sub-phase's own isolated audit would catch, rather than re-deriving every 13A–13G finding from scratch:

- Startup ordering (`App.axaml.cs`/`StartupCatchUpRefresh.cs`) — unchanged, window assigned before the
  fire-and-forget catch-up call, finalize-before-refresh and shared-lock guarding intact.
- Hours/ERT domain purity (`ParkHoursCalculator.cs`/`EffectiveHours.cs`) — confirmed no waterpark
  parameter exists anywhere in the type; `NorthGateOpening` null-propagates correctly for
  Closed/NotCheckedYet; override precedence matches spec §8.2 exactly.
- Manual Hours/Buyout typed entry → ERT reactivity, and custom product override → PDF → finalization →
  History immutability — the two most complex cross-cutting Phase 13 paths — were independently
  audited end-to-end (file:line evidence in `AssociateViewModel.cs`, `AssociateParkHoursComposer.cs`,
  `AssociateProduct.cs`, `AssociatePassdownDocumentModelBuilder.cs`,
  `FinalizedPassdownSnapshotBuilder.cs`). Both **PASS**: ERT reactively recomputes from freshly
  committed typed values via one shared `ParkHoursCalculator.Calculate` call; an unparseable typed
  value never reaches the committed field; waterpark hours are carried on a fully separate
  presentation path that never touches `EffectiveHours`. The custom-price override
  (`AssociateProduct.EffectiveDisplayPrice`) is the one value both PDF generation and finalization read
  from a single shared document-model build; the frozen snapshot stores plain formatted
  strings/decimals, never a live catalog reference. One non-blocking test-coverage observation (no
  existing test re-mutates a catalog value *after* finalization to prove immutability end-to-end,
  though the guarantee is structural) — not treated as a defect.
- Branding sweep — no stale "Carowinds Admissions Operations" hit and every remaining "Carowinds
  Passdown" hit in `src/`/`tests/`/`packaging/` confirmed to be one of the four intentional legacy
  compatibility identifiers. No `LastVerified`/Deep Refresh diagnostic text found anywhere in the PDF
  generation path.
- Schema/scope confirmation — `Migration0011CreateDeepRefreshStatusTable` (Phase 13D) remains the
  newest migration; `AssociateDraftPayload` schema 3, `FinalizedPassdownSnapshot` schema 1;
  `AppSection` still has exactly 5 values (no Manager, no Alerts).

No genuine defect was found. Full detail, verdict table, and the release rebuild itself are in
`docs/release-strategy.md` "Phase 13H — final Phase 13 integration audit and release closeout"; the
shipped artifact's exact identity is in `docs/release-manifest.md`.

### Test suite stability

Five full non-live suite runs, zero source changes between them: three clean at 1600/1600, two each
hit one failure — both the pre-existing, previously-documented (Phase 6D) Avalonia headless
cross-assembly parallelism flake (`Stack empty.` in `AvaloniaPropertyDictionaryPool.Get()` during
`XamlIlPopulate`), on two different, unrelated View-construction tests neither belonging to any Phase
13 file. Not a regression.

### Release rebuild

`packaging/windows/Build-PortableRelease.ps1`'s `$releaseName` and `packaging/windows/Publish-Windows.ps1`'s
console prose were updated to `Carowinds-Operations` (deferred from 13G). The final win-x64 portable
ZIP was rebuilt manually (no `pwsh` on this dev host, same as every prior rebuild), byte-verified
against a fresh extraction, and hashed — see `docs/release-manifest.md` for the exact artifact
filename, size, and SHA-256.

### Windows validation

**NOT EXECUTED** — no Windows host or VM was available this session, the same limitation every prior
Phase 7E/9F/10F/11F/12F/13G rebuild has carried. 100/125/150% scaling, live icon/taskbar rendering, and
real Task Scheduler install/run were not executed; confidence comes from the automated suite's
real-XAML-loader construction/layout checks and the byte-level icon/Product-attribute verification
performed during the release rebuild.

### Phase 14 / Phase 15

Both confirmed not started — no Alerts/Data Confidence/notifications/monitoring code exists anywhere
in the codebase, and `AppSection` has no Manager value.
(0/0). One local commit follows; 13H (final packaging/release rebuild) is explicitly not started.

---

## Phase 15A — Product-section grouping fix and anonymous ordinary Bring-A-Friend dated pricing

Fixes two Associate Passdown/PDF correctness issues left over from Phase 13C: (1) a product could
print under two differently-named sections for the same real-world offer, and (2) ordinary
Bring-A-Friend's exact per-date price was believed unreachable without passholder authentication —
new live evidence shows it is not.

### 1–5. Root cause: display grouping keyed directly by retrieval `SourceCategory`

Every prior phase composed the Associate/PDF product list by grouping `AssociateProduct` rows
directly on their raw retrieval `SourceCategory` (`AssociatePassdownDocumentModelBuilder.BuildProductCategories`,
`AssociateViewModel.BuildProductSections`) — a category that identifies *where a product was
retrieved from*, not *where it should be displayed*. Three concrete consequences, each confirmed by
direct inspection before any fix was written:

- **Bring-A-Friend Duo duplication**: `PromotionsCatalogSource` generically discovers "Celebrate
  America! Bring-A-Friend Duo" as a catalog card (category `Promotions`, Sanity id
  `o1gUjW0O273eyMG69L54eV`, bundle $50/2 friends), while `DatedBringAFriendDuoSource` (Phase 13C)
  independently retrieves "Celebrate America! Bring-A-Friend Duo Offer " (category
  `BringAFriendDuo`, accesso id `10097207`, per-person $25) for the selected date. Same real-world
  offer, two different retrieval identities (different id, different name, different category, by
  design — see Phase 13A's category-scoped identity precedence), so both rows printed, each under its
  own section heading.
- **Military/First Responder** (`SourceCategory.MilitaryFirstResponder`) got its own dedicated PDF
  section purely because it has its own retrieval category — there is no product reason it needs to
  be visually separated from ordinary Daily Admission.
- **SCarowinds Bundles**: `PromotionsCatalogSource` also generically discovers "SCarowinds Bundles"
  (accesso keyword `fallwizard`, an admission+maze-access bundle) on the same `daily-tickets` page as
  every other Promotions card, landing it under the `Promotions` category/section even though its
  content is SCarowinds-specific and belongs alongside the dated SCarowinds/Haunt products instead
  (`docs/retrieval/dated-haunted-source.md` already documented `fallwizard` as *not* the haunted-pass
  checkout keyword `HAPWizard` uses, so no structural cross-category duplicate exists today — but its
  *section* was still wrong).

**Fix — one authoritative resolver, not independent boolean filters** (spec §5): a new
`ProductDisplaySection` enum (`DailyAdmission`, `FastLane`, `SCarowinds`, `SeasonPasses`, `AddOns`,
`Promotions`) and `ProductDisplaySectionResolver` (`Application/Associate/ProductDisplaySection.cs`)
provide the *only* place display-section membership is decided:

| `SourceCategory` | Resolved section |
|---|---|
| `DatedAdmissionTickets` | `DailyAdmission` |
| `MilitaryFirstResponder` | `DailyAdmission` |
| `DatedFastLane` | `FastLane` |
| `HauntedAttractions` | `SCarowinds` |
| `SeasonPasses` | `SeasonPasses` |
| `AddOns` | `AddOns` |
| `Promotions` | `Promotions`, unless the product's own display name contains "SCarowinds" (a generic, content-based rule — not a one-off match on a single product's exact string — then `SCarowinds` |
| `BringAFriendDuo` | *(none)* — its real-world offer is already shown once via its Promotions catalog counterpart |
| `DatedBringAFriend` (new, §7 below) | *(none)* — merged into the existing Promotions "Bring-A-Friend Ticket" row rather than shown independently |

`AssociatePassdownDocumentModelBuilder.BuildProductCategories` (PDF) and
`AssociateViewModel.BuildProductSections` (Associate Products screen) both now group by
`ProductDisplaySectionResolver.Resolve(product)` instead of raw `Category`, and both skip a `null`
resolution entirely (no section, no row). `PassdownSectionSelection`'s include/exclude toggles
(`IncludeDailyAdmission`, `IncludeHauntedAttractions`, ...) are now looked up by the *resolved
section* too (`IsSectionIncluded`/PDF, existing toggles/UI) — so Military rows are governed by the
Daily Admission toggle and an SCarowinds-reclassified Promotions card by the SCarowinds toggle, since
that is where they now visually live. The persisted `SourceCategory`/`CatalogProductKey`/
`AssociateProduct.Category` identity is completely unchanged — this is presentation-only, per
CLAUDE.md's "do not change persisted catalog category/identity merely to achieve display grouping."

PDF section names are looked up via a `PdfCategoryDisplayNames` now keyed on `ProductDisplaySection`
(`Documents/Associate/PdfCategoryDisplayNames.cs`); the Desktop Products screen gets a parallel
`ProductDisplaySectionDisplay` (`Desktop/Presentation/ProductDisplaySectionDisplay.cs`) — both
deliberately duplicated two-line lookup tables, matching the pre-existing
`SourceCategoryDisplay`/old `PdfCategoryDisplayNames` convention (Documents must not depend on
Desktop). `SourceCategoryDisplay.FriendlyName` itself (used by Dashboard health rows, refresh-progress
toasts, and category-status banner text — all *retrieval*-identity display, unrelated to product
grouping) was left untouched, plus one added case for the new `DatedBringAFriend` category.

`TierFor` (PDF Key Selling / Promotional / Reference visual tiers) is now keyed on
`ProductDisplaySection` too: Military rows render in the same Key Selling tile style as the rest of
Daily Admission, which already supports a real "$80 Gate Price" comparison identically to the old
Promotional tier — no pricing information is lost by the retiering.

### 6. Bring-A-Friend Duo — removed the dedicated section, kept the retrieval untouched

`DatedBringAFriendDuoSource`/`SourceCategory.BringAFriendDuo` still refresh on the same daily cadence,
participate in Check Online/Deep Refresh/UserForced identically, and still feed the Dashboard's data
health row (unchanged: `StatusTrackedDatedCategories` still includes it, and
`OperationsDashboardService`'s health breakdown is orthogonal to product-section display). Only its
*display* composition changed: `ProductDisplaySectionResolver.Resolve(SourceCategory.BringAFriendDuo)`
returns `null`, so it is never given its own PDF section or Associate Products-screen section (neither
a product row nor a zero-row status banner) — its real-world offer is represented for display purposes
solely by the Promotions catalog card ("Celebrate America! Bring-A-Friend Duo"), which already carries
the correct bundle-total/per-person breakdown (`PriceSemanticsClassifier.TryParseBundle`, unchanged).
No name-based special case was added for this — the fix is the category-level `null` resolution, which
applies to the category as a whole, not a string match on the offer's name (spec §2's "do not
special-case only the visible string if a generic category/grouping fix is possible").

### 7–13. Anonymous ordinary Bring-A-Friend exact dated pricing — Phase 13C's limitation revisited

**Source audit.** Live investigation (2026-08-27, via the Browser tool against the real
`sixflags.com`/`cf-ca.buy.sixflags.com`/`ecomm.api.cf.accessoticketing.com` surfaces) traced exactly
how the anonymous Special Offers page produces a date-varying Bring-A-Friend price:

1. `sixflags.com/carowinds/special-offers`'s Bring-A-Friend "Buy Now" button embeds
   `https://cf-ca.buy.sixflags.com/packageList/keyword/SnapWizard?` — the same generic admission
   storefront Phase 13C already found (no Bring-A-Friend card there; confirms 13C's `SnapWizard`
   finding was correct as far as it went).
2. Separately, the storefront's left-nav "Daily Tickets → Bring-A-Friend Ticket" link (not
   previously followed) opens `https://cf-ca.buy.sixflags.com/wizardCalendar/BAFWizard/BAFWizardDailyTickets`
   — a dedicated "Select a date to begin!" calendar page, reachable with **no login prompt at any
   point**, that lists both "Celebrate America! Bring-A-Friend Duo Offer" ($25.00, every date) and
   "Bring-A-Friend Ticket" with a price that visibly changes per selected calendar date (was $80.00
   → $39.00 on Aug 29, 2026; → $35.00 on Aug 30, 2026 — both live-observed, matching the locked
   regression values exactly).
3. Intercepting this page's own network calls (`performance.getEntriesByType('resource')`) confirmed
   it is powered by the **same existing accesso `getkeywordcalendar` endpoint** every other dated
   source already calls (`https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar`),
   with a keyword never previously tried: **`BAFWizardDailyTickets`**. Phase 13C's guessed-keyword
   list tried bare `BAFWizard` (which returns `"No dates found that meet requirements"`) but never the
   "Wizard + DailyTickets" suffix pattern every other admission-family keyword already follows
   (`SnapWizardDailyTickets`).
4. Replaying the exact request shape `AccessoKeywordCalendarRequest` already uses (same headers, same
   body fields, keyword swapped to `BAFWizardDailyTickets`) directly against the live endpoint,
   independent of the storefront page, confirmed it anonymously — no cookies, no session id beyond the
   endpoint's own ephemeral `session_id`, no passholder credential of any kind:
   - 2026-08-29: `{"id":"10094895","name":"Bring-A-Friend Ticket","CT":{"retail_amount":"39.00"}}`
   - 2026-08-30: `{"id":"10094893","name":"Bring-A-Friend Ticket","CT":{"retail_amount":"35.00"}}`
   - A 5-date horizon window (Aug 29 – Sep 7) confirmed the pattern holds across multiple dates
     (weekday $39.00, Saturday $35.00, Labor Day $29.00), each date carrying its own package id.
   - Every response for this keyword also always carries "Celebrate America! Bring-A-Friend Duo
     Offer" alongside it (identical package id/price to `SUBAF50`'s own response) — a shared-keyword
     coincidence the new adapter deliberately filters out by package name (§9).
   - **No response for this keyword, at any date, ever included a comparison/"was" price field** — the
     official "$80 Gate Price" figure lives only on the static Promotions catalog card, already
     retrieved by `PromotionsCatalogSource`.

**Conclusion**: Phase 13C's "passholder-login-gated" finding was an incomplete investigation, not a
platform limitation — the dated price was reachable anonymously all along, via a keyword nobody had
tried. `docs/retrieval/special-ticket-sources.md` and `docs/retrieval/catalog-source-inventory.md`
have both been corrected in place (old conclusion struck through/marked superseded, not deleted, per
CLAUDE.md's "never invent data" and this task's "clearly record that Phase 15A supersedes that
specific finding").

### 8–9. New source and schema

`DatedBringAFriendSource` (`Infrastructure/Retrieval/Sources/DatedBringAFriendSource.cs`) — structurally
identical to `DatedBringAFriendDuoSource` (primary single-day request, horizon-window fallback,
`DirectApi` strategy) — under a new `SourceCategory.DatedBringAFriend`. Its `BuildProduct` selects only
the package named exactly `"Bring-A-Friend Ticket"` from whatever the keyword returns, ignoring the
co-returned Duo package by name (never both, never the wrong one). `DatedBringAFriendPayload`/
`DatedBringAFriendProduct` (`Application/Retrieval/DatedBringAFriendPayload.cs`) carry no comparison
field at all — never fabricated, matching live evidence.

**No schema change**: `SourceCategory` is persisted as its enum name (a string) in
`SqliteDatedResultRepository`/`SqliteSourceStateRepository`, so a new enum value needs no migration.
Database schema stays 11, `AssociateDraftPayload` stays 3, `FinalizedPassdownSnapshot` stays 1.

### 10–13. Composition — merge, not a third row

`AssociatePassdownService.MergeExactDatedBringAFriend` (called once per `ComposeAsync`, after the
catalog-categories loop) implements the precedence rule directly on the already-composed product list:
if a verified `DatedBringAFriend` result exists for the *selected* date, it locates the existing
Promotions-category row whose display name is exactly `"Bring-A-Friend Ticket"` (the literal official
string shared by both surfaces — the catalog card's own title and the dated package's own name; not a
guess) and replaces it **in place** — same `ProductIdentity`, same list position — with the exact
price, `PriceKind.ExactDated`, `DateSpecific = true`, `PriceQualifier = null` (dropping "Starting
From"), and its own verification info, while leaving `ComparisonPriceAmount` (the catalog row's
already-sourced "$80 Gate Price") completely untouched. If no exact value exists for the selected date
(no data, a different date, or a failed attempt), the catalog row is never touched at all — the
generic "Starting From $35" value remains exactly as before. Reusing the catalog row's own identity
(rather than composing a new dated identity) means a manager's existing per-product draft
override/include-on-PDF toggle survives regardless of whether today's date has an exact price.
`PdfProductRow`/PDF rendering needed no change: `PriceKind.ExactDated` + `PriceQualifier = null` already
suppresses "Starting From" text while `ComparisonPriceText` still renders struck through (Phase 13C's
existing rendering, unchanged).

Deliberately **not** attempted: matching by any structural id (the catalog card's Sanity id and the
dated package's accesso id are unrelated, exactly as with BAF Duo) — the shared official display name
is the only trustworthy link, and it is a real, observed, official string on both surfaces, not an
invented one.

### 14. Bring-A-Friend Duo regression

Untouched and re-verified: `LoadAssociateDateAsync_OrdinaryBringAFriendMerge_DoesNotAffectBringAFriendDuo`
proves the new ordinary-BAF merge and the existing Duo catalog/dated composition coexist in the same
`ComposeAsync` call without cross-contamination — the merge only ever touches a row whose display name
is exactly `"Bring-A-Friend Ticket"`, never `"Celebrate America! Bring-A-Friend Duo"`.

### 15. Check Online / Deep Refresh integration

No refresh-architecture change: `DatedBringAFriendSource` registers as an ordinary
`DatedSourceRegistration` in the composition root (`App.axaml.cs`), so `RunUserForcedRefreshAsync`
(Check Online) and `RunDeepRefreshAsync` (Deep Refresh) both already force-refresh it end to end,
identically to every other dated source, with no per-source allowlist to maintain. Because
`ComposeAsync` always re-reads whatever `DatedResultRepository` holds for the *currently selected*
date, a Check Online on the selected date immediately updates the merged Bring-A-Friend row on
recompose — no separate wiring was needed. `FreshnessPolicyCatalog` gives it the same daily 6 AM
cadence as every sibling dated category.

### 16. Tests added

- `ProductDisplaySectionResolverTests` (12) — pure resolver unit tests: Military → DailyAdmission, no
  dedicated Military section, both Bring-A-Friend dated categories → no section, SCarowinds-named
  Promotions card → SCarowinds not Promotions, Fast Lane/SeasonPasses/AddOns pass through unchanged,
  ordinary BAF stays Promotions, a brand-new generic promotion still falls into Promotions, a custom
  product resolves to `null`, every defined `SourceCategory` resolves to a valid section or `null`.
- `AssociatePassdownDocumentModelBuilderGroupingTests` (5, new file) — PDF integration: SCarowinds
  card never duplicated under Promotions, Fast Lane unaffected, ordinary BAF stays Promotions, a
  generic new promotion falls into Promotions, every rendered product across one 8-category
  composition appears in exactly one section.
- `AssociatePassdownDocumentModelBuilderPriceSemanticsTests` (+4): Military renders under Daily
  Admission not its own section; Bring-A-Friend Duo's dated category never renders its own section
  (and, with a catalog counterpart present, the offer renders exactly once, under Promotions); an
  Exact Dated Bring-A-Friend row never prints "Starting From" while still showing the "$80 was"
  comparison; a Starting-From-only row still prints "Starting From" (fallback unchanged).
- `AssociateViewModelProductEditingTests` (+2): Military renders under the Daily Admission Products
  screen section, not its own; a Bring-A-Friend Duo dated product with no catalog counterpart renders
  no section at all (no phantom empty-status banner either).
- `AssociatePassdownServicePriceSemanticsTests` (+6, composition-layer, real production
  `ComposeAsync`/`MergeExactDatedBringAFriend`, not hand-constructed domain objects only): Aug 29
  merges to $39.00 Exact Dated with the $80.00 comparison preserved and "Starting From" dropped; Aug
  30 merges to $35.00; two recorded dates plus a third, never-recorded date prove no cross-date
  leakage (the unrecorded date falls back to the generic $35 Starting-From floor, never treated as
  exact); a genuinely missing dated result falls back to Starting From; a *failed* dated attempt
  (`RecordFailedAttemptAsync`) also falls back rather than fabricating a value from an error; the new
  merge coexists with Bring-A-Friend Duo without cross-contamination.
- `DatedBringAFriendSourceTests` (17, new file, mirrors `DatedBringAFriendDuoSourceTests`) — contract
  tests against real, sanitized `fixtures/dated-bring-a-friend/` captures (`published-2026-08-29.json`,
  `published-2026-08-30.json` — genuine unmodified live captures; `horizon-window.json` — genuine
  5-date live capture) plus synthetic transport-edge-case fixtures (malformed/missing-service/
  no-dates-found/empty-entries, reused from the Duo family's own convention) and one new synthetic
  edge case (`published-missing-target-package.json`) proving the adapter errors defensively rather
  than fabricating a product when its target package name is absent. Explicitly proves the co-returned
  Duo package is never picked up, date-confirmation/isolation, past-date short-circuiting, horizon
  fallback, and — structurally, by construction — no credential/cookie/session field is ever sent
  (the request body/headers are built from the same `AccessoKeywordCalendarRequest` helper every other
  anonymous dated source already uses).
- `CompositionRootTests` (updated): registration count 10 → 11 (renamed
  `BuildServiceProvider_RegistersElevenSourceRegistrations`), expected dated-category set gained
  `DatedBringAFriend`.
- `AssociatePassdownServiceTests`/`OperationsDashboardServiceTests` (updated, not net-new): the
  status-tracked-category list and the Dashboard's "nothing ever checked" not-checked count both grew
  by one for the new category.

### 17. Final validation

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors. `dotnet test CarowindsPassdown.slnx
--filter "Category!=LiveSmoke"` — **1645/1645 passing** (baseline 1600 + 45 net new/changed), confirmed
clean on 3 of 4 consecutive full runs; the fourth hit the same pre-existing, previously-documented
(Phase 6D/13H) Avalonia headless cross-assembly parallelism flake (`Stack empty.` in
`AvaloniaPropertyDictionaryPool.Get()` during `XamlIlPopulate`), on `AssociateView`/
`OperationalNotesView` construction tests — neither file touched by this phase, not a regression.
Database schema 11, `AssociateDraftPayload` schema 3, `FinalizedPassdownSnapshot` schema 1 — all
unchanged, confirmed by inspection (no new migration file; §9 above).

### 18. Documentation updated

This section; `docs/retrieval/special-ticket-sources.md` (ordinary Bring-A-Friend section corrected
in place — old conclusion marked superseded, not deleted); `docs/retrieval/pricing-semantics.md`
(worked examples 1 and 2 both revised: example 1's "not retrievable anonymously" conclusion corrected,
example 2's "two separate sections" behavior corrected to reflect the removed dedicated section);
`docs/retrieval/catalog-source-inventory.md` ("no adapter added, by design" section corrected).

### 19. Deferred to 15B+

Everything not explicitly asked for in Phase 15A's scope: the pre-existing, accepted "2027 Gold Pass"
dual-listing under both `Promotions` and `SeasonPasses` (a different, already-documented and
explicitly out-of-scope multi-category duplication, unrelated to the three cases this phase fixes);
any change to the Dashboard's own compact category-breakdown lists (`DashboardDataHealthPresentation`/
`OperationsDashboardService.ProductCatalogCategories` already omit `BringAFriendDuo`/
`MilitaryFirstResponder` from their fixed two-group display today, pre-existing and unrelated to
product-section grouping — `DatedBringAFriend` was deliberately left out of those same fixed lists
too, for consistency, rather than expanding Dashboard UI scope); any Manager workflow (per CLAUDE.md,
still deferred); rebuilding the release ZIP (explicitly deferred by this phase's own instructions).

## Git checkpoint (Phase 15A)

Committed as `a2725ea` ("Phase 15A: fix Associate/PDF product-section duplication and discover
anonymous ordinary Bring-A-Friend dated pricing").

## Phase 15B — Operations Calendar month grid: full-width redesign

Phase 15B is visual/compositional only: the Calendar's underlying data, selection, navigation, and
routing behavior are all unchanged (spec's own "keep all current Calendar behavior" instruction). The
problem was that Phase 13D's equal-height fix (`Height="132"`, `MinHeight` removed) was functionally
correct — it made every cell's *height* uniform regardless of content — but never addressed *width*,
where the month still rendered as narrow, content-sized vertical strips with large unused horizontal
gaps between them.

### 1. Root cause of the narrow-strip appearance — found empirically, not guessed

`docs/phase13-hardening.md`'s own Phase 13D "test-harness honesty note" recorded that a plain
`view.Measure()`/`Arrange()` pass never realizes `ItemsControl`-generated day-cell containers in this
headless test harness, so per-cell pixel bounds were never previously provable here. This phase found
the missing piece: this test assembly's headless platform (`Avalonia.Headless`'s `UseHeadless`, see
`HeadlessTestSetup.cs`) *does* support a real, off-screen `Window` — constructing one, setting
`Content`, calling `Show()`, and pumping `Dispatcher.UIThread.RunJobs()` fully realizes every generated
container with real, trustworthy `Bounds`. Using that technique directly against the pre-15B view
(before any fix), against a real 1280×860 window:

- **Off-month day cells** (bare day number only) rendered **40px wide**.
- **In-month day cells** (day number + a few text lines) rendered **~94px wide**.
- Both numbers are far narrower than the ~124–183px a genuinely stretched 7-column grid should
  produce at that width — confirming the reported "narrow vertical strip"/"detached sliver" symptom
  directly, in pixels, not by inference.

Two independent, compounding causes were isolated by testing each in controlled isolation:

1. **Avalonia's `FluentTheme` gives `Button` a non-`Stretch` default `HorizontalAlignment`.** A
   freshly-constructed `Button` object reports `HorizontalAlignment.Stretch` at the CLR-default level
   (confirmed via reflection), but once realized inside the running app's themed visual tree, its
   *effective* `HorizontalAlignment` is not `Stretch` — every day-cell `Button` sized itself to its own
   content instead of filling its column. `Button.calendarDay` never overrode this (it only set
   `HorizontalContentAlignment`, a different property, governing content placement *inside* an
   already-sized button, not the button's own size).
2. **`UniformGrid`'s own column width is not "available width ÷ columns."** Even after fixing (1) in
   isolation, `UniformGrid`-hosted cells still didn't reliably stretch to a true 1/7 share of the row —
   consistent with Phase 13D's own prior finding that `UniformGrid`'s cell size is driven by content
   (there, height; here, width), not by the panel's own arranged size. A real `Grid` with explicit
   `ColumnDefinitions="*,*,*,*,*,*,*"` was verified, in the same real-window harness, to *always*
   distribute available width evenly regardless of any child's content — the structural guarantee
   `UniformGrid` cannot make.

A third, more subtle mechanical fact was also confirmed empirically before shipping the fix: `Grid.Row`/
`Grid.Column` set directly on an `ItemsControl` `DataTemplate`'s root element (e.g. the day-cell
`Button`) has **no effect** on layout — the actual panel child Avalonia inserts is a `ContentPresenter`
wrapper, and it is *that* object's `Grid.Column` which must be set. The working, verified mechanism is
an `ItemsControl.Styles` `Style` targeting `ContentPresenter` with a `Setter` bound to the item's own
data (see §2 below) — a well-established but easy-to-get-wrong Avalonia idiom, verified here against
the real Avalonia 12.1.1 runtime rather than assumed from general WPF/Avalonia knowledge.

### 2. New layout structure — one real `Grid` per week, never `UniformGrid`

- `Button.calendarDay` gained an explicit `HorizontalAlignment="Stretch"` setter — the direct, minimal
  fix for cause (1) above.
- The month grid no longer binds a flat `Days` list into a single `UniformGrid Columns="7"`. Instead:
  - `OperationsCalendarDayViewModel` gained `ColumnIndex` (`(int)Date.DayOfWeek`, Sunday=0..Saturday=6
    — matching the existing Sun..Sat weekday-header order).
  - A new, presentation-only `OperationsCalendarWeekViewModel` (`Days`: 7-element slice) groups the
    already-loaded `Days` list into Sunday-through-Saturday rows — `OperationsCalendarViewModel.Weeks`
    is computed via `Days.Chunk(7)` in the existing `OnSnapshotChanged` handler, never a second query or
    reordering.
  - The view's XAML now binds an outer `ItemsControl` over `Weeks` (`ItemsPanel`: a plain vertical
    `StackPanel`, one item per week), whose `DataTemplate` is itself an inner `ItemsControl` over that
    week's `Days`, whose `ItemsPanel` is a real `Grid ColumnDefinitions="*,*,*,*,*,*,*"`. Each inner
    `ItemsControl` carries one `ItemsControl.Styles` `Style` (`Selector="ContentPresenter"`,
    `x:DataType="vm:OperationsCalendarDayViewModel"`) setting `Grid.Column="{Binding ColumnIndex}"` —
    the verified mechanism from §1 above.
- Because each week is its own independent 7-column `Grid` (rather than one `Grid` spanning the whole
  month with computed row indices), no dynamic `RowDefinitions` construction was needed at all — row
  stacking is just the outer `StackPanel`'s normal vertical layout, and each week's row height is
  already bounded by every day cell inside it sharing the same fixed `Height="132"` style.

### 3. Row-height strategy — unchanged mechanism, still bounded

Phase 13D's fixed `Height="132"` + `ClipToBounds="True"` mechanism (spec Part 3's "content must never
make one date taller than neighbors") is preserved exactly — this phase does not reopen that decision.
Only the *padding* changed (`Padding="6,4"` → `"8,6"`) to give the now much-wider cells a little more
breathing room, and `TextBlock.dayHoursLine` (the strongest line after the date, spec Part 4) grew from
10.5px Medium to 11.5px SemiBold, since full-width columns now leave room for it without crowding.

### 4. Content hierarchy, Closed days, waterpark, notes, Draft/Finalized — unchanged by design

None of these needed to change: the existing content-budget priority (day number → operating
state/hours → Carolina Harbor line → stale label → passdown status → notes summary,
`OperationsCalendarDayViewModel`/the view's per-cell `StackPanel`) already matches spec Part 4's
required hierarchy, Closed days already render compactly (`"CLOSED"` + hours-line omission +
`"No Draft"`, no filler), waterpark/notes were already compact single lines/counts (never full note
text), and Draft/Finalized labels were already concise (`OperationsCalendarPassdownPresentation`). This
phase's redesign only had to stop suppressing that content's real column width — it did not need to
rewrite what the content says.

### 5. Off-month dates

Unchanged behavior, now with the geometry to back it up: `Button.calendarDay.outOfMonth` still applies
`Opacity="0.4"` over the identical `calendarDay` base style (same `Height`, same `Margin`, same
`HorizontalAlignment="Stretch"`) — off-month cells sit in exactly the same `Grid` columns as in-month
cells, so they now share the same real column width instead of collapsing to a "sliver" (§1's 40px
measurement, pre-fix, was overwhelmingly an off-month-cell number).

### 6. Selected date / Today

Unchanged: `Button.calendarDay.selected`/`.today`/`.today.selected` still apply only
`Background`/`BorderBrush`/`BorderThickness` — no size-affecting property in any of the three, so
selecting a date (or it being Today) cannot change that cell's dimensions relative to its neighbors, a
fact this phase now has direct pixel evidence for (§7 below) rather than only "by construction."

### 7. Tests added — real-window layout evidence (`OperationsCalendarViewTests.cs`)

All new tests construct a real, off-screen headless `Window` (not just `view.Measure()`/`Arrange()`)
and extract plain geometry data (`DayCellGeometry`/`CalendarLayoutSnapshot` records) — the first tests
in this codebase to do so for Calendar. Because this test assembly has no `Avalonia.Headless.XUnit`
package (xunit v2/v3 version conflict, per `HeadlessTestSetup`'s own doc comment), there is no running
Avalonia dispatcher loop for a `Window`-based test to marshal onto from a different thread —
`Dispatcher.UIThread.Invoke` from a different thread was confirmed empirically to deadlock here (no
loop pumps its queue). The fix: a new `tests/Carowinds.Passdown.Ui.Tests/AssemblyInfo.cs` with
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`, which keeps every test in this one
assembly on the single OS thread `HeadlessTestSetup`'s module initializer ran on. This is assembly-wide
(affects all ~595 Ui.Tests, not only Calendar's), but the measured cost was small (full `Ui.Tests` run:
~6s for 595 tests) against the benefit of real, provable pixel-geometry regression coverage that was
previously structurally impossible in this harness.

- `OperationsCalendarView_MonthGrid_AllDayCellsShareOneUniformWidthAndHeight` (Theory, 1280×860 and
  960×640): every realized day-cell button — in-month and off-month together — shares one width and
  one height (within 1px, to tolerate normal `Grid` star-column sub-pixel rounding at widths not evenly
  divisible by 7); asserts the shared width is meaningfully larger than the pre-15B narrow-strip regime
  (>60px); asserts cell count is a real 35- or 42-day grid with none dropped/duplicated. Covers spec
  Part 17 #1/#2/#8/#9/#10.
- `OperationsCalendarView_MonthGrid_WeekRowsAlignIntoSevenColumnRows`: groups realized cells by their
  absolute row Y and asserts exactly as many distinct rows as weeks in the grid, 7 cells per row.
  Covers Part 17 #3.
- `OperationsCalendarView_DenseOutlierDay_SelectedBusyWaterparkNotesCellsShareUniformGeometry` (Theory,
  both viewports): reuses Phase 13D's own `DenseSingleOutlierDaySnapshot` (custom hours + waterpark +
  stale + many Important notes on one date, Today+Selected both present, off-month cells alongside) and
  asserts the same one-shared-width/height property holds — proving the busy/selected/today/waterpark/
  notes-heavy cell never grew relative to its sparse siblings. Covers Part 17 #4/#5/#6/#7.
- `OperationsCalendarView_MonthGrid_NoDayCellClipsHorizontally` (Theory, both viewports): every cell's
  absolute right edge stays within the window's client width. Covers Part 17 #12.
- `OperationsCalendarView_SelectedDatePanel_FitsWithinWindow` (Theory, both viewports): the selected-date
  panel's absolute right edge stays within the window's client width alongside the redesigned grid.
  Covers Part 17 #13.

Combined with the existing `CalendarDayStyle_UsesFixedHeight_NotMinHeight` (Phase 13D, unchanged) and
the existing construction/dense-month/mixed-state tests (all still passing unmodified), Part 17's list
is covered except items #9–#11 as literal separate "dense/sparse × both sizes" test names — those are
covered by the existing dense/mixed-state tests (unchanged) combined with the new geometry tests already
running at both viewports, rather than duplicated as additional named tests.

### 8. Windows DPI scaling (100/125/150%) — NOT EXECUTED

No Windows host is available in this environment (dev host is Fedora/macOS per CLAUDE.md's own
platform section; target is Windows 11 x64). The redesign avoids the specific anti-pattern that would
make DPI scaling worse — no hardcoded pixel widths for the grid itself (`*` star columns, not fixed
`Width`s) — but this is a structural argument, not a substitute for real on-device validation. Reported
honestly as **NOT EXECUTED**, matching this doc's own established convention (e.g. Phase 13D Part 37's
identical disclosure for Windows-only concerns).

### 9. Validation

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors. `dotnet test CarowindsPassdown.slnx
--filter "Category!=LiveSmoke"` — **1654/1654 passing** (baseline 1645 + 9 new Calendar layout tests),
confirmed on a clean full run. Database schema 11, `AssociateDraftPayload` schema 3,
`FinalizedPassdownSnapshot` schema 1 — all unchanged (this phase touches only Desktop presentation
code, one new Desktop view model, and tests).

### 10. Deferred to 15C+

No further Calendar work was scoped for 15B beyond this redesign. Real Windows-host DPI validation
(§8) remains outstanding, as does rebuilding the Windows release ZIP (explicitly deferred by this
phase's own instructions, same as Phase 15A).

## Git checkpoint (Phase 15B)

Committed as `1359bd3` ("Phase 15B: redesign Operations Calendar month grid to a full-width
7-column layout").

## Phase 15E — Windows/UI production polish

Phase 15E audited every major screen for production-readiness at the app's two documented desktop
targets (1280×860, 960×640) and fixed the concrete defects that audit found. No Windows 11 host was
available in this environment (dev host is Fedora/macOS per CLAUDE.md), so DPI scaling itself is
**NOT EXECUTED** — the same honest disclosure convention Phase 15B (§8) and Phase 13D (Part 37) already
established. Everything else below is real-window/layout evidence, not speculation.

### 1. Method — real-window audit, not visual inspection

No screenshot/pixel renderer exists in this environment. Rather than re-guessing at layout risk from
reading XAML, this phase extended Phase 15B's own real-window technique (a genuine off-screen headless
`Window`, `Show()`+`Dispatcher.UIThread.RunJobs()`, then reading realized `Bounds`) to the app shell and
every other major page, and used it to find defects empirically before fixing anything. Three real,
confirmed-by-measurement defects were found this way — all three are the same underlying pattern (a
horizontal row of `Auto`-sized controls with no reflow fallback, sized to fit only at the 1280px design
width) and all three were invisible from reading the XAML alone:

1. **App-shell section strip clips at 960×640** (`MainWindow.axaml`): five section buttons in a plain
   `StackPanel` need ~1127px; at 960px, "History" was realized at `X=996` — starting *past* the window's
   own 960px client width. The control was fully unreachable, not merely visually tight.
2. **Associate action bar clips at 960×640** (`AssociateView.axaml`): six items (◀/date-picker/▶/Check
   Online/Export Diagnostics/Create PDF) in one `Grid ColumnDefinitions="Auto,Auto,Auto,Auto,Auto,*"`
   need more than 960px combined. Measured: `Export Diagnostics` right edge at 988px (8px into the
   scrollbar-free clipped zone), and `Create PDF` — the trailing star column — realized at **zero
   width**, positioned at `X=996`. Create PDF was completely unreachable at the documented minimum
   window size.
3. **Dashboard header clips at 960×640** (`DashboardView.axaml`): the header `Grid` used
   `ColumnDefinitions="Auto,*,Auto"` with the title in the first `Auto` column. `OperatingDayLabel` is
   produced by the same production formatter real data uses (`"dddd, MMMM d, yyyy"`, e.g. "Wednesday,
   September 30, 2026") — an ordinary date, not stress-test data. At 960px, the `Refresh` button was
   realized with its right edge at 997px, 37px past the window's own 960px client width.

Each defect was reproduced with a purpose-built diagnostic test (real `Window`, real production
formatters/fakes, printed `Bounds`) before any fix was written, and re-measured after the fix to confirm
the control now sits fully inside the window. The Associate/Dashboard/nav-strip fixes below are the
direct output of that empirical loop, not a stylistic guess.

### 2. Fixes made

- **`MainWindow.axaml`** — the section strip's `StackPanel` became a `WrapPanel`. At 960×640, "History"
  now wraps to a second line at `(266, 90)`, fully inside the window, instead of being pushed off it.
- **`AssociateView.axaml`** — the action-bar `Grid` became a `WrapPanel`; the `HorizontalAlignment="Right"`
  hacks on Export Diagnostics/Create PDF (which only worked when the star column had positive width) were
  removed since a `WrapPanel` doesn't need them. At 960×640, Export Diagnostics and Create PDF now wrap to
  a second line, both fully inside the window (verified: `Export Diagnostics` at `(40,90)`→width 288,
  `Create PDF` at `(336,90)`→width 176, both well short of the 960px edge).
- **`DashboardView.axaml`** — the header `Grid` changed from `Auto,*,Auto` to `*,Auto`: the title column
  is now the flexible one (with `TextWrapping="NoWrap"`/`TextTrimming="CharacterEllipsis"` added to the
  title `TextBlock` so a title that doesn't fit truncates with an ellipsis instead of forcing overflow),
  and the Refresh action group is the fixed `Auto` column that can never be squeezed. Re-measured: Refresh
  now sits at `(806,18)`→width 130, right edge 936px, inside the 960px window.
- **Button padding consistency** (§13 of the spec): `AssociateView.axaml` and `HistoryView.axaml`'s
  `primaryAction`/`secondaryAction` styles used `Padding="16,8"` while Dashboard/Calendar/Notes all used
  `Padding="14,7"` — a real, if minor, cross-page inconsistency in button height (each view keeps its own
  local copy of these styles by established convention, so the values had silently drifted). Standardized
  Associate and History to the majority convention (`14,7`); no other button property changed, nothing
  was globally enlarged.

None of these fixes touch business/retrieval/persistence logic, navigation semantics, or any bound
property — they are pure `Panel`/`Grid` structural and cosmetic changes.

### 3. Calendar re-validation (Phase 15B)

Re-run against the current app: all Phase 15B real-window geometry tests (seven equal columns, week-row
alignment, dense-outlier-day uniform geometry, no horizontal clipping, selected-date panel fit) still
pass unmodified. No defect was found in Calendar during this phase's audit; per this phase's own
instruction ("do not redesign Calendar again unless actual validation exposes a defect"), it was not
touched.

### 4. Other pages audited, no defect found

Associate Products/Notes sections, Operational Notes (editor + filter/list), and History (populated
detail with a long promotional product name, a comparison price, and a "Starting From" qualifier as
stress content) were each driven through the same real-window technique at both target viewports and
found not to clip. Accessibility spot-checks (`AutomationProperties.Name` on Manual Hours/Private Buyout
fields, the derived Early Ride Time value, Run Deep Refresh, View Date Pricing, the PDF multi-page
warning, and every Calendar day cell) were already present from prior phases (13D/13E/12E) — no gaps
found requiring a fix. Warning/status text (multi-page PDF warning, invalid Manual Hours, Check
Online/Deep Refresh busy and result messages) was already text-first, never color-only, per the existing
established convention — no change needed.

### 5. Real-window test coverage added (Part 17)

New file `Phase15ERealWindowLayoutTests.cs` (10 tests, extending Phase 15B's technique rather than
duplicating it):

- `MainWindow_SectionStrip_EveryButtonReachableWithinWindowBounds` (both viewports) — regression test for
  fix #1.
- `AssociateView_ActionBar_EveryActionReachableWithinWindowBounds` (both viewports) — regression test for
  fix #2, using the same Manual Hours/Private Buyout-enabled state as the existing
  `AssociateViewOperationalTimeLayoutTests`.
- `DashboardView_Header_RefreshActionStaysWithinWindowBoundsWithARealisticLongDateLabel` (both
  viewports) — regression test for fix #3, deliberately using the default fixture's production-formatted
  date label rather than a synthetic long string.
- `OperationalNotesView_EditorOpen_NoControlClipsHorizontallyAt960x640`,
  `HistoryView_WithSelectedDetail_NoControlClipsHorizontallyAt960x640` (using a long promotional product
  name, a comparison price, and a "Starting From" qualifier as representative stress content per Part 18),
  `AssociateView_ProductsSection_NoControlClipsHorizontally` (both viewports) — a general, reusable
  `AssertNoControlClipsHorizontally` sweep (every realized `Button`/`TextBox`'s right edge must stay
  within the window's client width, 1px tolerance) rather than duplicating per-control assertions.

### 6. Real-window test harness — flake investigated, not silently hidden

While adding the above, a full-solution run (`dotnet test CarowindsPassdown.slnx`) intermittently failed
a cluster of real-`Window` tests with `Dispatcher.VerifyAccess()` → "the calling thread cannot access
this object because a different thread owns it." Before assuming this was caused by this phase's new
tests, it was isolated with a `git stash` A/B comparison against the pre-15E baseline: **the identical
race reproduces on the untouched Phase 15B code**, confirming this is a pre-existing, known limitation
(consistent with the one flake Phase 13G already logged for `DashboardViewTests`), not a regression this
phase introduced. Root cause: `AssemblyInfo.cs`'s own doc comment assumed disabling xUnit's cross-collection
parallelization keeps every test on "the one thread the module initializer ran on" — that assumption does
not hold under this assembly's actual xUnit v3 runner; the module initializer runs on whichever thread
first touches the assembly, and sequential test *execution* is not guaranteed to reuse that same OS
thread.

A proper fix was attempted (a dedicated, explicitly-owned background thread that all real-`Window` work
would marshal onto, replacing the incidental-thread-reuse assumption) and **reverted** — it deadlocked
outright: blocking the `[ModuleInitializer]` method on that thread's own setup conflicts with the CLR's
loader lock, which is held across module initialization while the new thread tries to JIT/load Avalonia's
types. A hang is strictly worse than an occasional fast test failure, so `HeadlessTestSetup.cs` and
`OperationsCalendarViewTests.cs` are back to their original, direct, synchronous form (see both files'
own updated doc comments for the full account). The flake remains a known, pre-existing, occasionally
reproducing race — re-running clears it every time observed in this session; a genuine fix would need
either the `Avalonia.Headless.XUnit` package (blocked by this project's existing xUnit v2/v3 conflict,
per `HeadlessTestSetup`'s original doc comment) or a deeper test-harness redesign, both out of scope for
a UI-polish phase.

### 7. Validation

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors. Test counts, confirmed on clean runs (the
known §6 flake was retried until clean, per this phase's own instruction to distinguish a known race from
a deterministic regression):

- **1664/1664** passing (baseline 1654 + 10 new Phase 15E tests): Domain 105, Application 471, Documents
  114, Infrastructure 369, Ui 605.
- Re-run 3× as `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"`; one run was clean on
  the first attempt, matching §6's finding that the flake is intermittent, not deterministic.

Database schema 11, `AssociateDraftPayload` schema 3, `FinalizedPassdownSnapshot` schema 1 — all
unchanged; this phase touches only Desktop presentation XAML and test-only files.

### 8. Windows validation — NOT EXECUTED, manual checklist for 15F

No Windows 11 host was available in this environment. `docs/phase-7e-windows-validation-checklist.md`
already has a filled-in, PASSED display-scaling section (Part E), but that run was against an old
`0.1.0-dev` release artifact predating Phases 8 through 15 — it is not current evidence for today's app
and is not treated as satisfying this phase's own DPI requirement. A concise, current, 15E-scoped
addendum for 15F:

- [ ] Launch the current (not 0.1.0-dev) extracted app on Windows 11
- [ ] Titlebar/taskbar icon is the approved `logo.ico`, not a stock Avalonia icon
- [ ] 100% scaling: Dashboard, Calendar, Associate (Overview/Products/Notes), Operational Notes, History
      — no clipped text, no unreachable buttons
- [ ] 125% scaling: same pages, plus typed Manual Hours/Private Buyout fields remain usable and their
      labels don't clip
- [ ] 150% scaling: same pages again
- [ ] Resize the window down to 960×640 (the documented minimum) at each of the above scales — confirm
      the app-shell section strip, the Associate action bar, and the Dashboard header each wrap to a
      second line cleanly (this phase's three fixes) rather than clipping
- [ ] Trigger a PDF >1-page warning and confirm it's readable at each scale
- [ ] Run Deep Refresh and confirm the busy/status text stays legible and doesn't get clipped
- [ ] Keyboard-only pass: Tab through Associate Overview and Products, confirm visible focus rings and
      that tab order matches visual order (including through the new WrapPanel action rows)

Do not fabricate a PASS for any of the above — mark NOT EXECUTED if it wasn't actually run on Windows.

### 9. Deferred to 15F / out of scope

- Real Windows-host DPI validation (§8 above).
- Rebuilding the Windows release ZIP (explicitly out of scope for 15E, same as 15A/15B).
- A structural fix for the §6 headless-test-harness thread-affinity flake (would need either the
  `Avalonia.Headless.XUnit` package or a deeper harness redesign — test-infrastructure work, not UI
  polish).
- Phase 15C, 15D, and any further Manager-workflow scope (unchanged CLAUDE.md "current scope decision").

## Git checkpoint (Phase 15E)

Committed as `5c8044b` ("Phase 15E: Windows/UI production polish").
