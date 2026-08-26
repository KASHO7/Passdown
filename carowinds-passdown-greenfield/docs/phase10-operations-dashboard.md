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
