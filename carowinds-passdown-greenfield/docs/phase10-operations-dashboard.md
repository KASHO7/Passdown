# Phase 10 — Operations Dashboard

Phase 10A (this document's current scope) builds the Operations Dashboard's
**foundation**: its information architecture, its place in top-level
navigation, and a purpose-built Application-layer read model/service — not
the full dashboard experience. Later Phase 10 subphases (10B–10F, outlined
below) fill the foundation in with real content cards, richer status
surfaces, and visual polish. No phase through 10A implements alerts, an
operations calendar, a product/pricing reference tool, or the Manager
workflow (CLAUDE.md "Current scope decision" — Manager stays deferred).

## Purpose

> "What do I need to know today?"

The Dashboard is a **summary/control surface**, not another editing form.
It answers that question at a glance and routes a supervisor to the
existing detailed screens (Associate Passdown, History) to act on what it
shows — it never duplicates their editing UI.

## Top-level navigation (Part A)

Top-level app-shell sections, in `MainWindow.axaml`'s section strip:

```
Dashboard | Associate Passdown | History
```

`Dashboard` is new this phase; `Associate Passdown`/`History` are
unchanged from Phase 4B/9D. No `Manager`, `Products reference`, `Notes
Manager`, `Calendar`, or `Alerts` entry exists — those remain later
roadmap items, not placeholders.

**Default startup section: Dashboard.** Nothing in `AGENT_BUILD_SPEC.md`
requires Associate Passdown to remain the startup screen, and a
supervisor's daily habit is "check today's status first" — so
`MainViewModel.SelectedSection` now defaults to `AppSection.Dashboard`
(previously `AppSection.AssociatePassdown`). This is a deliberate,
recorded choice (spec Part Q), not an oversight: `AssociateViewModel`'s own
constructor still runs and starts its own local load exactly as before
(Associate state is not lazy or conditional on being visible — it is
constructed once, alongside Dashboard and History, at composition-root
time and stays alive for the app's lifetime, spec Part T), so switching to
the Associate tab is instant either way. `StartupCatchUpRefresh` remains a
detached background task fired from `App.OnFrameworkInitializationCompleted`
after the window is assigned — unaffected by which section renders first,
since it never runs against a specific view model at all.

## Dashboard date semantics (Part D)

**Dashboard represents "Today" in America/New_York.** This is
deliberately independent of `AssociateViewModel.SelectedDate` — an
associate may be preparing a future date's passdown while the Dashboard
still shows today's actual operating snapshot.

```
OperationsDashboardService.GetTodayAsync()
    -> DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime)
    -> LoadAsync(today)
```

Uses the existing `EasternTimeZone`/`IClock` abstractions (Application
layer) — never `DateTime.Now`, `DateTime.UtcNow`, or a fixed UTC offset.
`OperationsDashboardServiceTests.GetTodayAsync_ResolvesToCurrentEasternDate`
exercises a UTC instant close to a day boundary to prove the Eastern
conversion is actually applied, not just a same-day coincidence.
`LoadAsync(DateOnly)` also exists for a caller/test that needs a date other
than today — it never reads `AssociateViewModel.SelectedDate`.

## Architecture — the Dashboard is a projection, not a second implementation

```
existing Application services/state
        |
        v
OperationsDashboardService (Application/Dashboard/OperationsDashboardService.cs)
        |
        v
OperationsDashboardSnapshot (Application/Dashboard/OperationsDashboardSnapshot.cs)
        |
        v
OperationsDashboardViewModel (Desktop/ViewModels/OperationsDashboardViewModel.cs)
        |
        v
OperationsDashboardPresentation (Desktop/Presentation/OperationsDashboardPresentation.cs)
        |
        v
DashboardView.axaml
```

`OperationsDashboardService` depends on exactly three things:
`IAssociatePassdownService` (already local-only by its own contract —
`LoadAssociateDateAsync` never calls `IRefreshOrchestrator`/HTTP/
Playwright), `IFinalizedPassdownRepository` (metadata only — see below),
and `IClock`. It duplicates none of:

- park-hours calculation (`ParkHoursCalculator.Calculate`, reused
  transitively through `AssociateWorkspace.ParkHours`);
- date/"today" retrieval (`EasternTimeZone`/`IClock`, same instances the
  rest of the app already uses);
- source freshness/staleness logic (`AssociateVerificationState`, read
  straight off `AssociateWorkspace.CategoryStatuses`/
  `AssociateParkHoursView.Verification`);
- Associate draft composition (`AssociatePassdownService.LoadAssociateDateAsync`,
  called directly — never a second draft/dated-result/catalog merge);
- finalized-passdown detection (`IFinalizedPassdownRepository.ExistsForDateAsync`/
  `ListAsync`, the same repository Phase 9's finalizer and History screen use);
- product retrieval (not touched at all — Phase 10A does not surface
  per-product detail on the Dashboard);
- History persistence (no `IPassdownHistoryService` dependency exists);
- scheduled-refresh status rules (no `IScheduledRefreshLogProvider`/
  `IScheduledTaskStatusProvider` dependency exists).

`OperationsDashboardServiceTests.Constructor_HasNoRefreshOrchestratorOrRetrievalDependency`
and `OperationsDashboardViewModelTests.Constructor_HasNoRefreshOrPdfDependency`
assert this by reflecting over the actual constructor signatures, so "no
duplicated business logic / no network dependency" stays true by
construction, not just by code review.

On the Desktop side, `OperationsDashboardPresentation.Build` reuses
`ParkHoursPresentation.Build` directly on the snapshot's own
`AssociateParkHoursView` — the exact same formatter `AssociateView`
already uses for its Park Hours panel — rather than a second hours-text
formatter.

## `OperationsDashboardSnapshot` (Part E)

```csharp
public sealed record OperationsDashboardSnapshot(
    DateOnly OperatingDate,
    DashboardParkStatusSummary ParkStatus,
    DashboardPassdownStatusSummary PassdownStatus,
    int? ProjectedAttendance,
    int? PreSolds,
    bool EarlyRideTimeEnabled,
    string? EarlyRideTimeDetails,
    DailyFeatureType? DailyFeatureType,
    string? DailyFeatureText,
    DashboardNotesSummary Notes,
    DashboardSourceHealthSummary SourceHealth,
    DateTimeOffset GeneratedAt);
```

Fields deliberately **not** included in 10A: a product list/detail (the
Dashboard summarizes, it does not re-render Products), a History listing
(a "latest finalized" pointer belongs to a later subphase once the History
integration is actually designed, not fabricated now), and any
alert/confidence scoring (Phase 10D/13's job).

### Park status (Part F)

```csharp
public enum DashboardParkStatus
{
    NotChecked, Closed, Open, CustomHours, PrivateBuyout, NotPublished, Unavailable,
}

public sealed record DashboardParkStatusSummary(DashboardParkStatus Status, AssociateParkHoursView Hours);
```

`Status` is a pure classification over `AssociateParkHoursView.Effective.Status`
(`EffectiveHoursStatus.Open`/`Closed`/`NotCheckedYet`, already computed by
`ParkHoursCalculator`) plus `IsManualOverrideApplied`/`IsPrivateBuyoutApplied`
and, when nothing has ever been checked, `Verification.State`
(`NotPublished`/`Unavailable`/`Error`) — no new hours arithmetic. `Hours`
carries the raw view through so the Desktop layer can reuse
`ParkHoursPresentation` for headline/verification text instead of a second
formatter.

### Passdown status (Part G)

```csharp
public enum DashboardPassdownStatus { NoDraft, DraftInProgress, Finalized }

public sealed record DashboardPassdownStatusSummary(
    DashboardPassdownStatus Status, DateTimeOffset? LastSavedAt, DateTimeOffset? FinalizedAt);
```

Derived from `AssociateWorkspace.SaveStatus` (`NeverSaved` -> `NoDraft`;
`Saved` -> `DraftInProgress`) and `IFinalizedPassdownRepository.ExistsForDateAsync`.
A `PassdownDate` can only become eligible for finalization at 6:00 AM
Eastern the day *after* it (`FinalizationEligibility`,
`docs/phase9-finalization.md`), so `GetTodayAsync()`'s snapshot will never
actually observe `Finalized` for "today" — `LoadAsync(DateOnly)` still
implements the real check (rather than special-casing "today" to skip it)
so the same read model stays correct for a past date passed to it later.

### Notes summary (Part H)

```csharp
public sealed record DashboardNotesSummary(bool HasNotes, IReadOnlyList<string> PreviewLines, int TotalLineCount);
```

A read-only prefix (first 3 lines) of the current date's own
`AssociateDraftPayload.OperationalNotes` — no second notes persistence
model, no future Operational Notes Manager built early.

### Source health (Part I)

```csharp
public enum DashboardSourceHealth { AllCurrent, SomeStale, SomeUnavailable }

public sealed record DashboardSourceHealthSummary(
    DashboardSourceHealth Health, int CurrentCount, int StaleCount, int UnavailableCount, int NotCheckedCount);
```

A compact aggregate over `AssociateWorkspace.CategoryStatuses` plus
`AssociateParkHoursView.Verification` — the exact same
`AssociateVerificationState` values the Associate screen's own status
banners already use. `SomeUnavailable` wins over `SomeStale`, which wins
over `AllCurrent`; "not yet checked" is not itself a problem state. This
is a foundation for the richer status surfaces Phase 10D designs — not
Phase 13's Alerts & Data Confidence system.

## Service (Part J) and local-only loading (Part K)

```csharp
public interface IOperationsDashboardService
{
    Task<OperationsDashboardSnapshot> GetTodayAsync(CancellationToken cancellationToken = default);
    Task<OperationsDashboardSnapshot> LoadAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);
}
```

```
navigate to Dashboard
    -> OperationsDashboardViewModel constructor fires a fire-and-forget local load
    -> IOperationsDashboardService.GetTodayAsync()
    -> IAssociatePassdownService.LoadAssociateDateAsync (local-only) + IFinalizedPassdownRepository (metadata-only)
    -> render
```

No HTTP call, no Playwright session, no Task Scheduler API call is
possible from this path — `OperationsDashboardService`'s constructor takes
no type capable of any of those (see the reflection tests above).
`OperationsDashboardServiceTests.LoadAsync_FinalizedDate_ReportsFinalizedWithTimestamp_AndNeverLoadsFullRecord`
additionally proves `IFinalizedPassdownRepository.GetByDateAsync` — the
only method that can return a historical PDF blob — is never called by the
Dashboard, even for an already-finalized date (spec Part S); finalization
timestamps come from the metadata-only `ListAsync`.

## `OperationsDashboardViewModel` (Part L)

Depends on `IOperationsDashboardService` and an optional `ILogger` only —
no repository, PDF, or refresh dependency. Owns `Snapshot`/`Presentation`,
`IsLoading`/`ErrorMessage`, and two navigation actions:

- `OpenPassdownCommand` raises `OpenAssociateRequested`
- `OpenHistoryCommand` raises `OpenHistoryRequested`

`MainViewModel` — the single owner of top-level navigation (spec Part P) —
subscribes to both events in its constructor and turns them into a
`SelectedSection` switch. The Dashboard view model itself never references
`AppSection` or `MainViewModel`, so it stays independent of app-shell
navigation concerns (mirrors how `PassdownHistoryViewModel` has no
knowledge of `AssociateViewModel`).

`ReloadCommand` is a manual re-fetch (spec Part R) — the Dashboard does
not poll or subscribe to an automatic refresh/completion event from
`StartupCatchUpRefresh`; a supervisor revisits/reopens it (or presses
Refresh) to see any persisted state that changed in the background. No
polling loop was added.

## Layout skeleton (Part M/N)

`DashboardView.axaml`, at the 1280×860 desktop-density target:

```
Header — "Today" / operating-day label / last-updated / Refresh
──────────────────────────────────────────────────────────────
[ Today's Operation ]        [ Passdown Status + Open Passdown ]

[ Daily Snapshot: Attendance | Pre-solds | Early Ride Time | Daily Feature ]

[ Data Verification + Open History ]   [ Operational Notes preview ]
```

Grid-based two-column panels (not one long `StackPanel`), reusing the same
`panel`/`heroPanel`/`chip`/`sectionTitle` style classes `AssociateView`/
`HistoryView` already define, scoped to this `UserControl` the same way.
No fake placeholder cards — every panel renders real values from the
snapshot (`AssociateViewModel`'s "no draft yet" fields render as "Not
recorded"/"Not applicable today"/"No operational notes entered", never a
blank card). Wrapped in a `ScrollViewer` so a compressed 960×640 window
degrades to vertical scrolling rather than clipping (spec Part N).

## Navigation coordination and state isolation (Part P)

`MainViewModel` remains the sole owner of `Dashboard`/`Associate`/`History`
and `SelectedSection`. All three child view models are constructed once at
composition-root time and stay alive for the app's lifetime — switching
`SelectedSection` only toggles `IsVisible` on the wrapping `Border` in
`MainWindow.axaml`; it never recreates Associate's in-progress edit/autosave
state and never touches `AssociateViewModel.SelectedDate`
(`MainViewModelNavigationTests.NavigatingBetweenSections_PreservesAssociateState`).

## Accessibility (Part V)

The three top-level section buttons carry `AutomationProperties.Name`
("Dashboard section" / "Associate Passdown section" / "History section");
Dashboard's own actions carry names and help text ("Open Passdown", "Open
History", "Refresh today's operations summary"). Status is always
text-first (badges/labels), never color-only, matching the existing
`ui-polish` skill guidance.

## Visual validation (Part Y) — PARTIAL

No pixel/bitmap screenshot renderer exists in this codebase (unchanged
since Phase 9E's own note on this) — `Avalonia.Headless` is used here only
for real-XAML-loader construction checks (`DashboardViewTests`), the same
approach every prior phase's UI tests use. `DashboardView` was reviewed
directly against the 1280×860 layout target (Grid-based two-column panels,
no oversized single-column stack, `ScrollViewer` fallback for 960×640) but
not pixel-verified. Recorded honestly as **PARTIAL**, matching the
precedent set for `HistoryView` in `docs/phase9-finalization.md`.

## Tests added (Part X)

- `Carowinds.Passdown.Application.Tests/Dashboard/OperationsDashboardServiceTests.cs`
  (13 tests) — Eastern date resolution, no-refresh-dependency, park-status
  classification (Closed/CustomHours/PrivateBuyout), passdown-status
  (NoDraft/DraftInProgress/Finalized without loading the PDF blob), notes
  summary, source health.
- `Carowinds.Passdown.Ui.Tests/OperationsDashboardViewModelTests.cs`
  (6 tests) — load/error state, reload, both navigation events, no
  refresh/PDF/HTTP-named constructor dependency.
- `Carowinds.Passdown.Ui.Tests/DashboardViewTests.cs` (4 tests) —
  real-XAML-loader construction for a populated snapshot, a load failure,
  and dense content; automation names.
- `Carowinds.Passdown.Ui.Tests/MainViewModelNavigationTests.cs` — extended
  (default-to-Dashboard, all three section-switch commands, Dashboard's
  two navigation events, Associate-state preservation across all three
  sections).
- `Carowinds.Passdown.Ui.Tests/CompositionRootTests.cs` — extended
  (`IOperationsDashboardService`/`OperationsDashboardViewModel` resolve
  from the composition root).

## Full validation

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` —
**886/886** (858 Phase 9F baseline + 28 new: 13 Application + 15 Ui). No
live internet was used or required.

## What Phase 10A does *not* implement

- The full set of Dashboard content cards/widgets beyond the foundation
  panels above.
- Alerts or any data-confidence scoring system (Phase 13).
- A full operations calendar.
- A product/pricing reference tool.
- The Manager workflow (still deferred per CLAUDE.md).
- Automatic/event-driven Dashboard refresh when background state changes
  (Phase 10B/10D candidate — manual `ReloadCommand` only, Part R).
- A "latest finalized passdown" pointer or any deeper History integration
  beyond what's needed to classify today's own passdown status (Phase 10C).
- Redesigning existing Associate or History screens (neither was changed
  beyond `MainWindow.axaml`'s section strip gaining a third entry).

## Future content map

```
10A   foundation: navigation, read model/service, layout skeleton (this document)
10B   Today operational summary — richer content within the existing panels (this document)
10C   passdown/history/notes integration — deeper cross-linking, "latest finalized" surfaces
10D   data confidence/status surfaces — building on DashboardSourceHealthSummary
10E   visual/desktop polish
10F   final Phase 10 audit and release rebuild
```

---

# Phase 10B — Today's Operational Summary

Phase 10A already built nearly the entire content set Phase 10B's spec
targets — `OperationsDashboardSnapshot` already carried attendance,
pre-solds, Early Ride Time, Daily Feature, and a notes preview, and
`DashboardView.axaml` already rendered all of it in the Part Q hierarchy
(hero row, daily-snapshot grid, notes/verification row). Phase 10B's real
scope was closing the specific gaps between that foundation and the
spec's literal requirements: an `Open Notes` action, wording consistency
with the rest of the app, and bounded presentation-only truncation for
long free-text content — not a rebuild.

## Header (Part B)

The header now reads as an eyebrow + headline pair, matching the spec's
`TODAY` / date example exactly: `TextBlock Text="TODAY"` using the same
`sectionTitle` style class as every other panel heading, above
`Presentation.OperatingDayLabel` promoted to the `pageTitle` style. No
`DateTime.Now` call was added anywhere in Desktop — the label still comes
from `OperationsDashboardPresentation.Build`'s existing formatting of
`OperationsDashboardSnapshot.OperatingDate`.

## Park status / effective hours / custom hours / private buyout / closed / not-published / unavailable (Part C, O)

Unchanged from Phase 10A — `OperationsDashboardService.BuildParkStatus`
remains the single classification point, and `OperationsDashboardPresentation.Build`
still reuses `ParkHoursPresentation.Build` on the snapshot's own
`AssociateParkHoursView` for headline/verification text. Phase 10B adds
the missing regression coverage the Part O spec calls for at the
Application layer (`LoadAsync_ParkHoursNotPublished_ReportsNotPublished`,
`LoadAsync_ParkHoursUnavailable_ReportsUnavailable`, alongside the
existing Closed/CustomHours/PrivateBuyout tests) and at the View layer
(`DashboardView_EachParkStatus_ConstructsWithoutError`, a `[Theory]` over
Closed/NotPublished/Unavailable/CustomHours/PrivateBuyout asserting the
badge text is never `"Open"`).

## Passdown status (Part D)

Unchanged presentation logic (`No Draft` / `Draft In Progress` /
`Finalized`, with `Open Passdown` routing to Associate) — already matched
the spec's target states from Phase 10A.

## Today at a Glance (Part E–I)

Renamed the panel from "Daily Snapshot" to "Today at a Glance" to match
the spec's literal section label (and the same label `AssociateView`
already uses for its own analogous Attendance/ERT/Daily Feature block —
Phase 10B keeps the two screens' vocabulary aligned). Content itself
(Attendance, Pre-solds, Early Ride Time, Daily Feature) is unchanged from
Phase 10A; what changed is empty-state wording and truncation:

- **Attendance / Pre-solds** (Part F/G): empty now reads `"Not entered"`
  instead of `"Not recorded"` — matching `AssociateView`'s own
  `PlaceholderText="Not entered"` on these exact two fields, rather than
  inventing a second empty-state vocabulary. `0` is never conflated with
  missing — `Build_ZeroAttendance_IsDistinctFromNotEntered` proves a
  genuinely-saved `0` still renders as `"0"`.
- **Early Ride Time** (Part H): disabled now reads `"Not set"` (previously
  `"Not applicable today"`), matching `DailyFeatureTypeOption`'s own
  `"Not set"` wording for its analogous "nothing chosen" state.
- **Daily Feature** (Part I): empty now reads `"Not entered"` (previously
  a separate `"Not set for today."` string driven by an XAML
  `IsNullOrEmpty` toggle); the empty/present decision and the text itself
  now live in `OperationsDashboardPresentation.Build` so both are
  unit-testable without constructing the view.

## Operational notes preview (Part J)

Unchanged data source (`AssociateDraftPayload.OperationalNotes`, prefix of
3 lines, no new persistence). Added: an `Open Notes` button. Associate
Passdown has no separate Notes tab/section — Operational Notes is one
field on its single Overview screen — so a true deep-link would require
an ugly coupling to Associate's internal layout (spec Part J/T's own
escape hatch). `OpenNotesCommand` therefore raises the same
`OpenAssociateRequested` event `OpenPassdownCommand` does, routing to
Associate Passdown as a whole; it is a distinct command/button with its
own `"Open Notes"` automation name and help text so the UI is explicit
about what it does, but the deeper Notes-specific deep-link is
deliberately deferred to Phase 10C (not implemented here).

## Truncation (Part K)

Presentation-only, applied in `OperationsDashboardPresentation.Build`
(never touching persisted draft data):

- Daily Feature text: bounded to 140 characters plus a trailing `"…"`
  when longer.
- Each operational-notes preview line: bounded to 100 characters plus a
  trailing `"…"` when longer (on top of the existing 3-line prefix from
  `OperationsDashboardService.BuildNotesSummary`).

`OperationsDashboardPresentationTests` (new file) unit-tests both bounds
directly, and `DashboardView_LongDailyFeatureAndNotes_ConstructsWithBoundedText`
proves the view still constructs cleanly and the bound presentation
strings are shorter than the raw (500/400-character) inputs.

## Last updated / freshness (Part L)

Unchanged from Phase 10A: `GeneratedAtText` (`"Updated {timestamp}"`) is
the one clear, non-mixed timestamp already available —
`OperationsDashboardSnapshot.GeneratedAt`, the moment this snapshot was
computed. It does not conflate draft-save time, source-refresh time, or
finalization time. The richer confidence/staleness surface Part L defers
to Phase 10D was not added here.

## Navigation / Associate-date isolation / local-only guarantee (Part T–V)

No changes beyond `OpenNotesCommand` reusing the existing
`OpenAssociateRequested` event — `MainViewModel`'s wiring, Associate
state preservation, and the local-only constructor shape (no
HTTP/repository/PDF dependency on `OperationsDashboardService` or
`OperationsDashboardViewModel`) are all unchanged and still covered by
the Phase 10A tests (`MainViewModelNavigationTests`,
`Constructor_HasNoRefreshOrchestratorOrRetrievalDependency`,
`Constructor_HasNoRefreshOrPdfDependency`).

## Accessibility (Part X)

`Open Notes` carries `AutomationProperties.Name="Open Notes"` and help
text; `DashboardView_WithSnapshot_ExposesCriticalAutomationNames` now
asserts all three action names (`Open Passdown`, `Open History`,
`Open Notes`) plus `Refresh today's operations summary`.

## Visual validation (Part Z) — PARTIAL

Same constraint as Phase 10A: no pixel/bitmap screenshot renderer exists
in this codebase. `DashboardView` was reviewed directly against the
1280×860 layout target (unchanged Grid structure from 10A) and against
the new long-content case (`DashboardView_LongDailyFeatureAndNotes_ConstructsWithBoundedText`,
`DashboardView_EachParkStatus_ConstructsWithoutError`) via Avalonia's
headless real-XAML-loader construction checks, not pixel-verified.
Recorded honestly as **PARTIAL**, matching Phase 10A/9E precedent.

## Tests added (Part Y)

- `OperationsDashboardServiceTests`: +2 —
  `LoadAsync_ParkHoursNotPublished_ReportsNotPublished`,
  `LoadAsync_ParkHoursUnavailable_ReportsUnavailable`.
- `OperationsDashboardViewModelTests`: +1 —
  `OpenNotesCommand_RaisesOpenAssociateRequested`.
- `DashboardViewTests`: +2 test methods (+6 test cases with the new
  `[Theory]`) — `DashboardView_LongDailyFeatureAndNotes_ConstructsWithBoundedText`,
  `DashboardView_EachParkStatus_ConstructsWithoutError` (5 inline cases);
  extended `DashboardView_WithSnapshot_ExposesCriticalAutomationNames` to
  assert `"Open Notes"`.
- `OperationsDashboardPresentationTests` (new file): 19 test cases —
  park-status badge text for all 7 classifications, attendance/pre-solds
  present/missing/zero, Early Ride Time enabled/disabled, Daily Feature
  present/missing/long/short, notes empty/long-line/short-lines.

28 new tests total (886 Phase 10A baseline + 28 = 914).

## Full validation

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` —
**914/914**. No live internet was used or required.

## What Phase 10B does *not* implement

- Alerts or any data-confidence/staleness scoring beyond the existing
  `DashboardSourceHealthSummary` aggregate (Phase 10D).
- A true Notes deep-link (opening Associate directly scrolled/focused to
  Operational Notes) — `Open Notes` routes to Associate Passdown as a
  whole; a dedicated Notes sub-view/anchor does not exist yet (Phase 10C).
- A "latest finalized passdown" pointer or richer History integration
  (Phase 10C).
- An operations calendar, a product/pricing reference tool, or the
  Manager workflow (still deferred per CLAUDE.md).
- Any network call from Dashboard load — still local-only by
  construction, unchanged from Phase 10A.
- Visual/desktop polish beyond what these content changes required
  (Phase 10E).
- The Phase 10F release rebuild/audit.

---

# Phase 10C — Passdown/History/Notes integration

Phase 10C turns the Dashboard from a read-only summary into a workflow
hub: it adds a compact History integration surface (the latest finalized
passdown's metadata) and replaces Phase 10B's "route to Associate as a
whole" placeholder with true, targeted deep links into Associate
Passdown's Overview and Notes sections. No new operational business rule
was added — every value shown was already computed by an existing
Application-layer type; Phase 10C's work is entirely wiring and
navigation.

## Latest-finalized History integration (Parts A/B)

`OperationsDashboardSnapshot` gained one field:

```csharp
FinalizedPassdownSummary? LatestFinalized
```

Reusing the existing metadata-only `FinalizedPassdownSummary` type
(`Application/Persistence/FinalizedPassdownPersistence.cs`) — the exact
same read model `IFinalizedPassdownRepository.ListAsync`/the History
screen's own list already use — rather than inventing four separate
scalar fields (`LatestFinalizedPassdownDate`/`LatestFinalizedAt`/etc.) or
a second read model. `null` means no passdown has ever been finalized
(Part M's empty state).

`OperationsDashboardService.BuildPassdownAndHistoryAsync` (replacing
Phase 10A/B's `BuildPassdownStatusAsync`) makes exactly **one**
`IFinalizedPassdownRepository.ListAsync` call per `LoadAsync`, metadata
only, and uses it for two purposes at once: classifying today's own
passdown status (unchanged behavior from Phase 10A/B) and selecting the
latest finalized record for the History integration surface. This
replaces the prior `ExistsForDateAsync` fast-path with the same
`ListAsync` call the rare "today is finalized" branch already had to
make — no new repository dependency, no additional round trip in the
common case.

## Latest-selection rule (Part C)

"Latest finalized" is defined as the highest `PassdownDate`, computed
explicitly in `OperationsDashboardService`:

```csharp
summaries.Aggregate((latest, candidate) => candidate.PassdownDate > latest.PassdownDate ? candidate : latest)
```

— never the most-recently-inserted row and never the row with the most
recent `FinalizedAtUtc`. This is computed independently of
`SqliteFinalizedPassdownRepository.ListAsync`'s own `ORDER BY
passdown_date DESC` (which already happens to produce the same answer at
index 0) so the Dashboard's own correctness never silently depends on a
persistence-layer ordering detail it doesn't own.
`OperationsDashboardServiceTests.LoadAsync_MultipleFinalizedRecords_LatestSelectedByHighestPassdownDate`
seeds three dates where the highest `PassdownDate` is neither the
first-nor-last-inserted row nor the most recently `FinalizedAtUtc`, and
asserts the correct one wins. The current schema's unique index on
`passdown_date` (Phase 9A) means "multiple rows for one date" can never
occur, so there is no tie-breaking rule to define.

## History metadata-only guarantee (Parts L/N/O)

Unchanged Phase 9C/9D guarantee, now also proven from the Dashboard's own
call site: `IFinalizedPassdownRepository.GetByDateAsync` (the only method
that can return `PdfBytes`/`SnapshotJson`) is never called by
`OperationsDashboardService` — proven the same way Phase 10A did for
today's own finalized-date case
(`OperationsDashboardServiceTests.LoadAsync_LargeFinalizedHistorySet_RemainsMetadataOnly`
seeds 50 finalized rows and asserts `GetByDateCallCount == 0`). A legacy
pre-9C row with `HasFrozenPdf == false` (Part N) and a row with a
malformed `SnapshotJson` string or an unrecognized future
`SnapshotSchemaVersion` (Part O) all still surface correctly as
`LatestFinalized`, because the Dashboard never reads or deserializes
`SnapshotJson` in the first place — there is nothing to fail on. Three
dedicated regression tests
(`LoadAsync_LatestFinalizedHasNoPdf_StillPopulated`,
`LoadAsync_LatestFinalizedHasMalformedSnapshotJson_DoesNotThrow`,
`LoadAsync_LatestFinalizedHasFutureSchemaVersion_DoesNotThrow`) lock this
in, alongside the equivalent `DashboardView` construction-level cases.

## No-History empty state (Part M)

`OperationsDashboardPresentation.LatestFinalized` is `null` when
`OperationsDashboardSnapshot.LatestFinalized` is `null`; `DashboardView`'s
"Latest Finalized" panel then shows the plain-language line "No finalized
passdowns yet." — the same Part M wording the spec calls for — never a
fabricated date. `Open History` stays visible and reachable in this
state.

## Dashboard layout (Part P)

`DashboardView.axaml`'s bottom row gained one full-width "Latest
Finalized" panel below the existing Data Verification / Operational
Notes row, showing the reused
`PassdownHistorySummaryPresentation.Build` output (date / finalized-at /
origin text) plus the page's single `Open History` action. The duplicate
`Open History` button that Phase 10A/10B had placed on the Data
Verification card was removed — one action, one place, per Part Q. No
giant History card/list was added; the page stays within its existing
Grid-based panel structure.

## Open Passdown / Open Notes deep link (Parts F/G/H)

`AssociateViewModel` gained a view-model-owned internal section rail
(replacing Phase 4B's view-layer-only `ToggleButton` + code-behind
mutual-exclusion state):

```csharp
public enum AssociateSection { Overview, Products, Notes }
public partial AssociateSection SelectedSection { get; set; } = AssociateSection.Overview;
```

`AssociateView.axaml`'s three rail items are now plain `Button`s with a
data-bound `active` class and a `Command` — the exact same pattern
`MainWindow.axaml`'s own top-level section strip already uses — instead
of `ToggleButton.IsChecked` bindings via `#ElementName`; the three
content `ScrollViewer`s bind their `IsVisible` to
`IsOverviewSectionSelected`/`IsProductsSectionSelected`/`IsNotesSectionSelected`
instead of `#OverviewSectionButton.IsChecked` and friends.
`AssociateView.axaml.cs`'s `OnSectionButtonClick` code-behind handler was
deleted entirely — there is no view-layer navigation state left.

`OperationsDashboardViewModel.OpenAssociateRequested` (one event shared
by both actions since Phase 10B) is now two distinct events:
`OpenAssociateOverviewRequested` and `OpenAssociateNotesRequested`.
`MainViewModel` — already the sole owner of top-level navigation — is the
one place that reacts to each:

```csharp
Dashboard.OpenAssociateOverviewRequested += (_, _) => { Associate.SelectedSection = AssociateSection.Overview; ShowAssociateSection(); };
Dashboard.OpenAssociateNotesRequested += (_, _) => { Associate.SelectedSection = AssociateSection.Notes; ShowAssociateSection(); };
```

Neither event handler touches `AssociateViewModel.SelectedDate` — "Open
Passdown"/"Open Notes" always land on whatever date Associate already
has selected (Part H); a future "Open Today's Passdown" that
deliberately switches the date is a distinct, differently-labeled action
this phase does not add. Setting `SelectedSection` explicitly on every
"Open Passdown" click (not only when it differs) means Part G's rule —
Open Passdown always means Overview, even if Associate was last left on
Products/Notes — holds regardless of Associate's prior state.
`OperationsDashboardViewModel` itself still has zero knowledge of
`AssociateViewModel`/`AppSection`/`AssociateSection` — it only raises
plain events, exactly as Phase 10A's Part P design required.

## Open History routing (Part D)

Unchanged from Phase 10A: `OpenHistoryCommand` raises
`OpenHistoryRequested`, and `MainViewModel` turns that into
`ShowHistorySection()`. The optional "Open Latest" (pre-selecting the
latest finalized date inside History) was evaluated and **not**
implemented: `PassdownHistoryViewModel.SelectedItem` is a
`PassdownHistorySummaryPresentation`, not a `DateOnly`, and there is no
existing seam for one view model to hand another a "please select this
item" instruction without either reaching into the other's internals or
introducing a third coordination type solely for this one case. Plain
`Open History` (landing on History's own default view, whatever it was
last showing — see Part I) is the implementation, matching the spec's
documented fallback.

## History state isolation (Part I)

No code change was needed: `PassdownHistoryViewModel` was already
constructed once at composition-root time and kept alive for the app's
whole lifetime (Phase 9D), and `MainWindow.axaml` already only toggles
`IsVisible` rather than recreating the view. Phase 10C adds the
regression test that proves this specifically for a Dashboard round trip
(`MainViewModelNavigationTests.NavigatingHistoryThenDashboardThenHistory_PreservesHistorySelection`):
selecting a History row, switching to Dashboard, then back to History,
still shows the same `SelectedItem`.

## Dashboard Reload (Parts J/K)

Unchanged mechanism (`ReloadCommand` still calls
`IOperationsDashboardService.GetTodayAsync()`, no new dependency) — but
because `GetTodayAsync`/`LoadAsync` now also recompute `LatestFinalized`
from the same metadata call, pressing Reload naturally picks up a newly
finalized record that appeared since the initial load (e.g. after a
background `StartupCatchUpRefresh` finalizes an overdue date), with no
polling or event-bus infrastructure added
(`OperationsDashboardViewModelTests.ReloadCommand_PicksUpNewlyFinalizedMetadata`).
Startup catch-up interaction (Part K) stays exactly as Phase 10A
documented it: the Dashboard renders its initial local state
immediately, and a supervisor's manual Reload is what surfaces anything
that changed in the background — no automatic refresh was added this
phase either.

## Accessibility (Part R)

The "Latest Finalized" panel carries
`AutomationProperties.Name="Latest finalized passdown"`; `Open History`
keeps its existing name (now attached to the one remaining button, not
two). `DashboardView_WithSnapshot_ExposesCriticalAutomationNames` was
extended to assert this new name alongside the existing four.

## Presentation models (Part S)

`OperationsDashboardPresentation.LatestFinalized` is a
`PassdownHistorySummaryPresentation?` — built by calling
`PassdownHistorySummaryPresentation.Build` directly on the snapshot's
`FinalizedPassdownSummary`, reusing History's own list-row formatter
rather than writing a second date/timestamp/origin formatter. No new
presentation type touches a repository, computes a business rule, or
deserializes a historical snapshot.

## Visual validation (Part U) — PARTIAL

Same constraint as every prior Phase 9/10 document: no pixel/bitmap
screenshot renderer exists in this codebase. `DashboardView` and the
rebuilt `AssociateView` section rail were reviewed directly against the
1280×860 layout target (the new "Latest Finalized" panel is one compact
full-width row, keeping the page's existing `ScrollViewer` fallback for
960×640 unchanged) and exercised through Avalonia's headless
real-XAML-loader construction checks for every representative state the
spec lists: no history, one finalized record, a no-PDF finalized record,
draft-in-progress, no-draft, and (already covered by Phase 10B) long
notes/Daily Feature content. "Many finalized records" was validated at
the Application layer instead of the View layer
(`LoadAsync_LargeFinalizedHistorySet_RemainsMetadataOnly`, 50 rows) since
the Dashboard only ever renders one row (the latest) regardless of how
many exist — there is no additional View-layer rendering path that
scales with History's size. Recorded honestly as **PARTIAL**, matching
every prior phase's precedent.

## Tests added (Part T)

- `OperationsDashboardServiceTests`: +9 — no-history/one-record/
  multi-record-highest-date-wins/no-PDF/malformed-JSON/future-schema/
  large-history-metadata-only latest-finalized cases, plus
  "other dates finalized never affects today's own status".
- `OperationsDashboardPresentationTests`: +2 — null passthrough and
  exact-match reuse of `PassdownHistorySummaryPresentation.Build`.
- `OperationsDashboardViewModelTests`: renamed 2 existing cases for the
  split `OpenAssociateOverviewRequested`/`OpenAssociateNotesRequested`
  events, +1 new (`ReloadCommand_PicksUpNewlyFinalizedMetadata`).
- `DashboardViewTests`: +4 construction-level cases (no-history,
  one-record, no-PDF-record, draft-in-progress) plus an accessibility
  assertion extension.
- `MainViewModelNavigationTests`: +5 — Open Passdown routes to Overview,
  Open Notes routes to Notes, neither changes `SelectedDate`, switching
  top-level sections alone never changes Associate's internal section,
  History selection survives a Dashboard round trip.
- `AssociateViewModelTests`: +5 — internal section default/commands/
  independence from `SelectedDate` and from triggering a load.
- `AssociateViewTests`: +1 theory (3 cases) — the rebuilt Button-based
  rail constructs through the real XAML loader for every
  `AssociateSection` value.

28 new tests total (914 Phase 10B baseline + 28 = 942).

## Full validation

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` —
**942/942**, run twice to confirm no flakiness. No live internet was used
or required.

## What Phase 10C does *not* implement

- Alerts or any data-confidence/staleness scoring (Phase 10D).
- The "Open Latest" History pre-selection variant — evaluated and
  rejected as requiring cross-view-model coupling not worth it for this
  phase (see "Open History routing" above); plain `Open History` is used
  instead, matching the spec's own documented fallback.
- An operations calendar, a persistent Notes Manager, a product/pricing
  reference tool, or the Manager workflow (still deferred per CLAUDE.md).
- Any network call from Dashboard load or Reload — still local-only by
  construction, unchanged from Phase 10A.
- Automatic/event-driven Dashboard refresh when a background
  finalization completes — manual `ReloadCommand` only, unchanged from
  Phase 10A/10B (Part K).
- Revising Phase 9's finalization/History semantics in any way — the
  6:00 AM Eastern cutoff, insert-only repository, and metadata-only
  `ListAsync` contract are all unchanged.
- Rebuilding the release ZIP — Phase 10F's scope.
- Visual/desktop polish beyond what this integration required
  (Phase 10E).

---

# Phase 10D — Data Confidence and Source Status Surfaces

Phase 10D answers a different question than 10A–10C: not "what happened
today" but **"is what I'm looking at trustworthy right now?"** It replaces
Phase 10A's simple 3-state `DashboardSourceHealthSummary` (`AllCurrent`/
`SomeStale`/`SomeUnavailable`) with a richer read model built over the exact
same per-category `AssociateVerificationState` values — no new freshness
system, no persistent alerts, no notifications, and no automatic network
call. This is explicitly **not** Phase 13's Alerts & Data Confidence system:
there is no persisted alert state, no background polling, and nothing here
survives past the current in-memory snapshot.

## Part A — authoritative source/freshness semantics (audited, not reinvented)

Everything below was read from the existing model, not invented:

- **`AssociateVerificationState`** (`Application/Associate/AssociateVerificationInfo.cs`):
  `NotCheckedYet, Verified, Closed, NotPublished, Unavailable, Error, Stale` —
  a 1:1 relabeling of Domain's `RetrievalStatus` plus `NotCheckedYet` for
  "never attempted."
- **`Stale` is derived, never stored**: `LastKnownGood<TValue>.IsStale`
  (`Domain/Retrieval/LastKnownGood.cs`) is `HasSuccess &&
  LastAttemptEvidence != SuccessEvidence` — "the latest attempt is not the
  same evidence as the last success." It is *not* a time-window comparison;
  a value that has never been re-checked since it succeeded is not stale no
  matter how old it is. Consequently, once any success has ever been
  recorded, a later failed attempt (`RetrievalStatus.Error`, the only status
  `IsSuccessfulCompletion()` calls a failure) always renders as `Stale`,
  never as `Error` — `Error` as an *effective* status can only occur when
  nothing has ever succeeded at all.
- **`LastVerifiedAt` is always success-only**: `AssociateVerificationInfo.From`
  sets it to `SuccessEvidence?.TimeChecked` unconditionally — even while
  `Stale` — so it always names the last time real data was confirmed, never
  a failed attempt's time.
- **Last-known-good (LKG)**: `SuccessValue`/`SuccessEvidence` are kept
  fully separate from `LastAttemptEvidence`; `RecordAttempt` never lets a
  failure overwrite a prior success (spec §6.7). This is why a stale value
  is still safely displayable — it is never discarded.
- **Category-level timestamps differ by source shape**, and Phase 10D
  respects the difference rather than flattening it:
  - Park Hours (`AssociateParkHoursView.Verification`) carries a full
    `AssociateVerificationInfo` with a genuine success-only `LastVerifiedAt`.
  - The three status-tracked dated categories (Admission, Fast Lane,
    Haunted — `AssociateWorkspace.CategoryStatuses`,
    `AssociateProductCategoryStatus`) only carry `LastCheckedAt`, explicitly
    documented as "most recent attempt of any outcome" — **not**
    success-only.
  - Catalog categories (Season Passes, Add-Ons, Promotions) have no
    category-level status record at all; every persisted
    `CatalogProductRecord` is always `Verified` with a genuine success-only
    `LastVerifiedAt` (`AssociatePassdownService.VerificationFromCatalog` —
    `IProductReconciliationService.ReconcileAsync` requires `Verified`
    evidence to persist anything, so a stored catalog row can never be
    anything else).
- **Freshness windows** (`Application/Freshness/FreshnessOptions.cs`,
  `FreshnessPolicyCatalog.cs`) drive the *scheduler's* "when is this source
  due again" (`ISourceStateRepository`/`FreshnessPlanner`) — daily-cadence
  for dated categories, 7-day rolling for Season Passes, 14-day rolling for
  Add-Ons. Phase 10D does not read or re-derive any of this; it has no
  dependency on `ISourceStateRepository` at all (see Part X).

## Part B — what "Data Status" means here

The Data Status card answers "are today's displayed values current enough
to trust operationally?" — never absolute correctness. Vocabulary used:
`Current`, `Stale`, `Not published`, `Unavailable`, `Verification issue`,
`Not checked yet`. Never `Accurate`/`Safe`/`Guaranteed`.

## Parts C/R — overall aggregation model

`OperationsDashboardSnapshot.DataHealth` (`Application/Dashboard/OperationsDashboardSnapshot.cs`)
replaces the old `SourceHealth` field:

```csharp
public enum DashboardSourceStatus { NotChecked, Current, Stale, NotPublished, Unavailable, VerificationIssue }

public sealed record DashboardSourceStatusEntry(
    SourceCategory Category, DashboardSourceStatus Status,
    DateTimeOffset? LastVerifiedAt, DateTimeOffset? LastCheckedAt);

public enum DashboardOverallDataStatus { Current, SomeStale, SomeUnavailable, VerificationIssue, NoPublishedData }

public sealed record DashboardDataHealthSummary(
    DashboardOverallDataStatus OverallStatus,
    int CurrentCount, int StaleCount, int UnavailableCount, int VerificationIssueCount, int NotPublishedCount, int NotCheckedCount,
    IReadOnlyList<DashboardSourceStatusEntry> Sources,
    DateTimeOffset? LatestSuccessfulVerification);
```

`OperationsDashboardService.Classify` maps `AssociateVerificationState` to
`DashboardSourceStatus` 1:1, except `Verified`/`Closed` both collapse to
`Current` (Part D) and `Error` is renamed `VerificationIssue` for
plain-language display (Part T) — no other reinterpretation.

**Deterministic precedence** (`OperationsDashboardService.BuildDataHealth`),
worst first:

```
VerificationIssueCount > 0   -> VerificationIssue
StaleCount > 0                -> SomeStale
UnavailableCount > 0           -> SomeUnavailable
CurrentCount == 0              -> NoPublishedData
otherwise                      -> Current
```

This was chosen over the spec's illustrative "Error > Stale > Unavailable >
NotPublished > Current" precisely because the audit in Part A shows
`NotPublished` and `NotChecked` are not failures — they never appear in the
precedence chain directly; they only surface as `NoPublishedData` when
*nothing* is currently confirmed either (`CurrentCount == 0`). One
`NotPublished` source alongside three `Current` ones still reports
`Current` overall (`OperationsDashboardServiceTests.LoadAsync_OneCategoryNotPublished_OverallStaysCurrentWhenAnotherIsCurrent`).
`LoadAsync_NothingEverChecked_ReportsNoPublishedData` also fixes a real gap
in the old 3-state model: it previously reported `AllCurrent` when nothing
had ever been checked at all — fabricated confidence Part B/Q explicitly
forbids. That is now `NoPublishedData`.

## Part D — Closed is not a confidence issue

`Classify(AssociateVerificationState.Closed) => DashboardSourceStatus.Current`
— a genuinely, successfully verified "the park is closed today" counts
toward `CurrentCount`, not any issue bucket
(`OperationsDashboardServiceTests.LoadAsync_ParkHoursVerifiedClosed_CountsAsCurrentNotIssue`).
This was already true of the old `SourceHealth.CurrentCount` computation
(`state is Verified or Closed`) — Phase 10D carries the rule forward rather
than reinventing it.

## Part E — NotPublished is informational

A `NotPublished` source never degrades `OverallStatus` on its own when at
least one other source is `Current` — it only participates in
`NoPublishedData` when nothing else is confirmed either (see Part C's
precedence). This distinguishes "source successfully checked, nothing
published yet" from "source could not be retrieved" exactly as the spec
asks, using the existing `AssociateVerificationState.NotPublished` value —
no new "checked vs not checked" flag was added.

## Part F/J — Stale/LKG and timestamp semantics

A source that succeeded once and then failed on a later attempt reports
`Stale` while its `LastVerifiedAt` still names the **old** success
timestamp, never the failed attempt's time
(`OperationsDashboardServiceTests.LoadAsync_ParkHoursSucceededThenErrored_ReportsStaleWithOldVerifiedTimestamp`
— the underlying operational value itself is also untouched, per Part A's
LKG guarantee). `DashboardSourceStatusEntry` deliberately carries two
mutually-exclusive optional timestamps rather than one ambiguous one:

- `LastVerifiedAt` — populated only when the source's data can *prove* the
  timestamp is success-only (Park Hours, the three catalog categories).
  Presented as `"Last verified {time}"`.
- `LastCheckedAt` — populated instead for the three dated-tracked
  categories (Admission/Fast Lane/Haunted), whose only available timestamp
  (`AssociateProductCategoryStatus.LastCheckedAt`) is attempt-based, not
  success-only. Presented as `"Last checked {time}"` — **never** relabeled
  "verified" (`OperationsDashboardServiceTests.LoadAsync_DatedTrackedCategory_NeverExposesLastVerifiedTimestamp`).

`DashboardDataHealthSummary.LatestSuccessfulVerification` is the max of
every source's own `LastVerifiedAt` only — it deliberately excludes the
three dated categories' `LastCheckedAt`, so the Dashboard's one headline
"Last verified" timestamp can never be a failed attempt's time. When no
source has ever succeeded, it is `null` and the "Last verified" line is
omitted entirely (never a fabricated timestamp).

## Part G — Error vs. Unavailable, preserved

`Unavailable` ("the date exists but that product/value is unavailable" —
`RetrievalStatus.Unavailable`'s own doc comment) and `VerificationIssue`
(`AssociateVerificationState.Error`, "the source could not be completed")
remain distinct buckets with distinct counts
(`UnavailableCount`/`VerificationIssueCount`) and distinct overall
precedence — `VerificationIssue` outranks `SomeUnavailable`, since it means
the source could not even be reached, versus `Unavailable` meaning the
source *was* reached and gave a definitive negative answer. No collapsing
was introduced; the existing Domain-level distinction is the only source of
truth used.

## Part H/I — source breakdown and grouping

All 7 `SourceCategory` values are always shown, grouped exactly as spec
Part I suggests (`DashboardDataHealthPresentation.Build`,
`Desktop/Presentation/DashboardDataHealthPresentation.cs`):

```
Today's Operation          Products / Offers
  Park Hours                  Season Passes
  Daily Admission              Add-Ons
  Fast Lane                    Promotions
  SCarowinds (Haunted)
```

Each row shows only a plain-language category label
(`SourceCategoryDisplay.FriendlyName`, reused from the existing Associate
screen — no second name table) and status text — never a raw endpoint,
URL, HTTP status, or exception message (Part T; the presentation type has
no field capable of carrying one). The per-row timestamp
(`"Last verified …"`/`"Last checked …"`) is available via
`ToolTip.Tip`/`AutomationProperties.HelpText` rather than always-visible
text, keeping the card compact per Part AB while staying accessible without
a mouse (the help text is present regardless of hover).

Catalog categories with zero currently-stored products report
`NotChecked`, not a fabricated `NotPublished`/`Unavailable` — this service
has no way to distinguish "never retrieved" from "retrieved and genuinely
empty" without a new dependency on `ISourceStateRepository`, which stays
out of scope for Phase 10D (Part X; documented here so it isn't
rediscovered as a bug later).

## Part K — Data Status card and layout placement

`DashboardView.axaml` gained one full-width panel, **Data Status**,
positioned after "Today at a Glance" and before the Operational
Notes/Latest Finalized row — higher than a plain "half of a two-column row"
placement, because Phase 10D gave it a compact count-chip row, a 7-source
breakdown, and the Check Online action, making it the densest panel on the
page after the hero row. The prior "Data Verification" half-panel is gone;
Operational Notes now pairs with Latest Finalized instead (both compact,
single-purpose panels), keeping the page's overall row count unchanged
(still 4 stacked rows, no taller than Phase 10C's layout).

```
DATA STATUS                                    [ Check Online ]
Some information is stale
Some source(s) are showing older information.
Last verified Aug 25 at 5:58 AM

Current 5   Stale 1   Not published 1

Today's Operation          Products / Offers
  Park Hours    Current      Season Passes   Current
  ...                        ...
```

## Parts L/M/N/O — Check Online integration

**Direct reuse, not a navigation fallback.** `OperationsDashboardService`
already depended on `IAssociatePassdownService` (for
`LoadAssociateDateAsync`), and that interface already exposes
`CheckAssociateDateOnlineAsync` — the exact method
`AssociateViewModel.CheckOnlineCommand` calls. Phase 10D adds
`IOperationsDashboardService.CheckOnlineAsync` (`OperationsDashboardCheckOnlineResult`)
that simply calls it for today's own operating date and rebuilds the
snapshot from the refresh's own recomposed workspace — **no new
dependency, no second refresh implementation**, and
`OperationsDashboardServiceTests.CheckOnlineAsync_CallsExistingManualRefreshForToday`
proves it drives the same `IRefreshOrchestrator.RunManualDateRefreshAsync`
call Associate's own Check Online does. This made Part M's navigation
fallback ("Open Passdown to Check Online") unnecessary — direct reuse was
architecturally clean.

`OperationsDashboardViewModel.CheckOnlineCommand`:

- `IsCheckingOnline` is a **separate** busy flag from `IsLoading` (the
  local-only load/reload) — `CanExecute` on both `CheckOnlineCommand` and
  `ReloadCommand` cross-references the other, so neither can run
  concurrently with the other, and repeated clicks cannot start a second
  concurrent check
  (`OperationsDashboardViewModelTests.CheckOnlineCommand_CannotRunConcurrently`).
- Progress/result text reuses `Desktop/Presentation/RefreshMessages.cs`
  verbatim (`RefreshMessages.InProgressDefault`/`.Progress`/`.Summary`) —
  the exact same wording Associate's own Check Online shows, not a second
  copy.
- A failed Check Online (the refresh call itself throwing, not an
  individual source failing — each source already fails independently
  inside the orchestrator) leaves the existing snapshot untouched and shows
  a plain-language message, never the raw exception
  (`OperationsDashboardViewModelTests.CheckOnlineCommand_Failure_PreservesExistingSnapshotAndSetsMessage`).
- Partial/failed refreshes preserve LKG exactly as Associate's Check Online
  already does — nothing new was built here, it is the same
  `RunManualDateRefreshAsync` contract
  (`OperationsDashboardServiceTests.CheckOnlineAsync_FailedRefresh_PreservesLastKnownGood`).

## Part P — no network on normal load, reconfirmed

`OperationsDashboardServiceTests.LoadAsync_NeverTriggersOnlineRefresh`
asserts `FakeRefreshOrchestrator.ManualDateRefreshCalls` stays empty after
both `GetTodayAsync()` and `LoadAsync()` — Check Online is a distinct
method (`CheckOnlineAsync`) that nothing in the load path calls.
`OperationsDashboardService`'s constructor is unchanged (still
`IAssociatePassdownService, IFinalizedPassdownRepository, IClock`) and
`OperationsDashboardViewModel`'s constructor is also unchanged (still
`IOperationsDashboardService, ILogger?`) — the existing
`Constructor_HasNoRefreshOrchestratorOrRetrievalDependency`/
`Constructor_HasNoRefreshOrPdfDependency` reflection tests continue to pass
unmodified, proving Check Online added zero new constructor-level coupling.

## Part Q — Reload Summary vs. Check Online

Unambiguous, distinct labels on the Data Status card and header:

- **Refresh** (header button, unchanged from Phase 10A) — local database
  reread only (`ReloadCommand` → `IOperationsDashboardService.GetTodayAsync()`).
- **Check Online** (Data Status card, new) — explicit source retrieval
  (`CheckOnlineCommand` → `IOperationsDashboardService.CheckOnlineAsync()`).

Neither label is a bare "Refresh" duplicated across two buttons.

## Part W/X — dated vs. catalog source state, respected

Dated categories (Park Hours, Admission, Fast Lane, Haunted) are always
classified from `operatingDate`'s own composed workspace — never another
date's cached result, since `AssociateWorkspace` itself is already scoped
per-date. Catalog categories (Season Passes, Add-Ons, Promotions) are
global/catalog-scoped, not date-scoped — Phase 10D reuses whatever
`AssociateProduct.Verification`/`CatalogProductRecord.LastVerifiedAt`
already say rather than inventing a catalog freshness window of its own
(Part A's freshness-window audit).

## Part Y — finalized History isolation, unaffected

`OperationsDashboardSnapshot.LatestFinalized` is untouched by Phase 10D —
still built purely from `FinalizedPassdownSummary` metadata, with zero
reference to `DataHealth`/live source state anywhere in
`OperationsDashboardService.BuildPassdownAndHistoryAsync` or
`OperationsDashboardPresentation`. A finalized passdown can never become
"stale" because today's live source data changed — the two systems don't
intersect. No dedicated regression test was added beyond the existing
Phase 10C History-isolation coverage, since no code path connects them.

## Part Z — diagnostics privacy, reconfirmed

`Application/Diagnostics/DiagnosticReportBuilder.cs` and
`Infrastructure/Diagnostics/DiagnosticExportService.cs` were not touched by
Phase 10D — neither references `DashboardDataHealthSummary`/
`DashboardSourceStatusEntry` at all. The full `Carowinds.Passdown.Ui.Tests`
suite (including the existing diagnostics-export tests) passes unmodified,
confirming the sanitization rules documented in Phase 9/earlier phases
remain intact.

## Part AA — accessibility

- Data Status panel: `AutomationProperties.Name="Data status"`.
- Check Online button: `AutomationProperties.Name="Check Online"` plus
  help text.
- Each source row carries `AutomationProperties.Name="{category label}"`
  and `AutomationProperties.HelpText="{timestamp text}"` (empty when no
  timestamp exists), so the row's meaning and freshness are both available
  to a screen reader without requiring hover.
- No status is color-only — every badge is a plain-language `TextBlock`
  (`Current`/`Stale`/`Not published`/`Unavailable`/`Verification issue`/
  `Not checked yet`), matching the existing `ui-polish` convention already
  used by `ParkHoursPresentation`'s own badges.
- `DashboardView_WithSnapshot_ExposesCriticalAutomationNames` extended to
  assert `"Data status"` and `"Check Online"` alongside the five existing
  names.

## Part AB/AC — layout density

At 1280×860 the Data Status card is one `Border.panel` with a header row
(headline/detail + Check Online action), a horizontal count-chip row
(`ItemsControl` over `UniformGrid`-free `StackPanel Orientation="Horizontal"`,
only non-zero buckets shown), and a two-column `Grid` of compact source
rows — no per-source card, no giant single-column stack. At 960×640 the
existing page-level `ScrollViewer` fallback (unchanged since Phase 10A)
applies; the Data Status card's own two-column source grid was reviewed
(not pixel-verified — see Part AE) against the narrower width and does not
introduce any new fixed pixel widths that would clip.

## Part AD — tests added

- `OperationsDashboardServiceTests`: +16 — overall aggregation for
  all-current/one-stale/one-error/one-unavailable/one-not-published,
  Closed-counts-as-Current, NotPublished-does-not-degrade-when-another-
  is-Current, Stale-preserves-old-verified-timestamp, dated-tracked
  categories never expose `LastVerifiedAt`, catalog category with/without
  a stored product, nothing-ever-checked now reports `NoPublishedData`
  (fixing the old model's fabricated-confidence gap), no-network-on-load,
  Check Online calls the existing manual refresh for today, Check Online's
  returned snapshot reflects newly retrieved data without a second load,
  and a failed Check Online preserves LKG.
- `DashboardDataHealthPresentationTests` (new file): +11 — overall headline
  vocabulary for every state (never the raw enum name, `Current` excluded
  as the one legitimate coincidence), count-chip suppression of zero
  buckets, "Last verified"/"Last checked"/omitted timestamp fallback rules,
  operational/product group split, and no raw technical detail in status
  text.
- `OperationsDashboardViewModelTests`: +3 — Check Online updates
  Snapshot/Presentation and shows a result message, a Check Online failure
  preserves the existing snapshot and never leaks the raw exception, and
  Check Online cannot run concurrently with itself.
- `DashboardViewTests`: +2 automation names (`"Data status"`,
  `"Check Online"`) on the existing assertion, +1 `[Theory]` (5 cases) over
  every `DashboardOverallDataStatus`, +1 Check Online busy-state
  construction case.

38 new tests total (942 Phase 10C baseline + 38 = 980).

## Full validation

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` —
**980/980**. No live internet was used or required.

## Visual validation — PARTIAL

Same constraint as every prior Phase 9/10 document: no pixel/bitmap
screenshot renderer exists in this codebase. `DashboardView` was reviewed
directly against the 1280×860 layout target (the new Data Status panel's
header/chip-row/two-column-grid structure) and exercised through
Avalonia's headless real-XAML-loader construction checks for every
representative overall state (`Current`/`SomeStale`/`SomeUnavailable`/
`VerificationIssue`/`NoPublishedData`) plus the Check Online busy state.
Recorded honestly as **PARTIAL**, matching every prior phase's precedent —
not claimed as a pixel-verified PASS.

## What Phase 10D does *not* implement

- A full Alerts system, persistent alert state, or notifications — nothing
  here survives past the current in-memory `OperationsDashboardSnapshot`.
- An Operations Calendar or a persistent Notes Manager.
- The Manager workflow (still deferred per CLAUDE.md).
- Any new freshness/staleness time-window logic — `Stale` is still
  Domain's existing attempt-vs-success derivation (Part A); Phase 10D adds
  no new "is this too old" comparison.
- A new dependency on `ISourceStateRepository` — catalog categories with no
  stored products are reported `NotChecked`, not a more specific claim this
  service has no evidence for (Part H/X).
- Automatic/background Check Online — it remains a single explicit user
  action, exactly like Associate's own Check Online.
- Rebuilding the release ZIP — Phase 10F's scope.
- Redesigning Associate/History beyond what Phase 10C already changed.
- Visual/desktop polish beyond what this phase's own panel required
  (Phase 10E).

---

# Phase 10E — Visual/Desktop Polish

Phase 10E audits the combined Dashboard now that 10A–10D's content is all in
place, and makes targeted density/hierarchy/accessibility refinements — no
new business rule, no new field, no new panel type, no schema change.

## Audit (Part A)

Reviewed as one screen at the 1280×860 target: Today header → Today's
Operation / Passdown Status (hero row) → Today at a Glance → Data Status →
Operational Notes / Latest Finalized. A rough height accounting of the
existing (pre-10E) layout — header (~64px) + hero row (~180px) + Today at a
Glance (~107px) + Data Status (~247px) + the Notes/Latest-Finalized row
(~167px), plus `ScrollViewer` padding and inter-panel spacing — already came
in close to 860px, meaning the Part B goal ("little or no vertical
scrolling at 1280×860") was substantially already met by 10A–10D's own
layout choices. Phase 10E's job was therefore real refinement, not a
re-layout: closing genuine gaps found in the audit rather than restructuring
panels that already worked. Four gaps were found and fixed:

1. Four of the six panels (Today's Operation, Passdown Status, Today at a
   Glance, Operational Notes) had no `AutomationProperties.Name`, unlike
   Data Status/Latest Finalized (Phase 10C/10D) — an inconsistent
   accessibility landmark structure (Part W).
2. The Check Online busy state had no visual indicator distinct from the
   header's `IsLoading` `ProgressBar` — only text and a disabled button
   (Part R).
3. The Data Status count-chip row used a fixed horizontal `StackPanel`,
   which could clip instead of reflow if all six status buckets (Current/
   Stale/Unavailable/Not published/Issues/Not checked) were ever populated
   at once at the 960×640 fallback width (Part L/C).
4. The Data Status card — already the page's densest panel by design (Part
   K's own precedent) — had slightly looser internal spacing (`Spacing="10"`,
   `Margin="0,3"` per source row) than necessary; not oversized, but not as
   tight as it could be either.

Everything else audited clean: card balance (Today's Operation / Passdown
Status already render at equal height — a single-row `Grid` with two
`Stretch`-aligned `Border.panel` cells sizes both to the tallest content, no
explicit alignment code was needed), badge/chip typography (one shared
`Border.chip`/`TextBlock` style already used everywhere a chip appears),
button hierarchy (each card's one primary action uses `Button.primaryAction`,
everything else `Button.secondaryAction` — Open Passdown and Check Online
are each the single obvious action *within their own card*, matching
`AssociateView`'s own established Check Online-is-primary precedent, not a
page full of competing primary buttons), and color restraint (no literal
colors in `DashboardView.axaml` — every brush is a `DynamicResource` from
`App.axaml`'s existing navy/blue palette; no red/error color appears at all
on the Dashboard today, since `VerificationIssue`/error states are
plain-language text, not a red field).

## 1280×860 / 960×640 (Parts B/C)

Unchanged Grid-based two-column panel structure, still wrapped in the page's
one `ScrollViewer` (Phase 10A). The WrapPanel fix (gap 3 above) is
specifically a 960×640 safeguard — previously untested with more than one or
two simultaneous non-zero count buckets. `Border.chip` timestamps stay
tooltip/automation-only (Phase 10D), so no new fixed-width text was
introduced that could clip at the narrower width.

## Navigation / header polish (Parts D/E)

`MainWindow.axaml`'s section strip and `DashboardView.axaml`'s header were
reviewed and left unchanged — both already matched the spec's compact,
text-first, non-oversized targets (`Button.appSectionItem`'s
18,11 padding; the header's eyebrow-plus-headline pair from Phase 10B).

## Park Status / Passdown Status (Parts F/G)

Unchanged presentation logic. Confirmed (not just assumed) that the two hero
cards render at equal height without any special-case layout code, since
they share one `Grid` row with default `Stretch` vertical alignment — this
is why no "align them intentionally" code change was needed for Part G.

## Today at a Glance (Part H)

Unchanged — already one compact `Grid` (not four separate cards), matching
the spec's own preference.

## Operational Notes / Latest Finalized (Parts I/J)

Unchanged content/truncation. Both panels gained `AutomationProperties.Name`
(gap 1).

## Data Status / source grid (Parts K/L)

- Count-chip row: `StackPanel Orientation="Horizontal"` → `WrapPanel`, each
  chip's own `Margin="0,0,8,4"` replacing the panel's `Spacing="8"` (a
  `WrapPanel` doesn't support `Spacing`) — reflows instead of clipping when
  every bucket is populated (gap 3; new regression test below).
- Internal spacing tightened: outer `StackPanel Spacing` 10→8, each source
  row's `Margin` "0,3"→"0,2" (gap 4) — a small, deliberate density
  improvement to the page's single densest panel, not a restructure.
- The two-column "Today's Operation" / "Products / Offers" source grid
  itself (Phase 10D) was reviewed and left unchanged — already compact,
  aligned, and free of per-source cards.

## Badge/status consistency (Part M) and color restraint (Part N)

Audited, no change needed — see Part A audit above.

## Spacing system / typography (Parts O/P)

No stray "7/11/13/18"-style magic numbers were found in `DashboardView.axaml`
beyond the two intentional tightening edits above (Part K); the existing
spacing scale (2/4/6/8/10/16/18/20/24) was already consistent with
`AssociateView`/`HistoryView`'s own values, so no broader spacing-token
refactor was needed (per CLAUDE.md's "do not over-refactor" guidance and the
spec's own "do not over-refactor the entire app's spacing tokens unless
needed").

## Long content / truncation (Part Q)

Unchanged — Phase 10B's bounded Daily Feature/notes-preview truncation was
re-verified via the existing `DashboardView_LongDailyFeatureAndNotes_ConstructsWithBoundedText`
test, still passing unmodified.

## Check Online busy/error state (Parts R/S)

Added a dedicated `ProgressBar` (`IsIndeterminate="True"`, `Height="3"`,
`Width="140"`) bound to `IsCheckingOnline` — distinct from the header's
`IsLoading`-bound `ProgressBar` (Part R gap 2), so the two different "busy"
concepts (local reload vs. explicit online check) never look like the same
indicator. No changes to the underlying error-handling logic
(`OperationsDashboardViewModel.CheckOnlineAsync` already preserved existing
values and showed a plain-language message on failure — Phase 10D; Part S
reconfirmed unchanged by re-running
`CheckOnlineCommand_Failure_PreservesExistingSnapshotAndSetsMessage`).

## Reload Summary vs. Check Online (Part T) and action hierarchy (Part U)

Reviewed, unchanged — "Refresh" (header) vs. "Check Online" (Data Status
card) already read as distinct actions with distinct help text (Phase 10D
Part Q), and the primary/secondary button split already matches the spec's
own suggested hierarchy (see Part A audit above).

## Keyboard navigation (Part V)

Manually traced the logical/XAML declaration order, which Avalonia's default
tab order follows: header Refresh → Today's Operation (no focusable
controls) → Open Passdown → Today at a Glance (no focusable controls) →
Check Online → Open Notes → Open History. This matches the spec's expected
routing (Dashboard → Open Passdown/Notes/History) with no focus traps —
every focusable element is a plain `Button`, and Avalonia's `FluentTheme`
supplies a default visible focus adorner with no custom override needed
(none of `AssociateView`/`HistoryView` override it either, so this stays
consistent app-wide). Disabled-while-busy buttons (`CheckOnlineCommand`/
`ReloadCommand`, both already cross-referencing `CanExecute` since Phase
10D) are correctly skipped in tab order by Avalonia's own `IsEnabled`
handling — no dedicated `KeyboardNavigation.IsTabStop` overrides were
needed.

## Accessibility text (Part W)

Added `AutomationProperties.Name` to the four panels identified in the audit
(gap 1): `"Today's Operation"`, `"Passdown Status"`, `"Today at a Glance"`,
`"Operational Notes"` — all short, meaningful, and matching their own
visible heading text (no `"Border1"`/`"Panel2"`-style names), consistent
with the existing `"Data status"`/`"Latest finalized passdown"` names from
Phase 10C/10D. `DashboardView_WithSnapshot_ExposesCriticalAutomationNames`
was extended to assert all four.

## Windows scaling validation (Parts X/Y) — NOT EXECUTED

No Windows machine is available in this environment (macOS/darwin host, not
the Fedora dev host CLAUDE.md describes, and no Windows VM was reachable
during this phase). Per CLAUDE.md ("flag Windows-only behavior for Windows
VM testing") and this document's own instructions ("If Windows is
unavailable: mark all Windows scaling checks NOT EXECUTED... Do not infer
Windows scaling from macOS/Linux"), 100%/125%/150% scaling and Windows
font/rendering review are recorded honestly as **NOT EXECUTED** rather than
inferred from this build/test environment. `dotnet build`/`dotnet test` were
run successfully on macOS (net10.0, cross-platform), which validates
compilation and headless-XAML-loader construction only — not actual Windows
rendering.

## Visual screenshot validation (Part Z) — PARTIAL

Same constraint as every prior Phase 9/10 document: no pixel/bitmap
screenshot renderer exists in this codebase (`Avalonia.Headless` is used
here only for real-XAML-loader construction checks). All of the spec's
representative states (normal/full data, sparse/no-draft, private buyout,
stale data, verification issue, long content, Check Online busy) already
have a construction-level `DashboardViewTests`/`OperationsDashboardServiceTests`
case from Phase 10A–10D, re-run and still green. `DashboardView.axaml` was
reviewed directly against the 1280×860/960×640 layout targets. Recorded
honestly as **PARTIAL**, matching every prior phase's precedent — not
claimed as pixel-verified.

## UI test stability (Part AA)

One genuine timing fragility was found and fixed **during this phase's own
test-writing**, not in pre-existing 10A–10D coverage: an initial draft of
the new Check-Online-busy-indicator test asserted the *live, binding-driven*
`ProgressBar.IsVisible` value on the real control immediately after starting
an async command (`CheckOnlineCommand.ExecuteAsync`). That assertion failed
intermittently when run alongside the rest of the Dashboard test file (80
tests) but passed reliably in isolation — a dispatcher/binding-propagation
timing race under this suite's parallel test execution, not a product
defect. It was rewritten to assert `OperationsDashboardViewModel` state
(`IsCheckingOnline`) instead of bound-control state, matching every other
async/busy test in this file's own established pattern
(`DashboardView_CheckOnlineBusy_ConstructsWithoutError`,
`OperationsDashboardViewModelTests.CheckOnlineCommand_CannotRunConcurrently`,
etc.) — the fixed test
(`DashboardView_CheckOnlineBusy_HasDedicatedBusyIndicator`) still verifies
the indicator control exists in the real XAML tree, just not its live
`IsVisible` transition. With that fix in place, the full Dashboard test
filter (80 tests) and the full non-live suite were each run three and two
times respectively with zero failures — no other flake reproduced. No test
was disabled, no parallelism was turned off app-wide, and nothing was
masked: the root cause (a test asserting Avalonia binding-propagation timing
under parallel execution) was identified and fixed at its source.

## No business logic / schema changes (Parts AB/AC)

Confirmed: no changes to hours semantics, finalization, History immutability,
source freshness/aggregation rules, Check Online retrieval logic, catalog
lifecycle, or persistence schema. Schema stays **9**; snapshot schema stays
**1** (both unchanged from Phase 9). Every 10E change is
`DashboardView.axaml` layout/accessibility markup plus its own regression
tests — no `Application`/`Domain`/`Infrastructure` file was touched.

## Regression matrix (Part AD)

All 15 representative combinations the spec lists already have direct
coverage from Phase 10A–10D's own tests (`DashboardViewTests`,
`OperationsDashboardServiceTests`, `OperationsDashboardPresentationTests`,
`DashboardDataHealthPresentationTests`), all re-run green in this phase; no
gap required a new combination beyond the two added below.

## Tests added (Part AE)

- `DashboardViewTests`: +2 —
  `DashboardView_CheckOnlineBusy_HasDedicatedBusyIndicator` (the new busy
  `ProgressBar` exists, is distinct from the header's, and both busy/idle
  states construct cleanly) and
  `DashboardView_AllDataStatusCountBucketsPopulated_ConstructsWithoutError`
  (all six count-chip buckets populated at once, exercising the new
  `WrapPanel`). The existing `DashboardView_WithSnapshot_ExposesCriticalAutomationNames`
  test was extended (not counted as new) to assert the four newly-named
  panels.

2 new tests total (980 Phase 10D baseline + 2 = 982).

## Full validation

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` —
**982/982**, run twice with identical results. The Dashboard-focused test
filter was additionally run three times on its own (80/80 each run) to
specifically confirm the Part AA fix. No live internet was used or
required. Schema: **9**. Snapshot schema: **1**.

## What Phase 10E does *not* implement

- Any new Dashboard field, panel, or business rule — see Parts AB/AC above.
- Alerts, an Operations Calendar, a persistent Notes Manager, or the Manager
  workflow (still deferred per CLAUDE.md).
- A pixel/bitmap screenshot renderer — visual validation stays PARTIAL,
  matching every prior phase's own honest precedent.
- Actual Windows-scaling verification — NOT EXECUTED, no Windows machine
  available in this environment; still a Phase 10F (or later Windows-VM
  session) candidate before release sign-off.
- The Phase 10F release rebuild/audit.

## Phase 10F remaining work

- Rebuild and validate the Windows release ZIP.
- Execute the deferred 100%/125%/150% Windows scaling and font/rendering
  checks on an actual Windows machine/VM (Parts X/Y, NOT EXECUTED here).
- Final Phase 10 audit across 10A–10E as one release-readiness pass.

---

# Phase 10F — Final Audit and Release Closeout

Phase 10F closes out Phase 10 as a shipped feature set. It adds no new production behavior — it is a
final requirements audit of 10A–10E as one system, an architecture/isolation/immutability re-check, a
release rebuild, and a documentation checkpoint. See `docs/release-strategy.md` "Phase 10F rebuild"
for the full audit (32-item requirements matrix, architecture/startup/date-isolation/History/Data-Status/
Check-Online/UI-accessibility/diagnostics-privacy/schema audits) and `docs/release-manifest.md` for the
resulting artifact's exact identity.

**Audit outcome.** Every Phase 10A–10E claim in this document was checked directly against current
code and a fresh full test run rather than trusted from prior summaries alone: `OperationsDashboardService`'s
constructor is still exactly `(IAssociatePassdownService, IFinalizedPassdownRepository, IClock)`,
`OperationsDashboardViewModel`'s is still exactly `(IOperationsDashboardService, ILogger?)`, no SQL/HTTP
string appears in the Dashboard's own ViewModel/View files, `MainViewModel.SelectedSection` still
defaults to `AppSection.Dashboard`, the migration list registered in `App.axaml.cs` still ends at
`Migration0009AddFinalizedPassdownPdfColumns` (schema version **9**, unchanged), and
`FinalizedPassdownSnapshot.CurrentSchemaVersion` is still **1**. No defect was found; no code change
was needed.

**Validation.** `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **982/982** on a clean run,
identical to Phase 10E's count (no test added, removed, or weakened). A second full-solution run
reproduced the pre-existing `NoManagerWorkflowTests` Avalonia parallel-execution flake first recorded
in Phase 6D (`docs/architecture.md`) — unrelated to any Phase 10 file, and the same test file passed
291/291 cleanly when re-run standalone immediately afterward. See `docs/release-strategy.md` "Phase
10F rebuild" Part L for the full account.

**Release rebuild.** The release ZIP that Phase 10's own subphases correctly left unrebuilt (Phase 9F's
artifact predates the entire Operations Dashboard) has been rebuilt from this exact commit. The freshly
published `Carowinds.Passdown.Desktop.dll`/`.Application.dll` were checked directly for Phase 10A–10E-only
symbols (a byte-level `strings` search of the actual built assemblies, on both the pre-zip publish
output and an independently re-extracted copy of the finished ZIP — not just file timestamps) to
confirm this is not a stale reuse of the Phase 9F artifact — see `docs/release-manifest.md` for the
exact symbol list and the resulting SHA-256/size/file-count. `packaging/windows/TESTER-GUIDE.txt` and
`packaging/windows/README-portable-release.md` were updated with a short, plain-language description of
the Dashboard as the app's new default landing screen (they described Associate Passdown as the landing
screen before this rebuild, since Dashboard did not exist when they were last written).

**A discrepancy in the task brief that requested this rebuild was found and corrected, not silently
reconciled.** The SHA-256 the brief named as the artifact being superseded
(`1684f9146534a7ed7f58cf9e4124cd558aabc6f5f8121dc91135ff46a426ba71`) does not match any artifact this
repository's own history records; the actual prior-current artifact was the Phase 9F artifact
(`ccd41c76a79824b3e923e23d209b0ac15e4bf8059b491a17004ce4ca8f1979c4`), verified directly against this
document's and `docs/release-manifest.md`'s own pre-rebuild content. `docs/release-manifest.md` now
supersedes the real hash and records this discrepancy explicitly, per CLAUDE.md's "never invent data"
instruction — release identity is not exempt from that rule.

**Remaining gaps, honestly recorded (not fixed this phase, not blockers):**
- Dashboard pixel-level Windows visual validation — no bitmap/screenshot renderer exists in this
  codebase; only real-XAML-loader construction-level checks (unchanged since Phase 9E/10A–10E).
- Windows 100%/125%/150% display-scaling validation — not executed, no Windows host available in this
  or the Phase 10E session (carried forward, not re-attempted or inferred from macOS/Linux).
- A focused Windows Dashboard smoke test (Open Passdown/Open Notes/Open History round trips, Check
  Online busy/result states, Export Diagnostics) against this exact rebuilt ZIP — not executed, no
  Windows host available.
- Phase 9 History/finalization regression smoke and automatic-refresh Task Scheduler live
  install/rebind, both specifically against this rebuilt artifact — not executed; automated real-SQLite
  integration tests and static `.cmd`-content tests stand in, unaffected by any Phase 10 change.
- Carried forward from Phase 7E/7F/9F: native x64 (vs. ARM64 emulation) Windows validation, and
  self-contained-runtime confidence on a machine with no .NET pre-installed.
- The pre-existing `NoManagerWorkflowTests` Avalonia parallel-test flake (first recorded Phase 6D) —
  reproduced once this session under full-solution parallel execution, confirmed non-deterministic
  (clean standalone re-run), not a Phase 10 regression.

**Phase 10 complete.** 10A (foundation) through 10F (release closeout) are done, audited, and shipped
in the rebuilt artifact identified in `docs/release-manifest.md`. Intentionally **not** implemented,
and not implied to exist by anything above:
- persistent Alerts or notifications,
- an Operations Calendar,
- a persistent Notes Manager (Dashboard's Operational Notes remains a bounded preview only),
- the Manager workflow (deferred per CLAUDE.md "Current scope decision"),
- any new freshness/staleness time-window redesign beyond Domain's existing attempt-vs-success `Stale`
  derivation.

Phase 11 has not started.
