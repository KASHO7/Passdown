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

## Deferred to 12D–12F (superseded in part by 12C — see below)

- Richer source-health overlays beyond park hours.
- Any denser per-day note preview beyond a count — **still deferred, now explicit** (12C Part U):
  Calendar shows counts only, never note titles/bodies.
- A History date pre-selection seam — **re-evaluated in 12C, still deferred** (see below).
- A frozen-hours read model for finalized dates, if a future phase decides it's worth the added
  query cost/architecture.
- The renamed-app work, refresh-policy overhaul, manual-hours/buyout redesign, ERT simplification,
  logo, Alerts, and Manager workflow — all still deferred per `CLAUDE.md`.

---

## Phase 12C — Passdown Status + Persistent Operational Notes overlays

Phase 12C makes the Calendar useful for passdown *planning*, not just park-hours awareness: every day
cell and the selected-date panel now also answer **"does this date have a passdown, and are there
operational notes I should know about?"** No Alerts, no Manager workflow, no schema change, and the
Calendar remains a summary/routing surface — day cells gained two more short lines, not a second
Dashboard.

### Passdown status model and Finalized-over-Draft precedence (Parts A/B/C/D)

12A already computed a `PassdownStatus` (`NoDraft | DraftInProgress | Finalized`) for the
*selected-date* summary, with Finalized already taking precedence there (`OperationsCalendarService`'s
`BuildSelectedSummaryAsync` checks `finalized is not null` before falling back to the workspace's save
state). What 12A/12B never did is apply that same precedence to the **day-cell grid**, which instead
exposed two independent booleans (`HasFinalizedPassdown`, `HasAssociateDraft`) straight to the View —
and the View rendered *both* "Finalized" and "Draft" text simultaneously whenever a date had both a
finalized record and a still-existing saved draft row (a real, if latent, spec violation this phase
fixes, not a new feature).

The fix is one new presentation type, `Desktop.Presentation.OperationsCalendarPassdownPresentation`,
which owns the precedence rule directly:

```csharp
public static OperationsCalendarPassdownStatus DeriveStatus(bool hasFinalizedPassdown, bool hasAssociateDraft) =>
    hasFinalizedPassdown ? Finalized
    : hasAssociateDraft ? DraftInProgress
    : NoDraft;
```

`OperationsCalendarDayViewModel` calls `DeriveStatus` from the day's own two flags (spec Part A: "use
existing persisted state," never a second draft-lifecycle system) and then `Build(status)` for display
text; the selected-date summary passes its already-computed `OperationsCalendarPassdownStatus`
straight to the same `Build` overload — one formatter, two callers, never two precedence
implementations. The underlying `AssociateDraft` row is never deleted or otherwise touched by this
precedence decision (Part B: "the underlying editable draft may still exist... Calendar status should
represent operational lifecycle") —
`OperationsCalendarServiceTests.LoadAsync_DateHasBothSavedDraftAndFinalizedRecord_DayCellPassdownFlagsFavorFinalized`
proves the raw `HasAssociateDraft` flag stays `true` even though the *presented* status is Finalized.

Day-cell label (shortest form, Part D): **"No Draft"** / **"Draft"** / **"Finalized"**. Selected-date
detail label (fuller form, matching `Desktop.Presentation.OperationsDashboardPresentation`'s own
"No Draft"/"Draft In Progress"/"Finalized" wording — spec Part D "use consistent wording with existing
Dashboard where possible"): **"No Draft"** / **"Draft In Progress"** / **"Finalized"**, each with an
optional detail note (`DetailNoteText`) — `"No passdown started."` for No Draft (Part AD), `"Last saved
{timestamp}"` for Draft In Progress when a save timestamp exists, and `"Finalized {timestamp}"` for
Finalized (falling back to the Part AD empty-state sentence `"Finalized passdown available in
History."` on the rare case no timestamp is available). No Draft is deliberately neutral — no
error/warning style, since a future date with no draft yet is normal (Part C/W).

### Persistent Operational Notes counts and the Important sub-count (Parts E/F/O)

`OperationsCalendarDay`/`OperationsCalendarSelectedDateSummary` each gained one new field,
`ImportantOperationalNoteCount` (appended with a `= 0` default so every pre-12C construction call site
keeps compiling unchanged — no test or production call site needed updating just for the new field's
existence). It is computed in `OperationsCalendarService` as a further in-memory filter over the exact
same `allNotes` list `RelevantOperationalNoteCount` already filters — `note.Priority ==
OperationalNotePriority.Important && OperationalNoteRelevance.IsRelevantForDate(note, date)` — reusing
11A's one authoritative relevance rule, never a second/duplicated relevance condition, and adding
**zero** new `IOperationalNotesService` queries (Part O): the same single `ListAsync()` call 12A/12B
already made once per `LoadAsync` now backs both counts.
`OperationsCalendarServiceTests.LoadAsync_ComputingImportantCounts_DoesNotAddAnOperationalNotesQuery`
and `..._HundredPlusOperationalNotes_LoadsCorrectlyAndBounded` prove this directly with a
call-counting wrapper and a 120-note data set respectively.

`Desktop.Presentation.OperationsCalendarNotesPresentation` is the one new formatter for both counts:

| Relevant | Important | Cell text | Detail count | Detail Important |
|---|---|---|---|---|
| 0 | 0 | *(hidden)* | "No active operational notes for this date." | *(hidden)* |
| 1 | 0 | "1 Note" | "1 active" | *(hidden)* |
| 3 | 0 | "3 Notes" | "3 active" | *(hidden)* |
| 3 | 1 | "3 Notes · 1 Important" | "3 active" | "1 Important" |

A zero Important count never shows a meaningless "0 Important" line (Part F); Important is
communicated as plain text only, never a red/alarm-style badge (Part F/AA) — the same "text emphasis,
never color-as-severity" convention 11B/11C already established for persistent-note priority
elsewhere in the app.

### Day-cell content and priority (Parts G/H/I)

Each in-month day cell now renders, top to bottom: day number, operating-state/hours (12B, unchanged
priority), passdown status label, notes summary line (hidden when there are no relevant notes) — the
exact density the spec's own target concept describes (`29 / 10 AM–10 PM / Draft / 2 Notes · 1
Important`), never note titles, full passdown detail, or a second badge per state. The passdown-status
line is *always* shown (including "No Draft," per Part C/W: "future dates may show: No Draft... this is
useful planning information") — it is the one line 12C makes unconditional, unlike 12A/12B's
conditionally-hidden Finalized/Draft indicators.

All four overlay lines (operating-state label, hours, stale flag, passdown status, notes summary) now
live inside **one** `IsVisible="{Binding IsInDisplayedMonth}"` wrapper in `OperationsCalendarView.axaml`
— previously (12A/12B) the Finalized/Draft/note-count `TextBlock`s sat *outside* that wrapper, so an
off-month cell with real underlying draft/finalized/note data would have visibly leaked it (Part I: "do
not add passdown status or note counts... to off-month cells" — a real, if minor, pre-12C inconsistency
this phase also fixes). The Application-layer snapshot itself still never fabricates or suppresses an
off-month cell's real data (`LoadAsync_OffMonthCell_StillCarriesRealPassdownAndNoteData` proves the
snapshot still carries it); only the View now correctly hides it for off-month cells, matching the
same "day number only" decision 12A/12B already made for the operating-state overlay.

### Selected-date summary (Parts J/K/T/AD)

The panel gained a new **"Active Operational Notes"** section (Part T: labeled explicitly to avoid any
reader confusion with the daily Associate draft's own Notes entry, which remains a completely separate
concept and table), and the existing "Passdown Status" section now shows a detail note line beneath the
status label:

```
PARK
Open
10 AM – 10 PM

PASSDOWN STATUS
Draft In Progress
Last saved Aug 21, 2026 at 2:00 PM

ACTIVE OPERATIONAL NOTES
3 active
1 Important

[ Open Passdown ]  [ Open History ]  [ Open Operational Notes ]
```

`OperationsCalendarPresentation`'s fields were renamed/reshaped to carry this: `FinalizedText` became
the more general `PassdownDetailNoteText` (now populated for all three statuses, not only Finalized),
`OperationalNotesCountText` changed from a full sentence ("3 operational notes for this date.") to the
compact "N active"/empty-state form, and a new `OperationalNotesImportantText` field carries the
optional Important line. Both `PassdownStatusText`/`OperationalNotesCountText` bindings gained their
own composed `AutomationProperties.Name` ("Passdown status: {0}" / "Active operational notes: {0}") per
spec Part AB.

### Actions stay unchanged (Parts K/L/M/N) — already correct from 12A/12B

Re-reading 12A/12B's own implementation against 12C's spec confirmed every action-routing requirement
was already met, so **no code changed here**:

- **Open Passdown** already routes to Associate → Overview with the Calendar's selected date
  (`MainViewModel`'s `Calendar.OpenAssociateRequested` handler) — Part L's exact preferred behavior.
- **Open History** is already gated (`CanOpenHistory`) on `Selected.PassdownStatus == Finalized` and
  stays deliberately generic navigation (Part M) — re-evaluated for 12C and still correct: no clean
  seam exists to preselect a date inside `PassdownHistoryViewModel` (`SelectedItem` is typed as a
  `PassdownHistorySummaryPresentation`, not a `DateOnly`; the same architectural gap 12A/10C already
  documented). Forcing it from `MainViewModel` would mean reaching into `PassdownHistoryViewModel`'s own
  loaded-item list and racing its own load — exactly the "brittle coupling" Part M says to avoid. Still
  deferred to a future phase, unchanged from 12A/12B's own conclusion.
- **Open Operational Notes** remains generic navigation (Part N) — no filter/date state is forced.
- No "Include note in passdown" action exists anywhere on this screen (Part K) — persistent-note
  inclusion remains exclusively Associate → Notes, unchanged since 11D.

### Query strategy — zero new queries (Parts O/P/Q)

12C adds no new repository or service call. `ImportantOperationalNoteCount` is computed entirely from
the `allNotes` list 12A/12B already load once per `LoadAsync` (see above); the Finalized/Draft
precedence is computed entirely from the two flags 12A/12B already compute per day cell. The existing
bounded-query guarantees (`LoadAsync_QueriesDatedResultsAndDraftsExactlyOncePerMonthLoad`, the
finalized-metadata-only `ListAsync` — never `GetByDateAsync`) are unchanged and still hold; 12C's own
`LoadAsync_ComputingImportantCounts_DoesNotAddAnOperationalNotesQuery` extends that guarantee to cover
the new count. No PDF bytes, snapshot JSON, or History detail are read anywhere in this phase (Part R) —
Finalized status is still metadata-only, exactly as 12A established.

### Local-only guarantee and reflection behavior (Parts AE/AF/AG/AH) — unchanged

Nothing in 12C introduces HTTP, Playwright, Task Scheduler, or any mutation on Calendar load — the two
new presentation types (`OperationsCalendarPassdownPresentation`/`OperationsCalendarNotesPresentation`)
are pure formatters with no service/repository dependency of any kind, and `OperationsCalendarService`
gained no new constructor parameter. Draft creation, note resolution, and finalization are all still
picked up on the next Calendar load/reload exactly as 12A/12B already proved, since every one of
12C's new fields is derived fresh from the same already-reloaded local state.

### Finalized-date note semantics (Part V) — unchanged decision, now explicit

Relevant/Important note counts for an already-finalized date reflect the **current** date-relevance
model, not what was frozen into that date's History record — consistent with 12B's own "live, not
frozen" decision for park hours, and never computed by querying History. The panel's own label ("Active
Operational Notes") is deliberately generic rather than "Notes in Finalized Passdown," avoiding any
implication that these counts represent what associates actually received (Part V).

### Accessibility (Part AB)

Day-cell `AutomationName` now composes the passdown-status and notes-summary accessibility text from
the same two new presentation types, e.g. `"August 21, 2026. Open, 10 AM to 10 PM. Today. Selected.
Finalized. 2 active operational notes, 1 Important."` — and, per the precedence fix above, a
Finalized date's automation name now says **only** "Finalized," never also "Draft in progress." The
selected-date panel's Passdown Status/Active Operational Notes values each carry a composed
`AutomationProperties.Name` (see above). Every existing 12A/12B accessibility guarantee (today/
selected/operating-state text equivalents, never color-only) is preserved.

### Visual validation — PARTIAL (unchanged constraint)

Same constraint as every prior Phase 9–12 document: no pixel/bitmap screenshot renderer is referenced
by this codebase's test projects. `OperationsCalendarView_MixedPassdownAndImportantNoteStatesAcrossTheMonth_ConstructsWithoutError`
exercises a month mixing No Draft/Draft/Finalized cells and Important-note dates through Avalonia's
real XAML loader (construction-level evidence, not pixel-level), alongside the existing 1280×860/
960×640 layout-pass tests (unchanged, still passing with the two new day-cell lines added). Recorded
honestly as **PARTIAL** — actual on-screen density/legibility should be spot-checked in a Windows 11 VM
per `CLAUDE.md`'s platform guidance before this ships.

### Schema (Part AJ) — unchanged

No migration was added. Database schema remains **10**; `AssociateDraftPayload` schema remains **3**;
`FinalizedPassdownSnapshot` schema remains **1**. Both new fields
(`OperationsCalendarDay.ImportantOperationalNoteCount`,
`OperationsCalendarSelectedDateSummary.ImportantOperationalNoteCount`) are computed fresh on every
`LoadAsync` from the existing `operational_notes` table — nothing new is persisted, and no Calendar-
specific status table was created (Part AJ's explicit instruction).

### Tests added (Phase 12C)

- `OperationsCalendarServiceTests.cs` — **+17**: the three day-cell passdown-flag combinations
  (no draft/no finalized, draft only, both draft and finalized — proving the raw `HasAssociateDraft`
  flag survives finalization at the data layer), relevant-note counts of 0/1/many, Important count as a
  subset of relevant count (day cell and selected-date summary both), resolved notes excluded from both
  counts, upcoming/expired/open-ended/boundary-date (ends-on/begins-on) note relevance, note count
  keyed to each grid date rather than "today," an off-month cell still carrying real underlying data,
  a call-counting proof that the Important count adds no new Operational Notes query, and a 120-note
  bounded-load data-set case.
- `OperationsCalendarPassdownPresentationTests.cs` (new, 10 tests) — `DeriveStatus`'s precedence
  directly (both flags true → Finalized; draft only; neither), and `Build`'s cell/detail/accessibility
  text for all three statuses including the last-saved/finalized-timestamp/empty-state note variants.
- `OperationsCalendarNotesPresentationTests.cs` (new, 5 tests) — the compact cell text, detail count/
  Important lines, and accessibility text across 0/1/many relevant and 0/1/many Important notes.
- `OperationsCalendarPresentationTests.cs` — **+4**: selected-date No Draft/Draft In Progress/Finalized
  detail text, and the combined relevant-plus-Important notes detail text.
- `OperationsCalendarViewModelTests.cs` — **+4**: day-cell `PassdownStatusLabel`/`NotesSummaryText` for
  No Draft/Draft/Finalized-over-Draft/Important-notes cases; the pre-existing automation-name test was
  updated (not just extended) to assert the new Finalized-only precedence and the new "active
  operational notes" wording.
- `OperationsCalendarViewTests.cs` — **+2**: a month mixing passdown lifecycle states and Important-note
  dates, and the selected-date panel's Passdown Status/Active Operational Notes text/binding.

**42 new tests total** (1323 Phase 12B baseline + 42 = 1365).

### Full validation (Phase 12C)

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **1365/1365**. Run three times;
one run hit the same pre-existing, previously-documented (Phase 6D) Avalonia headless cross-assembly
parallelism flake on an *unrelated* Phase 11 view (`OperationalNotesView`, not any Phase 12 file) during
`XamlIlPopulate` — the other two runs, including the final one, passed all 1365 cleanly. No Operations
Calendar or Operational Notes test failed on its own merits in any run. No live internet was used or
required.

### What Phase 12C does *not* implement

- Alerts, alarm-style severity styling, or any red-for-Important treatment (Part F/AA).
- The Manager workflow, app rename, refresh-policy changes, manual-hours/buyout UI redesign, ERT
  behavior changes, app logo, or the release ZIP rebuild (Phase 12F's scope) — all explicitly out of
  scope per `CLAUDE.md` and this phase's own instructions.
- Calendar-driven inclusion of a persistent note into an Associate draft — still exclusively
  Associate → Notes (Part K/U), unchanged since 11D.
- A History date pre-selection seam (Part M) — re-evaluated, still no clean seam; deferred again.
- A denser per-day note preview beyond a count, or note titles/bodies anywhere on this screen
  (Part S/U) — counts only, by design.
- Date-aware deep-linking into Operational Notes Manager's own filter/selection state (Part N) —
  evaluated and left generic, matching 11C's identical decision for Dashboard.

## Deferred to 12D–12F (superseded in part by 12D — see below)

- Richer source-health overlays beyond park hours.
- Any denser per-day note preview beyond a count, or note titles/bodies in month cells (12A/12C's own
  deferred item).
- A History date pre-selection seam — **now implemented, Phase 12D** (see below).
- A frozen-hours read model for finalized dates, if a future phase decides it's worth the added
  query cost/architecture.
- Date-aware deep-linking into Operational Notes Manager from Calendar's selected date — **re-evaluated
  in 12D, still deferred** (see below).
- The renamed-app work, refresh-policy overhaul, manual-hours/buyout redesign, ERT simplification,
  logo, Alerts, and Manager workflow — all still deferred per `CLAUDE.md`.

---

## Phase 12D — date interaction, deep-linking, and navigation/state-preservation refinement

Phase 12D turns the Calendar from a read-only summary into a real navigation hub: how a supervisor
moves between dates inside the Calendar itself (day-cell selection, off-month cells, day/month
stepping, Go to Today), how that maps onto past/today/future context, and — the one genuinely new
architectural seam — how "Open History" now hands a specific date to `PassdownHistoryViewModel`
instead of only opening the section generically. No schema change, no Alerts, no Manager workflow, no
editor of any kind was added to this screen.

### Selected-date semantics (Part A) — DisplayedMonth/SelectedDate stay Calendar-owned

`OperationsCalendarViewModel.DisplayedMonth`/`SelectedDate` remain exactly as independent from
`AssociateViewModel.SelectedDate`/`PassdownHistoryViewModel.SelectedItem`/
`OperationalNotesViewModel`'s own filter/selection as 12A established — nothing in this phase changes
that boundary. What 12D does add is a single internal invariant *within* the Calendar itself: every
navigation action (day-cell click, Previous/Next Day, Previous/Next Month, Go to Today) now keeps
`DisplayedMonth` and `SelectedDate` in the same month once it's done, via one new private helper,
`SetDisplayedMonthAndSelectedDate(DateOnly, DateOnly)`. It suppresses the per-property auto-load flag
(the same `_suppressAutoLoad` pattern 12A already used at construction), sets both properties, then
issues exactly one `LoadAsync` call — so no navigation action here can ever produce the "one load per
property" bug 12A's own constructor fix (see "Real bug found" above) already proved possible. Merely
*viewing* a date (as opposed to acting on it) never mutates anything — selection remains entirely
local/read-only (Part AB/AC, unchanged from 12A).

### Off-month cell selection (Part B) — chosen: selecting moves the Calendar to that month

**Decision: selecting an off-month cell moves `DisplayedMonth` to that date's own month, not left as
informational-only.** `SelectDateCommand` now always calls
`SetDisplayedMonthAndSelectedDate(new DateOnly(date.Year, date.Month, 1), date)` regardless of whether
`date` is in- or out-of-month; a same-month click is unaffected in practice because setting an
`[ObservableProperty]` to its own current value is a no-op — only `SelectedDate` actually changes, and
exactly one reload still fires (`SelectDateCommand_InMonthDate_TriggersExactlyOneLoad`). Chosen over
leaving off-month cells purely informational because a leading/trailing day is still a real, fully
selectable date (12A already established this — off-month cells were never fabricated), and silently
refusing to select it would be a worse, more confusing default than just navigating there — the same
behavior a supervisor would expect from any calendar picker.
`SelectDateCommand_OffMonthDate_MovesDisplayedMonthToMatch` proves this directly.

### Previous/Next Day (Part C) — added

**Decision: add explicit day-stepping**, as both `PreviousDayCommand`/`NextDayCommand` (buttons in the
selected-date summary panel, "‹ Prev Day"/"Next Day ›") — supervisors scanning adjacent days is a real,
common workflow this phase's own spec calls out, and the cost is two small buttons, not new UI
complexity. Both route through the same `SetDisplayedMonthAndSelectedDate` helper via a shared
`StepSelectedDate(int dayDelta)`: `SelectedDate.AddDays(delta)`, then `DisplayedMonth` recomputed from
the new date's own year/month — so a step that crosses a month (Aug 31 → Sep 1) or year (Dec 31 → Jan
1, Jan 1 → Dec 31) boundary always lands with `DisplayedMonth` following automatically, never left
pointing at the old month. Exactly one reload per step
(`NextDayCommand_CrossingMonthBoundary_TriggersExactlyOneLoad`).

### Previous/Next Month (Part D) — chosen: preserve day-of-month, clamp when shorter

**Decision: preserve `SelectedDate`'s day-of-month across a month change, clamping to the target
month's own last day when it's shorter** (Jan 31 → Feb 28 in a non-leap year, Feb 29 in a leap year) —
the explicitly preferred alternative over always resetting the selection to the 1st. Implemented as
`NavigateMonth(int monthDelta)`: `DateTime.DaysInMonth` computes the target month's day count,
`Math.Min(SelectedDate.Day, thatCount)` clamps, and the result is applied via
`SetDisplayedMonthAndSelectedDate` (again, exactly one reload —
`NextMonthCommand_TriggersExactlyOneLoad`). This is a **behavior change from 12A**, which previously
left `SelectedDate` completely untouched by month navigation
(`PreviousAndNextMonthCommands_MoveDisplayedMonth_PreserveSelectedDate` asserted the selection stayed
on the *original* date even after two months of navigation) — that test was renamed to
`PreviousAndNextMonthCommands_MoveDisplayedMonthAndCarrySelectedDateAlong` and now asserts the new,
deliberately chosen day-of-month-following behavior instead. Chosen because leaving the selection
behind in a month the grid no longer displays is the more surprising default; carrying the
day-of-month along (with clamping) is what a supervisor already expects from every other date picker
that supports month paging. Covered by dedicated Jan→Feb (non-leap and leap year) and Dec→Jan tests.

### Go to Today (Part E) — unchanged behavior, now explicitly Eastern-boundary-tested

`GoToTodayCommand`'s behavior is unchanged (resolves "today" via `IClock` + `EasternTimeZone`, sets
both `DisplayedMonth`/`SelectedDate` to it, never touches `AssociateViewModel.SelectedDate`) but now
routes through the same `SetDisplayedMonthAndSelectedDate` helper (one reload, not two — previously it
relied on the load-version guard to discard the first of two loads; now only one load is ever started)
and is covered by a new deterministic Eastern-midnight-boundary test,
`GoToTodayCommand_FollowsEasternMidnightBoundary`, using a mutable `FakeClock`: at 11:59 PM Eastern
"today" is Aug 26; advancing the same clock to 12:01 AM Eastern and re-invoking Go to Today moves it to
Aug 27 — proving the day boundary is Eastern, never machine-local.

### Past/today/future context (Part F) — no new field; PassdownStatus already carries it

**Decision: no new `Past`/`Today`/`Future` classification was added.** The existing
`OperationsCalendarPassdownStatus` (No Draft / Draft In Progress / Finalized, Phase 12C) already
carries the practically relevant temporal signal for every action this screen exposes — a Finalized
date behaves like a past date (Open History becomes available), a date with an in-progress draft
behaves like today, and No Draft behaves like a future date, all without inventing a second,
potentially-inconsistent classification computed from `selected.Date` vs. `Today` directly. Adding a
redundant date-based enum that agrees with `PassdownStatus` in every real scenario would be pure
duplication; one that could ever *disagree* with it would be a bug waiting to happen. No Draft on a
future date remains deliberately neutral text, never a warning/alert (unchanged from 12C).

### Open Passdown (Part G) — unchanged, now explicitly regression-tested for dirty state

`OpenAssociateCommand`'s behavior is unchanged: it raises `OpenAssociateRequested` with the Calendar's
`SelectedDate`, and `MainViewModel` sets `AssociateViewModel.SelectedDate` from it. What 12D adds is an
explicit audit + regression test of *why* this is already safe.

### Associate dirty-state interaction (Part H) — reuses AssociateViewModel's own existing guard

Setting `AssociateViewModel.SelectedDate` was already routed, since Phase 4C, through
`OnSelectedDateChanged` → `SwitchDateAsync`, which **flushes any pending autosave for the date
currently being edited before loading the new date**:

```csharp
private async Task SwitchDateAsync(DateOnly newDate)
{
    await FlushPendingAutosaveAsync().ConfigureAwait(true);
    await LoadDateAsync(newDate).ConfigureAwait(true);
}
```

`FlushPendingAutosaveAsync` cancels the debounce timer and saves immediately if the current date is
dirty (`_isDirty`) or a save is already in flight — so Calendar's "Open Passdown" (which sets
`Associate.SelectedDate` exactly like any other date change) was **already safe by construction**; no
second dirty-state framework was built. This phase adds the regression test that proves it end to end:
`CalendarOpenAssociateCommand_FlushesDirtyAssociateAutosaveBeforeSwitchingDate` edits a field on
`AssociateDate` (leaving it `Unsaved`), selects a different date in Calendar, clicks Open Passdown, and
asserts the fake service's `SaveCalls` actually recorded the flushed save for the *original* date
before the new date loaded — no unsaved work is silently discarded.

### History date preselection (Part I/J/K/AD/AK) — a clean seam was found; implemented

**Decision: implement it.** 12A/12B/12C all deferred this because `PassdownHistoryViewModel.SelectedItem`
is typed as a `PassdownHistorySummaryPresentation`, not a `DateOnly`, and there was no seam that didn't
mean either constructing that presentation type from outside `PassdownHistoryViewModel` or reaching
into its loaded-item list. Phase 12D adds the missing seam **on** `PassdownHistoryViewModel` itself,
where it belongs:

```csharp
public Task SelectByDateAsync(DateOnly date) => _pendingSelectByDate = SelectByDateCoreAsync(date);

private async Task SelectByDateCoreAsync(DateOnly date)
{
    await PendingLoad.ConfigureAwait(true);
    var match = HistoryItems.FirstOrDefault(item => item.PassdownDate == date);
    if (match is not null)
    {
        SelectedItem = match;
    }
}
```

It waits for the list load first (so a call arriving before construction's own load finishes still
resolves correctly — `SelectByDateAsync_CalledBeforeListLoadCompletes_StillResolvesCorrectly`), then
looks up the match from its own already-loaded `HistoryItems` — never constructing or searching a
presentation object from outside, never touched by `MainViewModel` or `OperationsCalendarViewModel`
directly. If no matching row exists, `SelectedItem` is left exactly as it was — no crash, no fabricated
selection (`SelectByDateAsync_NoMatchingDate_LeavesSelectionUnset_NoCrash`). `PendingSelectByDate`
mirrors the existing `PendingLoad`/`PendingDetailLoad` pattern so a test can await it deterministically.

`OperationsCalendarViewModel.OpenHistoryRequested` changed from a bare `EventHandler` to
`EventHandler<DateOnly>`, carrying the Calendar's `SelectedDate` — safe to widen since the command is
still only enabled once that date's `PassdownStatus` is `Finalized` (`CanOpenHistory`, unchanged), so
the carried date always corresponds to a row History is expected to have. `MainViewModel` wires it:

```csharp
Calendar.OpenHistoryRequested += (_, date) =>
{
    _ = History.SelectByDateAsync(date);
    ShowHistorySection();
};
```

— never constructing a `PassdownHistorySummaryPresentation`, never searching `History.HistoryItems`
itself, and never reaching past the one public method `PassdownHistoryViewModel` now exposes for this.
`CalendarOpenHistoryCommand_WhenFinalized_RoutesAndSelectsMatchingHistoryRecord` proves the full round
trip. **Query cost stays exactly what 12A already established** (Part AD): Calendar itself still never
loads History detail or a PDF blob to prepare this — `SelectByDateAsync` only ever touches
`HistoryItems`, the same metadata-only list `PassdownHistoryViewModel` already loads on construction;
setting `SelectedItem` triggers the *same* detail load (`_historyService.GetAsync`) a manual row click
would — this only happens because the user explicitly clicked "Open History," identical to History's
own normal interaction cost.

### Generic vs. explicit-date History navigation (Part K) — both proved side by side

Dashboard's own "Open History" (Phase 10C, unchanged) remains deliberately generic — it never calls
`SelectByDateAsync`, so a supervisor's existing History selection survives a Dashboard round trip
exactly as before (`GenericHistoryNavigation_PreservesExistingSelection_EvenAfterVisitingCalendar`
proves this holds even after an intervening visit to Calendar). Only Calendar's own date-specific
"Open History" action calls `SelectByDateAsync`, and it may *intentionally* replace whatever History
already had selected — that's correct precisely because the action is date-specific, not a bug in the
generic-preservation guarantee.

### Open Operational Notes (Part L/M) — chosen: stays generic; no date-context view added

**Decision: keep "Open Operational Notes" fully generic**, unchanged from 12A/12C. The Operational
Notes Manager's model (lifecycle/category/priority filters, not a date picker) doesn't have an
equally clean, low-cost seam the way History's own `SelectedItem` did, and the spec's own guidance for
this part is explicit that a temporary date-context view is optional and should be deferred if it
would complicate this phase. Building one here would be exactly the kind of feature creep this phase's
own instructions call out to avoid. Left as a candidate for a future phase.

### Context-aware actions (Part N/O) — already correct from 12A/12C; disable, not hide

Re-confirmed, no code changed: Open Passdown/Open Operational Notes have no `CanExecute` restriction
(always available, any status); Open History is *disabled* (not hidden) via `CanOpenHistory` until the
selected date is Finalized. **Decision: disable, don't hide** — an always-visible, sometimes-disabled
button is more predictable and discoverable than a control that appears/disappears depending on
status, and this was already 12A's own choice; 12D simply confirms and documents it as the deliberate,
consistent rule for this screen rather than leaving it implicit.

### Action labels (Part O) — unchanged, already precise

"Open Passdown"/"Open History"/"Open Operational Notes"/"Today"/"Previous Month"/"Next Month" were
already precise, non-vague labels from 12A; 12D's two new actions follow the same convention:
"Previous Day"/"Next Day" (button text "‹ Prev Day"/"Next Day ›", automation name "Previous day"/"Next
day").

### Selected-date summary (Part P) — Previous/Next Day added; layout otherwise unchanged

The selected-date panel gained one small addition: a `StackPanel` with the two day-stepping buttons,
placed directly under the "Today" chip and above the Park/Passdown/Notes sections — no new panel, no
editor, and the existing Park Status/Passdown Status/Active Operational Notes/action-button layout from
12B/12C is otherwise untouched.

### Month/date state preservation (Part Q/R/S/T/X) — unchanged guarantee, now covered from more angles

`OperationsCalendarViewModel` is still constructed once at composition-root time and stays alive for
the app's whole lifetime (12A) — nothing about 12D's new commands changes that. 12D adds explicit
coverage of the same "Calendar state survives navigating away and back" guarantee 12A already proved,
from three additional angles: after Open Passdown
(`CalendarState_SurvivesOpenAssociateThenReturning`), after Open Operational Notes
(`CalendarState_SurvivesOpenOperationalNotesThenReturning`), and (implicitly, since the mechanism is
identical) after Open History via the new `SelectByDateAsync` path — none of Calendar's own
`DisplayedMonth`/`SelectedDate` state is ever reset by any of these routing actions, and nothing
re-anchors it to Today automatically on return. Reload-preserves-selection and
finalization/note-count-reflection-on-reload were already proved at the Application-layer snapshot
level in 12A–12C and are unaffected by this phase's ViewModel-only changes.

### Local-only guarantee (Part AC) — unchanged

No HTTP, Playwright, `IRefreshOrchestrator`, or Task Scheduler dependency was added or is reachable —
`OperationsCalendarViewModel`'s constructor shape is unchanged, and `Constructor_HasNoRefreshOrPdfDependency`
still passes unmodified. Every new command (`PreviousDayCommand`/`NextDayCommand`, the reshaped
`SelectDateCommand`/`PreviousMonthCommand`/`NextMonthCommand`/`GoToTodayCommand`) still only ever calls
`IOperationsCalendarService.LoadAsync` — the same one local, bounded-query method 12A–12C already
proved is network-free.

### Accessibility (Part AE) — two new automation names

"Previous day"/"Next day" join the existing "Previous month"/"Go to today"/"Next month"/"Calendar month
grid"/"Selected date summary"/"Open Passdown"/"Open History"/"Open Operational Notes" set;
`OperationsCalendarView_WithSnapshot_ExposesCriticalAutomationNames` was extended (not replaced) to
assert both. Day-cell automation text is unchanged from 12C.

### Keyboard behavior (Part AF/AG) — no new control shape

Previous/Next Day are plain `Button`s, identical in shape to every other action on this screen — no
custom key handling was added or needed; FluentTheme's default Tab order and focus ring already make
them keyboard-reachable, and Enter/Space already activate them through Avalonia's standard `Button` key
bindings. **Decision: no arrow-key grid navigation was added** — the spec's own guidance treats this as
optional and warns against building custom grid-navigation complexity without a clear need; day cells
remain individually Tab-reachable `Button`s exactly as 12A established, which is sufficient for every
action this screen exposes. No manual focus-forcing was added when a deep-link action switches
sections — consistent with every prior Phase 9–12 view, which also never forces focus onto a specific
control after a section switch; Avalonia's own default focus behavior on the newly-shown view is
relied upon, with no focus traps introduced.

### Duplicate-load/performance audit (Part AI) — the single load-per-action guarantee

Every navigation action added or reshaped in this phase (`SelectDateCommand`, `PreviousDayCommand`,
`NextDayCommand`, `PreviousMonthCommand`, `NextMonthCommand`, `GoToTodayCommand`) now routes through
one shared helper, `SetDisplayedMonthAndSelectedDate`, that suppresses the auto-load flag while setting
both properties and issues exactly one `LoadAsync` call — proven directly by six new
`..._TriggersExactlyOneLoad` tests (one per action family: month nav, day step, day step crossing a
month boundary, Go to Today, in-month selection). This closes off, by construction, the exact class of
bug 12A's own constructor fix (see "Real bug found") already found once for two properties changing at
construction time — now guaranteed for every runtime navigation action too, not just construction.

### Schema (Part AM) — unchanged

No migration was added. Database schema remains **10**; `AssociateDraftPayload` schema remains **3**;
`FinalizedPassdownSnapshot` schema remains **1**. Every piece of 12D's new state
(`PassdownHistoryViewModel`'s `_pendingSelectByDate` field, the Calendar's internal navigation helper)
is in-memory ViewModel state only — nothing new is persisted, matching Part AM's explicit instruction.

### Visual validation — PARTIAL (unchanged constraint)

Same constraint as every prior Phase 9–12 document: no pixel/bitmap screenshot renderer is referenced
by this codebase's test projects. The existing 1280×860/960×640 `Measure`/`Arrange` layout-pass tests
(`OperationsCalendarView_LayoutAt1280x860_DoesNotThrow` / `..._LayoutAt960x640_DoesNotThrow`, unchanged
from 12B) still pass with the two new Previous/Next Day buttons added to the selected-date panel.
Recorded honestly as **PARTIAL** — actual on-screen density/button-crowding should be spot-checked in a
Windows 11 VM per `CLAUDE.md`'s platform guidance before this ships.

### Tests added (Phase 12D)

- `OperationsCalendarViewModelTests.cs` — **+22** (net; one pre-existing test was renamed and its
  assertions updated to match the new month-navigation behavior rather than removed): month navigation
  carrying the day-of-month with clamping (non-leap Feb, leap Feb, Dec→Jan), off-month selection moving
  `DisplayedMonth`, Previous/Next Day advancing/stepping back, day-stepping crossing a month boundary
  (both directions, including into a leap-year February) and a year boundary (both directions), the
  Eastern-midnight "Go to Today" boundary test, and one `..._TriggersExactlyOneLoad` test per action
  family (month nav, in-month selection, day step, day step across a month boundary, Go to Today); the
  existing "Open History" event test was extended to assert the event now carries the selected date.
- `PassdownHistoryViewModelTests.cs` — **+4**: `SelectByDateAsync` selecting a matching finalized date,
  safely no-op-ing on an unknown date, intentionally replacing an existing selection, and resolving
  correctly when called before the list load completes.
- `MainViewModelNavigationTests.cs` — **+5**: the Associate dirty-autosave-flushed-before-switch
  regression, the full Calendar→History date-preselection round trip, generic History navigation still
  preserving an existing selection after a Calendar visit, and Calendar's own state surviving an Open
  Associate round trip and an Open Operational Notes round trip.
- `OperationsCalendarViewTests.cs` — **+0 tests, +2 assertions**: the existing critical-automation-names
  test was extended to also assert "Previous day"/"Next day".

**24 new tests total** (1365 Phase 12C baseline − 1 renamed (no net count change) + 24 new = 1389),
plus 2 assertions added to an existing test.

### Full validation (Phase 12D)

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **1389/1389** on clean runs.
Run five times total under normal (parallel) execution; three of those five runs hit the same
pre-existing, previously-documented (Phase 6D) Avalonia headless cross-assembly parallelism flake
(`Avalonia.Controls.Panel.ChildrenChanged`/`AvaloniaList.NotifyAdd`/`AvaloniaPropertyDictionaryPool`
during `XamlIlPopulate`) on *unrelated* Phase 9/10 views (`HistoryView`, `DashboardView` — different
tests each time, never the same one twice, and never a Phase 12 file). The other two runs passed all
1389 cleanly. No Operations Calendar, Passdown History, or Main navigation test failed on its own
merits in any run. No live internet was used or required.

### What Phase 12D does *not* implement

- Any editor of any kind on this screen — still a summary/routing surface only.
- A `Past`/`Today`/`Future` classification field — deliberately not added (Part F); `PassdownStatus`
  already carries the practically relevant signal.
- Arrow-key grid navigation between day cells — deliberately deferred as unnecessary complexity (Part
  AF); Tab-reachable day-cell buttons remain sufficient.
- Date-aware deep-linking into Operational Notes Manager — re-evaluated, still deferred (Part L/M); no
  clean, low-cost seam exists yet without risking feature creep into the Manager's own model.
- A frozen-hours read model for finalized dates (12B Part S) — still deferred.
- Alerts, alarm-style severity styling, the Manager workflow, app rename, refresh-policy changes,
  manual-hours/buyout UI redesign, ERT behavior changes, app logo, or the release ZIP rebuild (Phase
  12F's scope) — all explicitly out of scope per `CLAUDE.md` and this phase's own instructions.

## Deferred to 12E–12F

- Richer source-health overlays beyond park hours.
- Any denser per-day note preview beyond a count, or note titles/bodies in month cells.
- A frozen-hours read model for finalized dates, if a future phase decides it's worth the added
  query cost/architecture.
- Date-aware deep-linking into Operational Notes Manager from Calendar's selected date.
- Arrow-key grid navigation between day cells, if a future phase decides supervisors need it.
- The renamed-app work, refresh-policy overhaul, manual-hours/buyout redesign, ERT simplification,
  logo, Alerts, and Manager workflow — all still deferred per `CLAUDE.md`.

## Phase 12E — Calendar polish, density, accessibility, and regression cleanup

Phase 12E is a **visual/layout/accessibility polish pass**, not a feature phase: no new Calendar
capability, Alerts, Manager workflow, business-logic change, or schema migration was added. It audits
the screen 12A–12D built and tightens hierarchy, density, and consistency with the rest of the app
before final Phase 12 release work.

### Visual hierarchy audit (Part A) — mostly already correct; one addition

Re-reading the screen against the intended hierarchy (title → month nav → grid → selected-date summary
→ actions) confirmed the header/nav/grid were already appropriately sized (no oversized title area, no
web-dashboard card sprawl) — 12A–12D's own density choices already matched Dashboard's established
recipe. The one real gap: the selected-date panel had no visual anchor marking it as the screen's
secondary hierarchy tier (below the grid, above nothing). `Border.heroPanel` — the exact same "3px
accent top rule" recipe `DashboardView`/`AssociateView` already use for their own most-prominent
panel(s) — was added to `OperationsCalendarView.axaml`'s own `UserControl.Styles` (each `UserControl`'s
styles are scoped to its own visual tree in Avalonia, so this could not simply be inherited) and applied
to the Selected Date panel. This is reuse, not invention, and adds zero extra vertical space.

### Month header / weekday header (Parts B/C) — audited, no change needed

Previous/Today/Next are already compact `secondaryAction` buttons with accessible names ("Previous
month"/"Go to today"/"Next month" — never arrow-only ambiguity), and Previous/Next Day already live in
the selected-date panel, not competing with month navigation (Phase 12D's own placement). The weekday
header's `Grid ColumnDefinitions="*,*,*,*,*,*,*"` already matches the day grid's own `UniformGrid
Columns="7"` column count exactly, so headings stay aligned with their columns at every window size.

### Day-cell layout and density (Part D)

The content stack (day number → operating-state/hours → passdown status → notes summary) already
matched the spec's own priority order; the only change is a small breathing-room increase (outer
`StackPanel Spacing` from `1` to `2`) so the day number reads as visually separate from the metadata
block beneath it, without growing `MinHeight` (still `72`, unchanged — no clipping-risk regression).

### Today / Selected / off-month styling (Part E) — a real gap, fixed

**Before**: `.selected` only changed `Background` (no border change), so a selected-but-not-today cell
was one shade of background fill away from an ordinary cell — a weak signal on its own. **After**:
- `.today` (unchanged): accent-colored outline only (`BorderThickness="2"`), background transparent.
- `.selected`: filled + outlined (`Background=PassdownBandBrush` **and** the same accent
  `BorderBrush`/`BorderThickness="2"`) — unmistakable even when the date isn't Today.
- `.today.selected` (new, explicit compound selector): a deeper tint (`PassdownChipAccentBrush`,
  already used elsewhere in the app for "elevated" chip content) and a slightly thicker border
  (`2.5`), so the combined state reads as its own deliberate, most-emphasized treatment rather than
  silently collapsing into whichever single-state rule happened to win. Off-month cells are unchanged
  (`Opacity="0.4"`, day-number-only content preserved from 12A–12C). None of this relies on color
  alone — `IsToday`/`IsSelected` are both still always present in the cell's own `AutomationName`
  (unchanged from 12B/12C).

### Operating-state / passdown / notes presentation consistency (Parts F/G/H/Q)

Auditing Dashboard's own equivalent surfaces (`DashboardView.axaml`'s Park Status/Passdown Status
panels) found **zero color-coding for operating state or passdown status anywhere in the app** — both
use a plain `chip` badge and a plain bold `hoursHeadline`/status text, regardless of Open/Closed/
Custom/Buyout or No Draft/Draft/Finalized. Given Part Q's own instruction ("visual consistency with the
rest of the app") outranks Part F/G's *optional* "recommended" emphasis suggestions, **Calendar's
operating-state and passdown-status text were deliberately left unchanged** — adding new color
semantics here that don't exist on the peer Dashboard screen would itself be the inconsistency, not a
fix. This was an audit-and-confirm, not a no-op oversight.

Operational Notes' "Important" indicator was a genuine, if minor, inconsistency: Calendar used a plain
`Classes="muted"` treatment while every other screen (`DashboardView`, `AssociateView`,
`OperationalNotesView`) uses one established convention — `TextBlock.priorityText` / `.important`
(bold + `PassdownChipAccentForegroundBrush`, never a red/alarm color; explicitly documented in those
views as "the word itself communicates priority, this is reinforcement only"). Both style rules were
added to `OperationsCalendarView.axaml`'s own styles (verbatim copies) and applied:
- **Selected-date panel**: `OperationalNotesImportantText` now uses `Classes="priorityText important"`
  (full-size, matching Dashboard/Associate/Notes exactly).
- **Day cell**: the notes-summary line now carries `Classes="dayIndicator priorityText"
  Classes.important="{Binding HasImportantOperationalNotes}"` — a new bool on
  `OperationsCalendarDayViewModel` (`ImportantOperationalNoteCount > 0`). The two-class
  `priorityText.important` selector's higher specificity overrides `dayIndicator`'s muted foreground
  when Important, while `dayIndicator`'s own `FontSize="10"` still wins the (equal-specificity,
  order-based) contest against `priorityText`'s `12.5` — so the cell keeps its compact 10px sizing and
  only gains the bold/accent color, never growing text size to signal importance (Part N: no tiny text
  to force density, but also no oversized text to force emphasis).

### Selected-date panel and action grouping (Parts I/J/P)

The panel's section content (Park/Passdown/Active Operational Notes/actions) was already close to the
spec's own target structure from 12B–12D; two thin `Border Height="1"
Background="{DynamicResource PassdownDividerBrush}"` hairlines were added — one after the Previous/Next
Day buttons (separating day-identity/day-stepping from the informational sections) and one before the
workflow-action buttons (separating information from routing actions) — a "subtle divider," never a
heavy boxed card, matching Part P's own instruction. This makes the three groups (date-navigation
controls, information, workflow-routing controls) visually distinct without adding a group label or
growing the panel. The three repeated `Margin="0,4,0,0"` inline overrides on the Park Status/Passdown
Status/Active Operational Notes section-title `TextBlock`s were consolidated into one shared
`TextBlock.sectionTitle.detail` modifier style (Part AC cleanup).

### Disabled "Open History" accessibility (Part S)

`AutomationProperties.HelpText` on "Open History" was previously a static string regardless of enabled
state. It is now bound to a new `OperationsCalendarViewModel.OpenHistoryHelpText` computed property —
`"Opens Passdown History for the selected date's finalized record."` when enabled, `"Opens Passdown
History. No finalized passdown for this date yet."` when disabled — so a screen reader's explanation of
*why* the control is currently unavailable stays accurate rather than only ever stating the general
rule. Wired via `[NotifyPropertyChangedFor(nameof(OpenHistoryHelpText))]` on the same `Snapshot`
property that already drives `CanOpenHistory`'s own `NotifyCanExecuteChangedFor` — no new load, no new
service call, just an additional computed-property notification alongside the existing one.

### 1280×860 / 960×640 (Parts K/L) — unchanged pass, plus a stronger combined regression test

The existing `OperationsCalendarView_LayoutAt1280x860_DoesNotThrow`/`..._LayoutAt960x640_DoesNotThrow`
tests (12B, unchanged) still pass with every 12E visual change applied. A new
`OperationsCalendarView_DenseMixedStates_LayoutAt960x640_DoesNotThrow` test combines the *densest*
realistic content (every operating state rotated across the grid, mixed passdown lifecycle, Important
notes) with the *tightest* supported viewport in one test — a stronger regression signal than the
previously separate single-concern dense/960×640 tests, since real density problems tend to appear only
when both pressures apply together.

### Windows 100/125/150% scaling preparation (Part M) — audited, no brittle widths found

No new fixed pixel widths were introduced. The month-grid/selected-panel split remains `ColumnDefinitions="*,320"`
(unchanged from 12A) — a deliberate, previously-justified fixed width for the detail panel, not a new
brittle addition; the grid column itself is still `*` (star-sized). All new elements (dividers, the
`heroPanel` border-thickness change, the two new style classes) use relative/auto sizing only.

### Typography / spacing (Parts N/O) — audited, one minor addition

No new font families or font sizes were introduced. The one spacing change (day-cell outer
`StackPanel.Spacing` `1`→`2`) is a small, deliberate density adjustment, not a broad repadding pass —
broader header/content padding was audited and left unchanged, since it already exactly matches
Dashboard's own established recipe (`Border Padding="24,16"`, `ScrollViewer Padding="24,20,24,24"`) and
further tightening would have created a Calendar-only deviation (Part Q).

### Top-level nav consistency (Part Q) — audited, already correct

`MainWindow.axaml`'s `Button.appSectionItem`/`.active` styling is already identical for all five
sections including Calendar (same padding, font size, bottom-border-accent selected treatment, hover
fill, and `"<Section> section"` automation-name convention) — no change was needed or made.

### Accessibility text audit (Part R) — re-confirmed, no regression

Day-cell `AutomationName` composition (`OperationsCalendarDayViewModel.BuildAutomationName`) is
unchanged in Phase 12E — the Finalized-over-Draft precedence fix (12C) already eliminated the stale
"Finalized. Draft in progress." duplication, and `DayViewModel_AutomationName_IncludesEveryTextualIndicator`
(unchanged) still asserts it directly. No new visual state in 12E lacks a textual equivalent: Today/
Selected/Today+Selected are still all three named booleans surfaced identically in `AutomationName`
regardless of the new border/background treatment, and the "Important" notes accent has no automation-
name impact (the existing `notesPresentation.AccessibilityText` clause — "2 active operational notes, 1
Important" — already spells this out in words, unaffected by the new visual styling).

### Keyboard / focus audit (Part T/U) — walked through, no gap found

Every control on this screen (Previous/Next Month, Today, day cells, Previous/Next Day, Open Passdown/
History/Operational Notes) is still a plain `Button` — 12E added no new interactive control shape (the
two new dividers are non-interactive `Border`s). FluentTheme's default Tab order and focus ring make
every one of them keyboard-reachable in visual order top-to-bottom/left-to-right; Enter/Space already
activate a focused `Button` through Avalonia's standard bindings. Walking the realistic flow the spec
describes (Calendar → month nav → Today → select a day → Previous/Next Day → Open Passdown → back →
Open History if enabled → Open Operational Notes) surfaces no focus trap and no unreachable control; a
disabled "Open History" is skipped by Tab like any other disabled `Button`, consistent with the rest of
the app. No custom arrow-key grid navigation was added (deliberately deferred since 12D; still true).
No manual focus-forcing was added after any state change (Previous/Next Month, Go to Today, Previous/
Next Day, off-month selection) — consistent with every prior Phase 9–12 view, none of which force focus
onto a specific control after a state change or section switch.

### Loading / error state (Parts Y/Z) — audited, no change needed

The `ProgressBar IsIndeterminate="True" Height="3"` occupies a fixed 3px row that doesn't resize the
surrounding layout when toggled, and `ErrorMessage` renders through the same `fieldError` class every
other screen uses, with the same concise, non-technical text ("Unable to load the operations calendar
right now.") — no SQL/stack trace/HTTP detail is exposed anywhere in this path, unchanged from 12A.

### No new business logic (Part AA) — confirmed

No hours precedence, draft precedence, note-relevance, finalization, History, or month/day-semantics
code was touched. The only non-style production changes are: one new bool
(`OperationsCalendarDayViewModel.HasImportantOperationalNotes`, a pure derived read of an existing
field) and one new computed string (`OperationsCalendarViewModel.OpenHistoryHelpText`, reusing the
existing `CanOpenHistory()` check verbatim rather than a second condition).

### Performance / load regression audit (Part AB) — confirmed unaffected

No new service/repository call was added. `HasImportantOperationalNotes` and `OpenHistoryHelpText` are
both computed from data the ViewModel already holds (`ImportantOperationalNoteCount`, `Snapshot`) —
zero new `IOperationsCalendarService.LoadAsync` calls, and the existing one-load-per-navigation-action
guarantee (12D) is untouched, since no navigation/command code was modified in this phase.

### XAML cleanup (Part AC)

- `TextBlock.sectionTitle.detail` consolidates three previously-duplicated inline `Margin="0,4,0,0"`
  overrides into one shared modifier style.
- `Border.heroPanel`/`TextBlock.priorityText`/`.important` were added as verbatim reuses of
  established app-wide styles rather than new, Calendar-specific inventions.

No broader style-framework refactor was attempted — changes stayed local to this one view, per the
spec's own instruction not to build a giant style framework just for Calendar.

### Schema (Part AH) — unchanged

No migration was added. Database schema remains **10**; `AssociateDraftPayload` schema remains **3**;
`FinalizedPassdownSnapshot` schema remains **1**.

### Tests added (Phase 12E)

- `OperationsCalendarViewModelTests.cs` — **+3**: `HasImportantOperationalNotes` true/false on the day
  cell, and `OpenHistoryHelpText` reflecting (and updating alongside `CanOpenHistory`) whether the
  selected date is Finalized.
- `OperationsCalendarViewTests.cs` — **+3**: a sparse-month (Part V) construction check asserting no
  day cell renders a notes line when there are no relevant notes, a Today+Selected-same-cell
  construction check (Part E, the new compound style), and the combined dense-mixed-states-at-960×640
  layout regression test (Parts K/L/AE).

**6 new tests total** (1389 Phase 12D baseline + 6 = 1395). No existing 12A–12D test was modified —
every one of them still passes unchanged, which is itself the regression evidence for Part AE's
"existing 12A–12D tests remain green," "no Finalized+Draft duplicate regression," "no off-month overlay
leakage regression," "one-load navigation behavior remains intact," and "local-only guarantee remains
intact" items — all still enforced by the unmodified 12A–12D suite.

### Full validation (Phase 12E)

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **1395/1395**, run three times
consecutively; all three runs passed cleanly with no flake observed (the previously-documented Phase 6D
Avalonia headless cross-assembly parallelism flake did not reproduce in any of the three runs this
time — its absence here is not evidence it's fixed, consistent with its own prior intermittent history
across 12A–12D). No live internet was used or required.

### Visual validation — PARTIAL (unchanged constraint)

Same constraint as every prior Phase 9–12 document: no pixel/bitmap screenshot renderer is available in
this codebase's test projects or this development environment. Evidence is construction- and layout-level
only: real-XAML-loader construction across a sparse month, a dense/mixed-operating-state month, a
Today+Selected-same-cell case, and a `Measure`/`Arrange` layout pass at both 1280×860 and the combined
dense-content/960×640 worst case — all succeeding without exception. No pixel-level clipping/contrast/
legibility claim is made beyond what these construction/layout checks prove. Actual on-screen appearance
(the new Selected-cell fill+border treatment, the Today+Selected compound tint, the Important-notes
accent color, the heroPanel accent rule, and 100/125/150% Windows scaling) should be spot-checked in a
Windows 11 VM per `CLAUDE.md`'s platform guidance before this ships — recorded honestly as **NOT
EXECUTED** for the Windows-scaling-specific check, since no Windows host was available this session.

### What Phase 12E does *not* implement

- Any new Calendar capability, Alerts, Manager workflow, or app rename — all explicitly out of scope.
- Operating-state or passdown-status color-coding — audited and deliberately **not** added, to stay
  consistent with Dashboard's own zero-color-coding treatment of the same concepts (Part Q outranking
  Part F/G's optional emphasis suggestion).
- Arrow-key grid navigation between day cells — still deliberately deferred (12D's own decision,
  re-confirmed here as still unnecessary).
- A frozen-hours read model for finalized dates, date-aware deep-linking into Operational Notes Manager,
  the renamed-app work, refresh-policy overhaul, manual-hours/buyout redesign, ERT simplification, logo,
  or the release ZIP rebuild — all still deferred per `CLAUDE.md` and Phase 12F's own scope.

## Phase 12F — final audit and release closeout

Phase 12F treated Phase 12A–12E as one completed feature and audited it end to end against
`AGENT_BUILD_SPEC.md` and every design decision documented above — reading the actual implementation
(`OperationsCalendarService`, `OperationsCalendarSnapshot`, `OperationsCalendarViewModel`,
`OperationsCalendarDayViewModel`, the four Desktop presentation types, `OperationsCalendarView.axaml`,
`MainViewModel`'s navigation wiring, the two new SQLite range-query methods, and
`PassdownHistoryViewModel.SelectByDateAsync`) directly, not merely trusting this document's own prior
claims. Full audit method and results live in `docs/release-strategy.md`'s "Phase 12F — Operations
Calendar final audit and release closeout" section; this entry records only the outcome.

**No genuine defect was found.** Every audited behavior — local-only loading, non-mutating navigation,
bounded range queries (never per-cell), off-month-cell overlay suppression, Finalized-over-Draft
lifecycle precedence, Operational Notes relevance/Important counts, the History date-preselection seam,
accessibility text composition, and schema stability (database 10 / `AssociateDraftPayload` 3 /
`FinalizedPassdownSnapshot` 1, confirmed directly in code) — matched its documented design exactly.
Unlike Phase 11F (which found and closed one test-coverage gap), Phase 12F required no code change and
added no new test, since nothing was found to regression-test. The full non-live suite passed
1395/1395 on four of five runs; the one flaky run reproduced the pre-existing, unrelated Phase 6D
Avalonia headless parallelism flake on `DashboardView` (a Phase 10 file), never a Phase 12 file — not a
regression.

Phase 12F rebuilt the Windows portable release (`Carowinds-Passdown-0.1.0-dev-win-x64.zip`, SHA-256
`dcceafee15be7f30ba410d3bb68032bd3eba057833f4a4c910fe5ecbbc4d4946`), verified via a `strings` symbol
check that the freshly published assemblies actually contain Phase 12A–12E code, confirmed staging-vs-
ZIP byte-for-byte integrity after a fresh extraction, and updated `docs/release-manifest.md`,
`docs/release-strategy.md`, `packaging/windows/TESTER-GUIDE.txt`, and
`packaging/windows/README-portable-release.md` to describe the Operations Calendar as a shipped
feature. No Windows host was available this session, so 100%/125%/150% display-scaling validation and
the full manual click-through checklist remain **NOT EXECUTED** — the same class of gap every prior
release rebuild (7E/9F/10F/11F) has carried, not a new risk.

**Phase 12 (Operations Calendar) is complete — READY WITH ACCEPTED LIMITATIONS.** Phase 13 (app rename,
refresh-policy overhaul, pricing/retrieval fixes, manual-hours/buyout redesign, ERT simplification,
logo, Alerts) was confirmed not started.
