# Phase 12A — Operations Calendar foundation

Phase 12A (this document's current scope) builds the Operations Calendar's **foundation**: a
top-level month-grid navigation surface, explicit Eastern-aware date semantics, a local-only
Application-layer read model/service, and clean routing into the existing Associate Passdown/
History/Operational Notes screens. It is not a full calendar experience — richer overlays, alerts,
and any Manager-workflow integration remain out of scope (CLAUDE.md "Current scope decision";
see "What Phase 12A does *not* implement" below).

## Purpose and boundary

> "What dates should I pay attention to, and where do I go next?"

The Operations Calendar is a **summary/routing surface**, the same category `OperationsDashboard`
(Phase 10) and `PassdownHistory` (Phase 9D) already occupy — not a second Associate editor, not a
Notes editor, not a History editor, and not a refresh screen. It shows a month grid, lets a
supervisor select a date, shows a concise selected-date summary, and routes into the existing
detailed screens using that date context. No editing control of any kind exists on this screen.

## Top-level navigation

```
Dashboard | Operations Calendar | Associate Passdown | Operational Notes | History
```

`AppSection` (`MainViewModel.cs`) gained one value, `OperationsCalendar`, placed between
`Dashboard` and `AssociatePassdown` — a genuinely new top-level section, never nested inside
Dashboard's or Associate's own internal navigation. Dashboard remains the default startup section
(`SelectedSection = AppSection.Dashboard`, unchanged). No `Manager`/`Alerts` entry exists.

`OperationsCalendarViewModel` is constructed once at composition-root time (`App.axaml.cs`) and
stays alive for the app's whole lifetime, exactly like `Dashboard`/`Associate`/`Notes`/`History` —
switching `SelectedSection` only toggles which section's `Border` is visible in `MainWindow.axaml`;
it never recreates the Calendar's own displayed-month/selected-date state, and it never touches
`AssociateViewModel.SelectedDate` on its own. The Operational Notes "dirty editor" navigation guard
(Phase 11B Part X) was extended to also cover the new `ShowOperationsCalendarSectionCommand`, so a
supervisor mid-create/edit on a persistent note still cannot navigate to Calendar (or any other
section) until they Save or Cancel.

## Date semantics

- **"Today"** is always resolved via `IClock` + `EasternTimeZone.ConvertFromUtc` — the same pattern
  `AssociateViewModel`/`OperationsDashboardViewModel` already use. `DateTime.Today`/`DateTime.Now`
  are never used anywhere in this feature, in the View or the ViewModel.
- **`DisplayedMonth`** is the month currently shown in the grid, always normalized to that month's
  first day (`OperationsCalendarService.LoadAsync` re-derives `new DateOnly(displayedMonth.Year,
  displayedMonth.Month, 1)` regardless of what day-of-month a caller passes).
- **`SelectedDate`** is independent of `AssociateViewModel.SelectedDate` — the two are never
  read from or written to each other except through the one explicit "Open Passdown" action (see
  "Routing" below). Selecting a date in Calendar never touches Associate's own draft/edit state;
  switching Associate's date never touches Calendar's selection either.
- **In-month vs. outside-month cells**: the grid is always complete Sunday-through-Saturday weeks
  covering the displayed month — 4, 5, or 6 rows depending on the month (never a fixed 6-row grid
  padded with fabricated cells). A leading/trailing day that belongs to the adjacent month is
  flagged `IsInDisplayedMonth = false` but still fully real and selectable.
- `DateOnly` is used throughout for date identity (`OperationsCalendarSnapshot.DisplayedMonth`/
  `Today`/`SelectedDate`, `OperationsCalendarDay.Date`) — never a bare `DateTime`.

## Read model / service (`Carowinds.Passdown.Application/Calendar/`)

```
IOperationsCalendarService
    LoadAsync(DateOnly displayedMonth, DateOnly selectedDate) -> OperationsCalendarSnapshot

OperationsCalendarSnapshot
    DisplayedMonth, Today, SelectedDate
    Days: IReadOnlyList<OperationsCalendarDay>
    Selected: OperationsCalendarSelectedDateSummary
    GeneratedAt

OperationsCalendarDay
    Date, IsInDisplayedMonth, IsToday, HasFinalizedPassdown, HasAssociateDraft, RelevantOperationalNoteCount

OperationsCalendarSelectedDateSummary
    Date, IsToday, PassdownStatus (NoDraft | DraftInProgress | Finalized), LastSavedAt, FinalizedAt,
    RelevantOperationalNoteCount
```

`OperationsCalendarService` is built entirely as a projection over existing local state — the same
architectural shape `OperationsDashboardService` (Phase 10A) already established — never a second
business-rule implementation:

- **`IAssociatePassdownService.LoadAssociateDateAsync`** composes the selected date's own
  `AssociateWorkspace`, reused for its `SaveStatus` (never/saved) — the exact same local-only call
  Dashboard makes for "today," made here for whichever date is currently selected.
- **`IAssociateDraftRepository.ListSavedDraftDatesAsync`** — the same "a draft row was actually
  saved" signal `ScheduledPassdownFinalizer` (Phase 9B) already uses to decide finalization
  eligibility — drives the month grid's `HasAssociateDraft` flag without loading every date's full
  workspace.
- **`IFinalizedPassdownRepository.ListAsync`** — metadata only, **never** `GetByDateAsync`, so a
  Calendar load can never materialize a historical PDF blob (mirrors Dashboard's Phase 10A/10C
  guarantee, and is covered by a dedicated regression test:
  `OperationsCalendarServiceTests.LoadAsync_NeverCallsGetByDateOnFinalizedPassdownRepository`).
- **`IOperationalNotesService.ListAsync`** plus the existing, authoritative
  `OperationalNoteRelevance.IsRelevantForDate` predicate (Phase 11A) — applied in memory once per
  grid cell and once for the selected date, never a second relevance rule and never one
  `ListRelevantForDateAsync` call per day.

Exactly **three** repository/service calls happen per `LoadAsync`, regardless of how many days are
in the grid (finalized-metadata list, saved-draft-dates list, all-notes list) plus one composed
workspace load for the selected date — never an N+1 query per day cell.

No HTTP, Playwright, or Task Scheduler dependency is reachable from this type by construction
(`OperationsCalendarServiceTests.Constructor_HasNoRefreshOrchestratorOrRetrievalDependency`).

## Desktop layer

- **`OperationsCalendarViewModel`** (`Desktop/ViewModels/OperationsCalendarViewModel.cs`) depends
  only on `IOperationsCalendarService` and `IClock` — no repository, HTTP, or PDF dependency,
  mirroring `OperationsDashboardViewModel`'s own shape
  (`OperationsCalendarViewModelTests.Constructor_HasNoRefreshOrPdfDependency`). Owns
  `DisplayedMonth`/`SelectedDate`/`Snapshot`/`Days`/`IsLoading`/`ErrorMessage`, and the
  `PreviousMonthCommand`/`NextMonthCommand`/`GoToTodayCommand`/`SelectDateCommand`/
  `OpenAssociateCommand`/`OpenHistoryCommand`/`OpenOperationalNotesCommand` actions. A version
  counter guards every load (`LoadAsync`'s `_loadVersion`) so an overlapping month-change and
  date-selection (e.g. "Go to Today," which sets both properties) always resolves to a snapshot
  matching the *last* requested parameters, never a stale one — the same
  `_detailLoadVersion` pattern `PassdownHistoryViewModel` already established for its own
  selection-driven detail load.
- **Navigation is plain events**, never a direct `MainViewModel`/`AssociateViewModel` reference:
  `OpenAssociateRequested` (carries the selected `DateOnly`), `OpenHistoryRequested`,
  `OpenOperationalNotesRequested` (both generic, no payload). `MainViewModel` — the sole owner of
  top-level navigation — subscribes to all three in its constructor.
- **`OperationsCalendarDayViewModel`** (`Desktop/ViewModels/OperationsCalendarDayViewModel.cs`) is
  one small per-cell view model wrapping an `OperationsCalendarDay` plus a `SelectCommand` that
  calls back into the parent's `SelectedDate` setter — the same small-view-model-with-a-callback
  pattern `CustomProductRowViewModel` already establishes for `AssociateView`'s custom-product rows,
  rather than a complex relative binding from inside an `ItemsControl` `DataTemplate` back up to a
  parent command.
- **`OperationsCalendarPresentation`** (`Desktop/Presentation/OperationsCalendarPresentation.cs`)
  is the one place that turns snapshot data into display strings (month header, selected-date
  headline, passdown-status text, finalized-at text, notes-count sentence) — no formatting logic
  lives in the View or the ViewModel.

## UI layout

`OperationsCalendarView.axaml`: a header row (displayed month + Previous/Today/Next), a 7-column
month grid (`UniformGrid Columns="7"` inside an `ItemsControl`, with a weekday header row above it),
and a fixed-width selected-date summary panel with the three routing actions. Wrapped in a
`ScrollViewer` so a compressed 960×640 window degrades to scrolling rather than clipping, matching
every other Phase 9–11 screen's fallback. Visual language (the same `panel`/`chip`/`sectionTitle`/
`primaryAction`/`secondaryAction` style classes and `Passdown*Brush` resources) is unchanged from
`DashboardView`/`HistoryView` — no new brand elements were introduced.

Each day cell shows: the day number, a small "Finalized"/"Draft"/note-count text indicator set
(never color-only — today/selected/out-of-month are also each a distinct border/opacity treatment,
but every one of those states additionally has a plain-text equivalent in the cell's own automation
name), and is a plain `Button` so it is keyboard-reachable with FluentTheme's default focus ring.

## Routing / deep links

- **Open Passdown** (`OpenAssociateCommand`): raises `OpenAssociateRequested` with the Calendar's
  currently selected date. `MainViewModel` sets `AssociateViewModel.SelectedDate` to that date,
  sets `AssociateViewModel.SelectedSection = AssociateSection.Overview`, and switches to the
  Associate Passdown section — the one explicit case where Calendar *is* allowed to change
  Associate's selected date, since routing to a specific date is this action's entire purpose.
- **Open History** (`OpenHistoryCommand`): deliberately **generic navigation only** — it does not
  pre-select a matching date inside `PassdownHistoryViewModel`. This mirrors the exact choice
  already made and documented for Dashboard's own History integration (`docs/phase10-operations-dashboard.md`
  Phase 10C "Open History routing"): `PassdownHistoryViewModel.SelectedItem` is a
  `PassdownHistorySummaryPresentation`, not a `DateOnly`, and there is still no clean seam for one
  view model to hand another "please select this item" without either reaching into its internals
  or inventing a third coordination type solely for this one case. The command is only enabled
  (`CanOpenHistory`) once the selected date's own `PassdownStatus` is `Finalized` — "if finalized
  history exists," per the spec's own wording — rather than always being clickable.
- **Open Operational Notes** (`OpenOperationalNotesCommand`): generic navigation, mirroring
  Dashboard's own "Open Operational Notes" (Phase 11C) — never forces
  `OperationalNotesViewModel.LifecycleFilter`/`SelectedNote`, so a supervisor's existing state there
  survives the round trip untouched.

`MainViewModel` remains the single navigation owner; `OperationsCalendarViewModel` itself has zero
knowledge of `AppSection`, `AssociateSection`, or any other view model.

## What Phase 12A does *not* implement

- Rich park-hours/source-health overlays across the month grid (candidate for 12B/12C).
- Alerts or any data-confidence scoring.
- Dense multi-line note previews inside a day cell — a cell shows only a count.
- Editing controls of any kind inside the Calendar screen.
- A date pre-selection when routing into History (evaluated and deliberately deferred, matching
  Phase 10C's identical decision for Dashboard).
- Any schema change — no new table, no new persisted view state; the calendar recomputes its
  snapshot from existing local state on every load.
- The Manager workflow, app rename, refresh-policy changes, or anything else CLAUDE.md's current
  scope decision defers.

## Accessibility

`AutomationProperties.Name` values: "Operations Calendar section" (top-level nav button),
"Previous month", "Go to today", "Next month", "Weekday headers", "Calendar month grid", "Selected
date summary", "Open Passdown", "Open History", "Open Operational Notes", plus one composed name
per day cell (`OperationsCalendarDayViewModel.AutomationName`, e.g. `"August 21, 2026. Today.
Selected. Finalized. Draft in progress. 2 operational notes."`) built from the cell's own boolean
flags so every visual indicator has a textual equivalent, never color-only. Every control is a
plain `Button`, keyboard-reachable with FluentTheme's default focus ring.

## Visual validation — PARTIAL

Same constraint as every prior Phase 9–11 document: no pixel/bitmap screenshot renderer
(`Avalonia.Headless.Skia` or equivalent) is referenced by this codebase's test projects —
`Avalonia.Headless` is used only for real-XAML-loader construction checks. `OperationsCalendarView`
was reviewed directly against the 1280×860 layout target (a two-column `Grid` — month grid at `*`,
a fixed 320px summary panel — wrapped in a `ScrollViewer` for the 960×640 fallback) and exercised
through Avalonia's headless real-XAML-loader construction checks for every representative state
(a populated snapshot, a load failure, and dense per-day metadata). Recorded honestly as
**PARTIAL**, matching every prior phase's precedent — actual on-screen appearance should be
spot-checked in a Windows 11 VM per `CLAUDE.md`'s platform guidance before this ships.

## Tests added

- `Carowinds.Passdown.Application.Tests/Calendar/OperationsCalendarServiceTests.cs` (18 tests) —
  no-network-dependency, `DisplayedMonth` normalization, grid completeness/weekday alignment for
  several month shapes (a month starting on Saturday needing 6 weeks, one starting on Sunday
  needing exactly 4, a December→January year boundary), in-month vs. out-of-month classification,
  Eastern "today" resolution near a UTC day boundary, per-day finalized/draft/note-count flags,
  selected-date `PassdownStatus` classification (NoDraft/DraftInProgress/Finalized) including when
  the selected date falls outside the displayed month, the never-loads-a-PDF-blob guarantee, and an
  empty/many-records case.
- `Carowinds.Passdown.Ui.Tests/OperationsCalendarViewModelTests.cs` (13 tests) — Eastern
  today-on-construction (including the double-property-set-at-construction race this phase
  specifically found and fixed — see "Real bug found" below), month navigation preserving the
  selection, date selection preserving the displayed month, "Go to Today" resetting both, day-cell
  selection routing back into the parent, both routing events (including History's finalized-gated
  `CanExecute`), a load-failure degrading gracefully, the day-cell automation-name content, and the
  no-refresh/no-PDF/no-repository constructor-reflection guarantee.
- `Carowinds.Passdown.Ui.Tests/OperationsCalendarViewTests.cs` (4 tests) — real-XAML-loader
  construction for a populated snapshot, a load failure, dense per-day metadata, and the
  container-level automation-name assertions.
- `Carowinds.Passdown.Ui.Tests/MainViewModelNavigationTests.cs` — extended: Dashboard↔Calendar
  navigation, "Open Passdown" routing to Associate's Overview section with the Calendar's selected
  date, "Open Operational Notes" generic routing, state isolation between Calendar and Associate in
  both directions, Calendar's own displayed-month/selected-date surviving a navigate-away-and-back
  round trip, History/Operational-Notes-Manager remaining unaffected by visiting Calendar, and the
  dirty-Operational-Notes-editor guard extended to Calendar's own nav command. The pre-existing
  `AppSection_ContainsOperationalNotesAndNoManagerAlertsOrCalendar` test was renamed/updated to
  `AppSection_ContainsOperationsCalendarAndOperationalNotesAndNoManagerOrAlerts` and now asserts the
  five-value enum, since Calendar's absence was exactly what that test previously locked in.
- `Carowinds.Passdown.Ui.Tests/CompositionRootTests.cs` — added
  `BuildServiceProvider_ResolvesOperationsCalendarServices`.

## Real bug found and fixed during implementation

`OperationsCalendarViewModel`'s constructor originally set `DisplayedMonth` and then `SelectedDate`
as two separate property assignments. Because each is an `[ObservableProperty]` with its own
`OnXChanged` partial method that starts a reload, the first assignment (`DisplayedMonth`) fired a
load while `SelectedDate` still held `default(DateOnly)` (0001-01-01) — a real, observable bug
(`OperationsCalendarViewModelTests.Constructor_InitializesToEasternTodayAndLoads` caught it
immediately: three load calls instead of one, the first with a garbage date). Fixed with a
`_suppressAutoLoad` flag held during construction so both properties are set before the single,
correct initial load fires — the same reasoning `PassdownHistoryViewModel`'s own version-guarded
detail load already applies to a different race (a newer selection landing before an older one's
load finishes), applied here to two properties changing together at construction time instead.

## Schema / architecture

No migration was added. Database schema remains **10** (unchanged since Phase 11A); snapshot schema
remains **1**. `OperationsCalendarService` introduces no new persistence — it is a pure read
projection over the same tables `AssociatePassdownService`/`IFinalizedPassdownRepository`/
`IOperationalNotesService` already own.

## Full validation

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **1286/1286** (up from the
1242 baseline by the 44 tests listed above: 18 Application + 13 + 4 + 1 CompositionRootTests + 8
MainViewModelNavigationTests additions), run twice to confirm no flakiness. No live internet was
used or required.

## Deferred to 12B+

- Richer park-hours/source-health overlays across the month.
- Any denser per-day note preview beyond a count.
- A History date pre-selection seam, if a future phase decides the cross-view-model coupling is
  worth it.
- The renamed-app work, refresh-policy overhaul, manual-hours/buyout redesign, ERT simplification,
  logo, Alerts, and Manager workflow — all still deferred per `CLAUDE.md`.

12B has not started.
