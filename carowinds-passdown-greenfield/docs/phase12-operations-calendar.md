# Phase 12 — Operations Calendar

Phase 12A built the Operations Calendar's **foundation**: a top-level month-grid navigation
surface, explicit Eastern-aware date semantics, a local-only Application-layer read model/service,
and clean routing into the existing Associate Passdown/History/Operational Notes screens. Phase 12B
(see its own section below) adds the **Park Hours and Operating-State overlay** layer on top of
that foundation — every day cell and the selected-date panel now answer "is the park operating on
this date, and what are the effective hours?" It is still not a full calendar experience — richer
source-health overlays, alerts, and any Manager-workflow integration remain out of scope (CLAUDE.md
"Current scope decision"; see each section's own "What this phase does *not* implement").

## Phase 12A — foundation

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

## What Phase 12A does *not* implement (superseded in part by 12B — see below)

- Park-hours/operating-state overlays across the month — **now implemented, Phase 12B**.
- Any denser per-day note preview beyond a count — still deferred.
- A History date pre-selection seam, if a future phase decides the cross-view-model coupling is
  worth it — still deferred.
- The renamed-app work, refresh-policy overhaul, manual-hours/buyout redesign, ERT simplification,
  logo, Alerts, source-health overlays beyond park hours, and Manager workflow — all still deferred
  per `CLAUDE.md`.

---

## Phase 12B — Park Hours and Operating-State overlays

Phase 12B makes each calendar date operationally useful by showing compact park-hours/operating-
state information directly in the month grid and the selected-date detail panel — the layer 12A
deliberately deferred. It answers exactly one new question: **"is the park operating on this date,
and what are the effective hours?"** No Passdown/Operational Notes overlay beyond what 12A already
built, no Alerts, no Manager workflow, and no schema change.

### Operating-state purpose and the reuse rule (Part A)

The single hard rule this phase is built around: **no new hours engine.** Every effective-hours
value the Calendar ever shows is produced by the exact same chain Associate Overview, the
Dashboard, and PDF generation already use:

```
official/persisted hours
    |
    v
manual-hours override (saved Associate draft)
    |
    v
private-buyout override (saved Associate draft)
    |
    v
Domain.Hours.ParkHoursCalculator.Calculate  (spec §8.2 — the one precedence implementation)
    |
    v
Application.Associate.AssociateParkHoursView
    |
    v
presentation (ParkHoursPresentation / OperationsCalendarOperatingStatePresentation)
```

`AssociatePassdownService`'s own park-hours composition (previously a private method,
`ComposeParkHours`) and its draft-deserialization logic (previously private `DeserializeDraft` plus
its legacy-migration helpers) were **extracted, not duplicated**, into two new public static
Application-layer types so `OperationsCalendarService` could call the exact same code:

- `Associate.AssociateParkHoursComposer.Compose(DatedResultRecord? record, AssociateDraftPayload draft, DateTimeOffset asOf) -> AssociateParkHoursView`
- `Associate.AssociateDraftPayloadSerializer.Deserialize(string payloadJson) -> AssociateDraftPayload`

`AssociatePassdownService.ComposeAsync` now calls `AssociateParkHoursComposer.Compose` directly
(previously its own private method's body); `OperationsCalendarService` calls the identical method
for every date in the visible grid. This is a structural guarantee, not a convention someone could
drift from later — there is only one park-hours calculation in the codebase to call.

`OperationsCalendarServiceTests.LoadAsync_AgreesWithDashboardParkStatus_ForIdenticalUnderlyingState`
additionally proves parity end to end: seeding identical local state and loading it through both
`OperationsDashboardService` and `OperationsCalendarService` produces the same effective
opening/closing times and equivalent status classifications.

### `OperationsCalendarOperatingState` (Part B)

A new enum in `Application/Calendar/OperationsCalendarSnapshot.cs`, structurally identical to
`Dashboard.DashboardParkStatus` (Phase 10A) — `NotChecked | Closed | Open | CustomHours |
PrivateBuyout | NotPublished | Unavailable`:

```csharp
private static OperationsCalendarOperatingState ClassifyOperatingState(AssociateParkHoursView hours) =>
    hours.Effective.Status switch
    {
        EffectiveHoursStatus.Open when hours.Effective.IsPrivateBuyoutApplied => PrivateBuyout,
        EffectiveHoursStatus.Open when hours.Effective.IsManualOverrideApplied => CustomHours,
        EffectiveHoursStatus.Open => Open,
        EffectiveHoursStatus.Closed => Closed,
        _ => hours.Verification.State switch
        {
            NotPublished => NotPublished,
            Unavailable or Error => Unavailable,
            _ => NotChecked,
        },
    };
```

Deliberately **not** the literal same type as `DashboardParkStatus` — Calendar and Dashboard are
peer top-level features, and Phase 12A already established the precedent
(`OperationsCalendarPassdownStatus`, kept independent of `DashboardPassdownStatus` for the same
reason) of each screen owning its own presentation-adjacent vocabulary over the same shared domain
values, the same way `Desktop.Presentation.ParkHoursBadgeKind` already does for Associate Overview.
The classification *logic* is what's shared in spirit (it's the same seven-line switch as
Dashboard's `BuildParkStatus`); the *type* is intentionally not, to avoid a Calendar→Dashboard
namespace dependency between peer features.

`OperationsCalendarDay` and `OperationsCalendarSelectedDateSummary` each gained two fields:
`OperatingState` (the classification above) and `ParkHours` (the full `AssociateParkHoursView` —
carried whole, not just its formatted text, specifically so the Desktop presentation layer can
reuse `ParkHoursPresentation.Build` directly instead of a second formatter).

### Day-cell presentation (Parts C/D/E/F/G/H/I/U)

`Desktop.Presentation.OperationsCalendarOperatingStatePresentation.Build(state, parkHours)` is the
one new presentation helper (spec Part U: it formats only — no `EffectiveHours` calculation, no
service call, no freshness decision of its own). It produces, per state:

| State | Compact label | Compact hours line | Example |
|---|---|---|---|
| Open | *(none — hours alone communicate it)* | `10 AM–10 PM` | `26 / 10 AM–10 PM` |
| Closed | `CLOSED` | *(none)* | `27 / CLOSED` |
| Custom Hours | `CUSTOM` | `9 AM–11 PM` | `28 / CUSTOM / 9 AM–11 PM` |
| Private Buyout | `BUYOUT` | `6 PM–11 PM` | `29 / BUYOUT / 6 PM–11 PM` |
| Not Published | `NOT PUBLISHED` | *(none)* | `30 / NOT PUBLISHED` |
| Unavailable | `UNAVAILABLE` | *(none)* | — |
| Not checked yet | *(none)* | *(none)* | — |

Compact time formatting omits `:00` when a time falls on the hour (`10 AM`) and keeps minutes
otherwise (`9:30 AM`) — deliberately terser than `ParkHoursPresentation`'s own full "10:00 AM –
10:00 PM" headline, which is sized for a full-width panel, not a ~72px calendar cell.
`OperationsCalendarDayViewModel` exposes `OperatingStateLabel`/`EffectiveHoursLine` as two separate
bindable strings so the view renders at most two extra lines per cell, on top of the day number and
12A's existing Finalized/Draft/note-count indicators — never a third formatter, never a giant badge,
never source-status detail (Part I: `Unavailable` never surfaces the underlying exception, HTTP
status, or endpoint).

### Closed (Part E)

Closed shows the text `CLOSED` and nothing else — never `12 AM–12 AM`, never a blank line, and
never red/error styling. `ParkHoursCalculator.Calculate` already never produces an
`Opening`/`Closing` time for `EffectiveHoursStatus.Closed` (both stay `null`), so there is nothing
to accidentally format into fabricated hours in the first place.

### Custom Hours (Part F)

The `CUSTOM` label plus the overridden hours — sourced from `AssociateDraftPayload.ManualHoursOverride`
via the shared composer, never recalculated in Calendar. Covered end to end by
`OperationsCalendarServiceTests.LoadAsync_SavedDraftHasManualOverride_ClassifiesCustomHoursOverridingOfficial`
(official hours present, draft override still wins) and the presentation-level
`OperationsCalendarOperatingStatePresentationTests.Build_CustomHours_...`.

### Private Buyout (Part G)

The `BUYOUT` label plus the buyout hours — sourced from `AssociateDraftPayload.PrivateBuyout`.
Never presented as Closed or an error, even when the official date is closed (buyout is spec
§8.2's named exception to "closed," and `ParkHoursCalculator` already encodes that precedence
first). Covered by
`OperationsCalendarServiceTests.LoadAsync_SavedDraftHasPrivateBuyout_ClassifiesPrivateBuyoutEvenWhenOfficiallyClosed`.
The clunky manual-hours/buyout time-entry UI itself is unchanged — still Phase 13's concern, not
touched here.

### Not Published / Unavailable (Parts H/I)

Both are informational, never error states: `NotPublished` means the source was successfully
checked and confirmed nothing is published yet (`RetrievalStatus.NotPublished`, a "successful
completion" per the Domain's own `IsSuccessfulCompletion()`); `Unavailable` covers both
`RetrievalStatus.Unavailable` (checked, explicitly unavailable) and `RetrievalStatus.Error`
(couldn't be reached at all) — collapsed to one Calendar-facing label, matching Dashboard's own
`DashboardParkStatus.Unavailable` precedent. Neither ever shows a raw exception message, HTTP
status, or endpoint URL — the presentation type has no field capable of carrying one.

### Stale/last-known-good (Part J) — shown, not hidden; no second freshness system

Calendar does **not** hide old hours when a later recheck fails — the app-wide last-known-good
guarantee (spec §6.7) already means `ParkHoursCalculator` still receives the last successful
official opening/closing even after a later attempt errors, so the effective hours themselves are
unaffected either way. What 12B *does* add is a compact **"Stale"** indicator: since
`OperationsCalendarDay`/`OperationsCalendarSelectedDateSummary` already carry the full
`AssociateParkHoursView` (including its `Verification.State`), `IsHoursStale` is computed by
checking whether `ParkHoursPresentation.Build(parkHours).StaleNoteText` is non-null — the exact
same staleness signal Associate Overview's own screen already surfaces, reused verbatim rather than
a second freshness computation. This was cleanly supportable (not deferred) precisely because 12B's
design already threads the full `AssociateParkHoursView` through to the day cell for the
`ParkHoursPresentation`-reuse reason above — the information was already there.

### Selected-date detail panel (Part K)

`Desktop.Presentation.OperationsCalendarPresentation` gained four fields — `ParkStatusText`,
`EffectiveHoursText`, `ParkHoursVerificationText`, `ParkHoursStaleNoteText` — built by calling
`ParkHoursPresentation.Build(selected.ParkHours)` once and reusing its `HeadlineText`/
`VerificationText`/`StaleNoteText` directly (never a second hours formatter). The panel now reads:

```
Selected Date
Saturday, August 29, 2026

Park Status
Open
Effective Hours
10:00 AM – 10:00 PM

Passdown Status
...
```

`EffectiveHoursStatus` is `Open` for all three of Open/Custom/Buyout (they differ only by which
override applied, per spec §8.2), so `ParkHoursPresentation`'s own headline is already correct for
every one of them without a second branch. `EffectiveHoursText` is `null` (and the row hidden) for
Closed/NotPublished/Unavailable/NotChecked. This stays compact and deliberately does not duplicate
the Dashboard's own richer Data Status card (spec Part K's own instruction) — only Park
Status/Effective Hours/verification/staleness, nothing else.

### Off-month cells (Part L) — chosen: day number only, no hours overlay

A leading/trailing day belonging to the adjacent month still carries a real, correctly-composed
`OperatingState`/`ParkHours` in the snapshot (Calendar never fabricates a cell), but the *view*
hides the operating-state/hours/stale block for any cell where `IsInDisplayedMonth` is false
(`OperationsCalendarView.axaml`'s day-cell `DataTemplate` wraps that block in its own
`IsVisible="{Binding IsInDisplayedMonth}"`), leaving only the (already-dimmed, via the existing
`outOfMonth` opacity style) day number. This was the explicitly preferred option (spec Part L) —
kept simple to avoid clutter/confusion at a month boundary, rather than a second, visually-subdued
hours treatment.

### Today/Selected coexistence (Part M)

No color was added per operating state (see "Style/visual language" below), so there is nothing to
collide with the existing `.today` (accent border) / `.selected` (band background) treatments —
they remain the only two colored/bordered states a day cell can have. Both flags, plus the
operating-state/hours text, coexist in the automation name unchanged
(`OperationsCalendarDayViewModel_TodayAndSelectedTogether_...` proves this directly).

### Month-load performance and query strategy (Parts N/O/P)

Exactly two new repository methods were added, each scoped to the visible grid range only — never
one call per day cell:

- `IDatedResultRepository.GetByCategoryAndDateRangeAsync(SourceCategory, DateOnly start, DateOnly endInclusive)`
  — one `SELECT ... WHERE category = ? AND requested_date BETWEEN ? AND ?` (SQLite; the existing
  `ix_dated_results_requested_date` index covers it).
- `IAssociateDraftRepository.GetByDateRangeAsync(DateOnly start, DateOnly endInclusive)` — one
  `SELECT ... WHERE operating_date BETWEEN ? AND ?` (the table's own primary key already covers it).

`OperationsCalendarService.ComposeParkHoursForRangeAsync` calls each exactly once per `LoadAsync`,
covering the **exact grid range already computed for the month view** (spec Part O — "Aug 1–31 plus
visible late-July/early-September cells," never a separately-chosen or larger range), then composes
every day's `AssociateParkHoursView` in memory. Combined with 12A's existing three calls
(finalized-metadata list, saved-draft-dates list, all-notes list) and the one per-load selected-date
workspace composition, a full month load is a **fixed, small number of local SQLite round trips
regardless of grid size (35 vs. 42 cells costs the same)** — proven directly by
`OperationsCalendarServiceTests.LoadAsync_QueriesDatedResultsAndDraftsExactlyOncePerMonthLoad`
(counting-wrapper repositories assert exactly one range call each for a 42-cell August grid).
Nothing here calls HTTP, `IRefreshOrchestrator`, `CheckAssociateDateOnlineAsync`, or any Playwright/
browser session — `Constructor_HasNoRefreshOrchestratorOrRetrievalDependency` (unchanged from 12A)
still holds with the two new constructor dependencies added.

### Saved-override resolution / no-draft behavior (Parts Q/R)

A saved Associate draft's `ManualHoursOverride`/`PrivateBuyout` is resolved via the same
`AssociateDraftPayloadSerializer.Deserialize` `AssociatePassdownService` itself uses — never a
second, drifting deserialization path, and never a new persisted "calendar override" concept (spec
Part Q: "do not create duplicate override storage"). A date with **no** saved draft at all still
shows official hours cleanly (`AssociateDraftPayload.CreateDefault()`'s disabled overrides simply
fall through `ParkHoursCalculator` to the official values) — a draft was never required to see park
hours, matching Part R exactly.

### Finalized historical-date hours — chosen: live/current, not frozen (Part S)

**Decision: Calendar shows the live, current composed hours for every date, including an already-
finalized one — never the frozen `FinalizedPassdownSnapshot` for that date.** This was a deliberate
choice, not an oversight, for two reasons:

1. **Architecture**: `FinalizedPassdownSnapshot`'s park-hours field
   (`FinalizedParkHoursSnapshot`) is *pre-formatted display strings* (`HeadlineText`/`BadgeText`),
   not the raw `TimeOnly` values `AssociateParkHoursView`/`ParkHoursCalculator` work with. Mixing
   the two shapes in one `OperationsCalendarDay.ParkHours` field would mean either inventing a
   parser to reconstruct `TimeOnly` values out of already-formatted text (fragile, and a second
   hours-representation entirely) or branching the day cell's rendering into two genuinely
   different code paths depending on `HasFinalizedPassdown`.
2. **Query cost**: reading a finalized date's frozen hours would require an extra
   `IFinalizedPassdownRepository.GetByDateAsync` (or a new metadata-light hours-only query) per
   finalized date visible in the grid — a query 12A/12B both deliberately avoid everywhere else,
   specifically to keep the Dashboard/Calendar summary surfaces from ever materializing a historical
   snapshot/PDF blob.

This is a known, honestly-documented limitation: if official hours are corrected *after* a date is
finalized, Calendar's day cell for that date will show the corrected (live) hours, which can differ
from what the frozen History record actually shows for that date. Proven concretely by
`OperationsCalendarServiceTests.LoadAsync_FinalizedDate_ShowsCurrentLiveHours_NotFrozenAtFinalizationTime`
(finalizes a date, then changes its official hours, then asserts the Calendar cell reflects the
*new* hours). A future phase can revisit this — e.g. a dedicated lightweight
"frozen-hours-summary-only" read model — but that is new architecture, not a 12B-scope fix.

### Future dates (Part T)

No different code path exists for a future date — the same `ComposeParkHoursForRangeAsync`/
`AssociateParkHoursComposer.Compose` call handles it. A future published date shows its hours; a
future date that's been checked but has nothing published yet shows `NotPublished`; nothing implies
a shown future hours value is guaranteed final. No network call is possible from month navigation
regardless of how far into the future the displayed month is.

### Style / visual language (Part V)

Deliberately **no color-per-operating-state** was added (explicit spec instruction: "avoid one
color per operating state... red for Closed... calendar cells that look like web cards"). The
compact text labels (`CLOSED`/`CUSTOM`/`BUYOUT`/`NOT PUBLISHED`/`UNAVAILABLE`) are the entire state
signal — rendered in the same restrained muted/bold-small-caps treatment regardless of which state,
matching the app's existing chip/status vocabulary. The one exception is the "Stale" indicator,
which reuses the app's pre-existing amber `PassdownWarningBrush` (the same "warning, not error"
convention `AssociateView`/`HistoryView` already use elsewhere) — never red. Today/Selected keep
their unchanged 12A border/band treatment, so the two concerns never visually compete.

### Accessibility (Part X)

Every day cell's `AutomationName` now includes a full operating-state clause, e.g. `"August 21,
2026. Open, 10 AM to 10 PM. Today. Selected. Finalized. Draft in progress. 2 operational notes."`
— built from `OperationsCalendarOperatingStatePresentation.AccessibilityText` (`"Open, 10 AM to 10
PM"` / `"Closed"` / `"Custom Hours, 9 AM to 11 PM"` / `"Private Buyout, 6 PM to 11 PM"` / `"Hours
not published"` / `"Hours unavailable"`, with `". Stale — showing last known information."`
appended when stale). The 12A clauses (Today/Selected/Finalized/Draft in progress/note count) are
unchanged and still present as exact substrings, so every pre-existing 12A automation-name assertion
keeps passing unmodified. The selected-date panel's new "Park Status" text carries its own
composed automation name (`"Park status: {status}"`).

### Keyboard behavior (Part Y)

No custom key handling was added or needed. Previous/Next/Today/day cells/Open Passdown are all
plain `Button`s (unchanged from 12A), so FluentTheme's default Tab-order and focus ring already
make every one of them keyboard-reachable; day cells were already keyboard-selectable via their own
`SelectCommand` bound to `Button.Command`, which Enter/Space already activate through Avalonia's
standard `Button` key bindings — Phase 12B added no new interactive control shape.

### Local-only guarantee (Part P) — unchanged, reinforced

`OperationsCalendarService`'s constructor gained two new parameters
(`IDatedResultRepository`/keeping `IAssociateDraftRepository`), neither of which is capable of any
network access — both are pure local-SQLite abstractions, exactly like every other dependency this
type already had. `Constructor_HasNoRefreshOrchestratorOrRetrievalDependency` (12A, unchanged)
still passes with the new constructor shape.

### Schema (Part AB)

No migration was added. Database schema remains **10**; `AssociateDraftPayload` schema remains
**3**; `FinalizedPassdownSnapshot` schema remains **1**. Every new field 12B added
(`OperationsCalendarDay.OperatingState`/`ParkHours`,
`OperationsCalendarSelectedDateSummary.OperatingState`/`ParkHours`) is computed fresh on every
`LoadAsync` from existing tables — nothing new is persisted.

### Visual validation (Part AA) — PARTIAL

No pixel/bitmap screenshot renderer (`Avalonia.Headless.Skia` or equivalent) is referenced by this
codebase's test projects, same constraint as every prior phase. What *was* added beyond 12A's own
plain construction checks: a real `Measure`/`Arrange` layout pass at both `1280×860` and `960×640`
(`OperationsCalendarView_LayoutAt1280x860_DoesNotThrow` /
`..._LayoutAt960x640_DoesNotThrow`), which succeeded without exception at both sizes — genuine
layout-level evidence, not just DataContext assignment — plus construction checks for a month with
every operating state mixed across different days
(`OperationsCalendarView_MixedOperatingStatesAcrossTheMonth_ConstructsWithoutError`). Recorded
honestly as **PARTIAL** — no cell-legibility/clipping/contrast claim is made beyond what
Measure/Arrange proves (no exception, non-zero final bounds); actual on-screen appearance should be
spot-checked in a Windows 11 VM per `CLAUDE.md`'s platform guidance before this ships.

### Real bug avoided by construction, not found

Unlike 12A (which found and fixed a real double-load race), 12B's implementation surfaced no new
defect — the extraction of `AssociateParkHoursComposer`/`AssociateDraftPayloadSerializer` was a pure
refactor verified test-green both immediately after extraction (before any Calendar-specific code
was added) and at the end, and the batch range-query design was correct on first test run (18/18
pre-existing Calendar tests passed unchanged before any new 12B test was added).

### Tests added (Phase 12B)

- `OperationsCalendarServiceTests.cs` — **+14** (32 total): normal published hours, officially
  closed, saved-draft manual override overriding official, saved-draft buyout overriding an
  officially-closed date, Not Published, Unavailable (via a genuine failed attempt with no prior
  success), no-draft-with-official-hours, no-draft-and-no-official-data (NotChecked), a future
  published date, a future NotPublished date, a finalized date showing live (not frozen) hours after
  a post-finalization official-hours change, exactly-one-range-query-per-collaborator-per-load, and
  a 2-case theory proving Calendar/Dashboard classification parity for identical underlying state.
- `OperationsCalendarViewModelTests.cs` — **+7**: accessibility text for Open/Closed/Custom
  Hours/Private Buyout/Not Published/Unavailable, and Today+Selected coexisting with operating-state
  content; the pre-existing automation-name test was extended (not replaced) to also assert the new
  hours clause.
- `OperationsCalendarOperatingStatePresentationTests.cs` (new, 7 tests) — the compact day-cell
  vocabulary directly: Open/Closed/Custom/Buyout/NotPublished/Unavailable/NotChecked, plus
  on-the-hour vs. non-hour compact time formatting.
- `OperationsCalendarPresentationTests.cs` (new, 4 tests) — the selected-date panel's Park
  Status/Effective Hours text for Open/Custom/Buyout/Closed, proving reuse of
  `ParkHoursPresentation.Build`.
- `OperationsCalendarViewTests.cs` — **+4**: a mixed-operating-state month, layout at 1280×860,
  layout at 960×640, and the selected-date panel's Park Status/Effective Hours binding.

**37 new tests total** (1286 Phase 12A baseline + 37 = 1323).

### Full validation (Phase 12B)

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **1323/1323**. Run five times
total under normal (parallel) execution; two of those five runs hit the pre-existing, previously-
documented (Phase 6D) Avalonia headless cross-assembly parallelism flake
(`Avalonia.Controls.Panel.ChildrenChanged`/`AvaloniaList.NotifyAdd` during `XamlIlPopulate`) on
*unrelated* Phase 4/11 views (`AssociateView`, `OperationalNotesView` — different tests each time,
never the same one twice, and never a Phase 12 file). The other three runs — including the final
one — passed all 1323 cleanly. No Operations Calendar test failed in any run. No live internet was
used or required.

### What Phase 12B does *not* implement

- Source-health/data-confidence overlays beyond park hours (e.g. product/pricing source status) —
  still out of scope, matching CLAUDE.md's explicit instruction for this phase.
- Any Passdown/Operational Notes overlay beyond what 12A already built.
- Alerts, the Manager workflow, the app rename, refresh-policy changes, manual-hours/buyout UI
  redesign, or ERT behavior changes — all explicitly out of scope for 12B.
- A frozen-hours read model for finalized historical dates (Part S) — documented and deferred, not
  silently mixed with live data.
- The release ZIP rebuild — Phase 12F's scope.

## Deferred to 12C–12F

- Richer source-health overlays beyond park hours.
- Any denser per-day note preview beyond a count (still 12A's own deferred item).
- A History date pre-selection seam.
- A frozen-hours read model for finalized dates, if a future phase decides it's worth the added
  query cost/architecture.
- The renamed-app work, refresh-policy overhaul, manual-hours/buyout redesign, ERT simplification,
  logo, Alerts, and Manager workflow — all still deferred per `CLAUDE.md`.

12C has not started.
