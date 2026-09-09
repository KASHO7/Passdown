using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 9D Part O/P/T (extended Phase 10A Part P/X): proves Dashboard/History are additive and
/// never disturb the Associate workflow — all three child view models are constructed once and kept
/// alive for the app's lifetime, so switching <see cref="MainViewModel.SelectedSection"/> only
/// toggles which one is visible.
/// </summary>
public class MainViewModelNavigationTests
{
    private static readonly DateOnly AssociateDate = new(2026, 8, 26);
    private static readonly DateOnly HistoryDate = new(2026, 8, 25);

    private static MainViewModel CreateMainViewModel(out OperationsDashboardViewModel dashboard, out AssociateViewModel associate, out PassdownHistoryViewModel history) =>
        CreateMainViewModel(out dashboard, out associate, out _, out history);

    private static MainViewModel CreateMainViewModel(out OperationsDashboardViewModel dashboard, out AssociateViewModel associate, out OperationalNotesViewModel notes, out PassdownHistoryViewModel history) =>
        CreateMainViewModel(out dashboard, out _, out associate, out notes, out history);

    private static MainViewModel CreateMainViewModel(out OperationsDashboardViewModel dashboard, out OperationsCalendarViewModel calendar, out AssociateViewModel associate, out OperationalNotesViewModel notes, out PassdownHistoryViewModel history) =>
        CreateMainViewModel(out dashboard, out calendar, out associate, out _, out notes, out history);

    private static MainViewModel CreateMainViewModel(out OperationsDashboardViewModel dashboard, out OperationsCalendarViewModel calendar, out AssociateViewModel associate, out FakeAssociatePassdownService associateService, out OperationalNotesViewModel notes, out PassdownHistoryViewModel history)
    {
        dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        calendar = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), new FakeClock(new DateTimeOffset(AssociateDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)));
        associateService = new FakeAssociatePassdownService();
        associate = new AssociateViewModel(associateService, new FakeClock(new DateTimeOffset(AssociateDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)));
        notes = new OperationalNotesViewModel(new FakeOperationalNotesService(), new SystemClock());
        var historyService = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(HistoryDate, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        history = new PassdownHistoryViewModel(historyService, new FakeFinalizedPassdownPdfRetrievalService());
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        return new MainViewModel(dashboard, calendar, associate, notes, history, settings);
    }

    // 2: top-level nav defaults to Dashboard (spec Phase 10A Part Q).
    [Fact]
    public void SelectedSection_DefaultsToDashboard()
    {
        var main = CreateMainViewModel(out _, out _, out _);

        Assert.Equal(AppSection.Dashboard, main.SelectedSection);
        Assert.True(main.IsDashboardSectionSelected);
        Assert.False(main.IsAssociateSectionSelected);
        Assert.False(main.IsHistorySectionSelected);
    }

    [Fact]
    public void ShowAssociateSectionCommand_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _, out _);

        main.ShowAssociateSectionCommand.Execute(null);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.True(main.IsAssociateSectionSelected);
        Assert.False(main.IsDashboardSectionSelected);
    }

    [Fact]
    public void ShowHistorySectionCommand_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _, out _);

        main.ShowHistorySectionCommand.Execute(null);

        Assert.Equal(AppSection.History, main.SelectedSection);
        Assert.True(main.IsHistorySectionSelected);
        Assert.False(main.IsAssociateSectionSelected);
        Assert.False(main.IsDashboardSectionSelected);
    }

    // Phase 16B: the Settings / Personal Sync section — same plain section-switch shape as History.
    [Fact]
    public void ShowSettingsSectionCommand_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _, out _);

        main.ShowSettingsSectionCommand.Execute(null);

        Assert.Equal(AppSection.Settings, main.SelectedSection);
        Assert.True(main.IsSettingsSectionSelected);
        Assert.False(main.IsDashboardSectionSelected);
    }

    [Fact]
    public void ShowDashboardSectionCommand_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _, out _);

        main.ShowHistorySectionCommand.Execute(null);
        main.ShowDashboardSectionCommand.Execute(null);

        Assert.Equal(AppSection.Dashboard, main.SelectedSection);
        Assert.True(main.IsDashboardSectionSelected);
    }

    // 15: Dashboard -> Associate navigation (via the Dashboard's own "Open Passdown" action).
    [Fact]
    public async Task DashboardOpenPassdownCommand_NavigatesToAssociate()
    {
        var main = CreateMainViewModel(out var dashboard, out _, out _);
        await dashboard.PendingLoad;

        dashboard.OpenPassdownCommand.Execute(null);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
    }

    // 16: Dashboard -> History navigation (via the Dashboard's own "Open History" action).
    [Fact]
    public void DashboardOpenHistoryCommand_NavigatesToHistory()
    {
        var main = CreateMainViewModel(out var dashboard, out _, out _);

        dashboard.OpenHistoryCommand.Execute(null);

        Assert.Equal(AppSection.History, main.SelectedSection);
    }

    // Phase 11C Part O: Dashboard -> Operational Notes navigation (via "Open Operational Notes").
    [Fact]
    public async Task DashboardOpenOperationalNotesCommand_NavigatesToOperationalNotesSection()
    {
        var main = CreateMainViewModel(out var dashboard, out _, out _, out _);
        await dashboard.PendingLoad;

        dashboard.OpenOperationalNotesCommand.Execute(null);

        Assert.Equal(AppSection.OperationalNotes, main.SelectedSection);
        Assert.True(main.IsOperationalNotesSectionSelected);
    }

    // Phase 11C Part P/Q: generic "Open Operational Notes" navigation must never force the
    // OperationalNotesViewModel's own filter/selection — a supervisor's existing state there survives
    // the round trip exactly as it was.
    [Fact]
    public async Task DashboardOpenOperationalNotesCommand_PreservesExistingLifecycleFilter()
    {
        var main = CreateMainViewModel(out var dashboard, out _, out var notes, out _);
        await dashboard.PendingLoad;
        await notes.PendingLoad;
        notes.LifecycleFilter = OperationalNoteLifecycleFilter.Resolved;

        dashboard.OpenOperationalNotesCommand.Execute(null);

        Assert.Equal(AppSection.OperationalNotes, main.SelectedSection);
        Assert.Equal(OperationalNoteLifecycleFilter.Resolved, notes.LifecycleFilter);
    }

    // 23: selecting a History record never changes AssociateViewModel.SelectedDate.
    [Fact]
    public async Task SelectingHistoryRecord_NeverChangesAssociateSelectedDate()
    {
        var main = CreateMainViewModel(out _, out var associate, out var history);
        await associate.PendingLoad;
        await history.PendingLoad;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        Assert.Equal(AssociateDate, associate.SelectedDate);
    }

    // 17/24: navigating between Dashboard/Associate/History preserves Associate's draft/date state —
    // nothing is recreated.
    [Fact]
    public async Task NavigatingBetweenSections_PreservesAssociateState()
    {
        var main = CreateMainViewModel(out _, out var associate, out var history);
        await associate.PendingLoad;
        await history.PendingLoad;

        associate.ProjectedAttendanceText = "9000";
        await associate.PendingAutosave;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        main.ShowDashboardSectionCommand.Execute(null);
        main.ShowAssociateSectionCommand.Execute(null);

        Assert.Same(associate, main.Associate);
        Assert.Equal(AssociateDate, associate.SelectedDate);
        Assert.Equal("9000", associate.ProjectedAttendanceText);
    }

    // Phase 10C Part G: "Open Passdown" routes to Associate's Overview section specifically.
    [Fact]
    public async Task DashboardOpenPassdownCommand_RoutesToAssociateOverview()
    {
        var main = CreateMainViewModel(out var dashboard, out var associate, out _);
        await dashboard.PendingLoad;
        associate.SelectedSection = AssociateSection.Notes;

        dashboard.OpenPassdownCommand.Execute(null);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(AssociateSection.Overview, associate.SelectedSection);
    }

    // Phase 10C Part F: "Open Notes" is a true deep link to Associate's Notes section.
    [Fact]
    public async Task DashboardOpenNotesCommand_RoutesToAssociateNotes()
    {
        var main = CreateMainViewModel(out var dashboard, out var associate, out _);
        await dashboard.PendingLoad;

        dashboard.OpenNotesCommand.Execute(null);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(AssociateSection.Notes, associate.SelectedSection);
    }

    // Phase 10C Part H: neither deep link ever changes AssociateViewModel.SelectedDate — an associate
    // mid-edit on another date is never silently switched to today's record.
    [Fact]
    public async Task DashboardDeepLinks_NeverChangeAssociateSelectedDate()
    {
        var main = CreateMainViewModel(out var dashboard, out var associate, out _);
        await dashboard.PendingLoad;
        await associate.PendingLoad;

        dashboard.OpenNotesCommand.Execute(null);
        Assert.Equal(AssociateDate, associate.SelectedDate);

        dashboard.OpenPassdownCommand.Execute(null);
        Assert.Equal(AssociateDate, associate.SelectedDate);
    }

    // Phase 10C Part H: merely viewing/switching top-level sections never touches Associate's internal
    // section on its own — only the Dashboard's explicit "Open Passdown"/"Open Notes" actions do.
    [Fact]
    public void SwitchingTopLevelSections_NeverChangesAssociateInternalSection()
    {
        var main = CreateMainViewModel(out _, out var associate, out _);

        main.ShowAssociateSectionCommand.Execute(null);
        main.ShowHistorySectionCommand.Execute(null);
        main.ShowDashboardSectionCommand.Execute(null);

        Assert.Equal(AssociateSection.Overview, associate.SelectedSection);
    }

    // Phase 10C Part I: History's own selection state survives a Dashboard round trip — History is
    // constructed once and kept alive, so navigating away and back never resets SelectedItem.
    [Fact]
    public async Task NavigatingHistoryThenDashboardThenHistory_PreservesHistorySelection()
    {
        var main = CreateMainViewModel(out _, out _, out var history);
        await history.PendingLoad;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        main.ShowDashboardSectionCommand.Execute(null);
        main.ShowHistorySectionCommand.Execute(null);

        Assert.Equal(HistoryDate, history.SelectedItem?.PassdownDate);
    }

    // Phase 11B Part AI #1/#2/#3/#4 (updated Phase 12A Part B, Phase 16B): top-level navigation
    // includes Operational Notes between Associate Passdown and History, Operations Calendar
    // between Dashboard and Associate Passdown, and (as of Phase 16B) Settings last — no
    // Manager/Alerts value exists at all.
    [Fact]
    public void AppSection_ContainsOperationsCalendarAndOperationalNotesAndSettingsAndNoManagerOrAlerts()
    {
        var values = Enum.GetNames<AppSection>();

        Assert.Equal(["Dashboard", "OperationsCalendar", "AssociatePassdown", "OperationalNotes", "History", "Settings"], values);
    }

    // Phase 11B Part AI #5: Dashboard remains the default startup section.
    [Fact]
    public void SelectedSection_StillDefaultsToDashboard_WithOperationalNotesAdded()
    {
        var main = CreateMainViewModel(out _, out _, out _, out _);

        Assert.Equal(AppSection.Dashboard, main.SelectedSection);
    }

    // Phase 11B Part AI #6/#7: Dashboard <-> Operational Notes navigation.
    [Fact]
    public void DashboardToOperationalNotesAndBack_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _, out _, out _);

        main.ShowOperationalNotesSectionCommand.Execute(null);
        Assert.Equal(AppSection.OperationalNotes, main.SelectedSection);
        Assert.True(main.IsOperationalNotesSectionSelected);

        main.ShowDashboardSectionCommand.Execute(null);
        Assert.Equal(AppSection.Dashboard, main.SelectedSection);
    }

    // Phase 11B Part AI #8/#9: Operational Notes <-> Associate/History navigation.
    [Fact]
    public void OperationalNotesToAssociateAndHistory_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _, out _, out _);
        main.ShowOperationalNotesSectionCommand.Execute(null);

        main.ShowAssociateSectionCommand.Execute(null);
        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);

        main.ShowOperationalNotesSectionCommand.Execute(null);
        main.ShowHistorySectionCommand.Execute(null);
        Assert.Equal(AppSection.History, main.SelectedSection);
    }

    // Phase 11B Part AI #10/#11: Associate's SelectedDate and History's selection both survive a trip through Operational Notes.
    [Fact]
    public async Task NavigatingThroughOperationalNotes_PreservesAssociateDateAndHistorySelection()
    {
        var main = CreateMainViewModel(out _, out var associate, out _, out var history);
        await associate.PendingLoad;
        await history.PendingLoad;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        main.ShowOperationalNotesSectionCommand.Execute(null);
        main.ShowAssociateSectionCommand.Execute(null);
        main.ShowOperationalNotesSectionCommand.Execute(null);
        main.ShowHistorySectionCommand.Execute(null);

        Assert.Equal(AssociateDate, associate.SelectedDate);
        Assert.Equal(HistoryDate, history.SelectedItem?.PassdownDate);
    }

    // Phase 11B Part AI #12: no persistent-note operation ever touches the Associate draft.
    [Fact]
    public async Task OperationalNoteOperations_NeverChangeAssociateDraft()
    {
        var main = CreateMainViewModel(out _, out var associate, out var notes, out _);
        await associate.PendingLoad;
        await notes.PendingLoad;
        associate.ProjectedAttendanceText = "9000";
        await associate.PendingAutosave;

        notes.NewNoteCommand.Execute(null);
        notes.EditorTitle = "A persistent note";
        await notes.SaveCommand.ExecuteAsync(null);
        notes.RequestResolveCommand.Execute(null);
        await notes.ConfirmResolveCommand.ExecuteAsync(null);

        Assert.Equal("9000", associate.ProjectedAttendanceText);
        Assert.Equal(AssociateSection.Overview, associate.SelectedSection);
    }

    // Phase 11B Part AI #13/#14: structural proof that a persistent note's content can never reach an
    // Associate PDF or finalized History — AssociateViewModel has no IOperationalNotesService
    // dependency at all (mirrors the Application-layer constructor-reflection guarantee
    // OperationalNotesServiceTests already proves for the reverse direction).
    [Fact]
    public void AssociateViewModel_HasNoOperationalNotesServiceDependency()
    {
        var parameterTypes = typeof(AssociateViewModel)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType);

        Assert.DoesNotContain(parameterTypes, t => t == typeof(Carowinds.Passdown.Application.OperationalNotes.IOperationalNotesService));
    }

    // Phase 11B Part X: while the Operational Notes editor is dirty (Create/Edit in progress), the
    // other three top-level section buttons are disabled — Save/Cancel re-enables them.
    [Fact]
    public async Task DirtyOperationalNotesEditor_DisablesOtherTopLevelSectionButtons()
    {
        var main = CreateMainViewModel(out _, out _, out var notes, out _);
        await notes.PendingLoad;
        main.ShowOperationalNotesSectionCommand.Execute(null);

        notes.NewNoteCommand.Execute(null);

        Assert.False(main.ShowDashboardSectionCommand.CanExecute(null));
        Assert.False(main.ShowAssociateSectionCommand.CanExecute(null));
        Assert.False(main.ShowHistorySectionCommand.CanExecute(null));
        Assert.Equal(AppSection.OperationalNotes, main.SelectedSection);

        notes.CancelCommand.Execute(null);

        Assert.True(main.ShowDashboardSectionCommand.CanExecute(null));
        Assert.True(main.ShowAssociateSectionCommand.CanExecute(null));
        Assert.True(main.ShowHistorySectionCommand.CanExecute(null));

        main.ShowDashboardSectionCommand.Execute(null);
        Assert.Equal(AppSection.Dashboard, main.SelectedSection);
    }

    // Phase 12A Part B: Dashboard <-> Operations Calendar navigation, and Dashboard stays default.
    [Fact]
    public void DashboardToOperationsCalendarAndBack_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _, out _, out _, out _);

        Assert.Equal(AppSection.Dashboard, main.SelectedSection);

        main.ShowOperationsCalendarSectionCommand.Execute(null);
        Assert.Equal(AppSection.OperationsCalendar, main.SelectedSection);
        Assert.True(main.IsOperationsCalendarSectionSelected);

        main.ShowDashboardSectionCommand.Execute(null);
        Assert.Equal(AppSection.Dashboard, main.SelectedSection);
    }

    // Phase 12A Part F/G: "Open Passdown" from Calendar routes to Associate's Overview section and
    // explicitly sets its selected date to whatever Calendar had selected.
    [Fact]
    public async Task CalendarOpenAssociateCommand_RoutesToAssociateOverviewWithCalendarSelectedDate()
    {
        var main = CreateMainViewModel(out _, out var calendar, out var associate, out _, out _);
        await calendar.PendingLoad;
        associate.SelectedSection = AssociateSection.Notes;
        var targetDate = new DateOnly(2026, 8, 5);
        calendar.SelectDateCommand.Execute(targetDate);
        await calendar.PendingLoad;

        calendar.OpenAssociateCommand.Execute(null);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(AssociateSection.Overview, associate.SelectedSection);
        Assert.Equal(targetDate, associate.SelectedDate);
    }

    // Phase 13D Part AI/AJ: "View Date Pricing" from Calendar routes to Associate's Products section,
    // explicitly setting its selected date to whatever Calendar had selected — the local-only
    // navigation seam, never a network check.
    [Fact]
    public async Task CalendarViewDatePricingCommand_RoutesToAssociateProductsWithCalendarSelectedDate()
    {
        var main = CreateMainViewModel(out _, out var calendar, out var associate, out _, out _);
        await calendar.PendingLoad;
        associate.SelectedSection = AssociateSection.Notes;
        var targetDate = new DateOnly(2026, 8, 5);
        calendar.SelectDateCommand.Execute(targetDate);
        await calendar.PendingLoad;

        calendar.ViewDatePricingCommand.Execute(null);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(AssociateSection.Products, associate.SelectedSection);
        Assert.Equal(targetDate, associate.SelectedDate);
    }

    // Part AJ: View Date Pricing never mutates Calendar's own SelectedDate/Snapshot — navigation is a
    // one-way read of whatever was already selected, not a round trip back into Calendar's state.
    [Fact]
    public async Task CalendarViewDatePricingCommand_NeverChangesCalendarSelectedDate()
    {
        var main = CreateMainViewModel(out _, out var calendar, out _, out _, out _);
        await calendar.PendingLoad;
        var targetDate = new DateOnly(2026, 8, 5);
        calendar.SelectDateCommand.Execute(targetDate);
        await calendar.PendingLoad;

        calendar.ViewDatePricingCommand.Execute(null);

        Assert.Equal(targetDate, calendar.SelectedDate);
    }

    // Phase 12A Part F: "Open Operational Notes" from Calendar is generic navigation only.
    [Fact]
    public async Task CalendarOpenOperationalNotesCommand_NavigatesToOperationalNotesSection()
    {
        var main = CreateMainViewModel(out _, out var calendar, out _, out _, out _);
        await calendar.PendingLoad;

        calendar.OpenOperationalNotesCommand.Execute(null);

        Assert.Equal(AppSection.OperationalNotes, main.SelectedSection);
    }

    // Phase 12A Part C: switching to Calendar never corrupts Associate's own selected date, and vice
    // versa — the two are entirely independent unless Calendar's own "Open Passdown" is used.
    [Fact]
    public async Task SwitchingToCalendar_NeverChangesAssociateSelectedDate()
    {
        var main = CreateMainViewModel(out _, out var calendar, out var associate, out _, out _);
        await associate.PendingLoad;
        await calendar.PendingLoad;

        main.ShowOperationsCalendarSectionCommand.Execute(null);
        calendar.SelectDateCommand.Execute(new DateOnly(2026, 8, 5));
        await calendar.PendingLoad;
        main.ShowAssociateSectionCommand.Execute(null);

        Assert.Equal(AssociateDate, associate.SelectedDate);
        Assert.Equal(new DateOnly(2026, 8, 5), calendar.SelectedDate);
    }

    // Phase 12A Part C/B: Calendar's own displayed month/selected date survive navigating away and
    // back — nothing recreates it, matching how History's own selection already survives the same
    // round trip.
    [Fact]
    public async Task NavigatingAwayFromCalendarAndBack_PreservesItsOwnDisplayedMonthAndSelectedDate()
    {
        var main = CreateMainViewModel(out _, out var calendar, out _, out _, out var history);
        await calendar.PendingLoad;

        main.ShowOperationsCalendarSectionCommand.Execute(null);
        calendar.NextMonthCommand.Execute(null);
        calendar.SelectDateCommand.Execute(new DateOnly(2026, 9, 10));
        await calendar.PendingLoad;

        main.ShowDashboardSectionCommand.Execute(null);
        main.ShowHistorySectionCommand.Execute(null);
        main.ShowOperationsCalendarSectionCommand.Execute(null);

        Assert.Same(calendar, main.Calendar);
        Assert.Equal(new DateOnly(2026, 9, 1), calendar.DisplayedMonth);
        Assert.Equal(new DateOnly(2026, 9, 10), calendar.SelectedDate);
    }

    // Phase 12A Part J: History and Operational Notes remain independent of Calendar — neither their
    // own state nor Associate's is disturbed by visiting Calendar in between.
    [Fact]
    public async Task NavigatingThroughCalendar_PreservesHistorySelectionAndAssociateState()
    {
        var main = CreateMainViewModel(out _, out var calendar, out var associate, out _, out var history);
        await associate.PendingLoad;
        await history.PendingLoad;
        await calendar.PendingLoad;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        main.ShowOperationsCalendarSectionCommand.Execute(null);
        main.ShowAssociateSectionCommand.Execute(null);
        main.ShowOperationsCalendarSectionCommand.Execute(null);
        main.ShowHistorySectionCommand.Execute(null);

        Assert.Equal(AssociateDate, associate.SelectedDate);
        Assert.Equal(HistoryDate, history.SelectedItem?.PassdownDate);
    }

    // Phase 12A Part J: Operational Notes Manager is entirely unaffected by Calendar navigation.
    [Fact]
    public async Task NavigatingThroughCalendar_OperationalNotesManagerUnaffected()
    {
        var main = CreateMainViewModel(out _, out var calendar, out _, out var notes, out _);
        await notes.PendingLoad;
        await calendar.PendingLoad;
        notes.LifecycleFilter = OperationalNoteLifecycleFilter.Resolved;

        main.ShowOperationsCalendarSectionCommand.Execute(null);
        main.ShowOperationalNotesSectionCommand.Execute(null);

        Assert.Equal(OperationalNoteLifecycleFilter.Resolved, notes.LifecycleFilter);
    }

    // Phase 12A Part X (extends Phase 11B Part X): a dirty Operational Notes editor also disables
    // navigating to Operations Calendar, not only Dashboard/Associate/History.
    [Fact]
    public async Task DirtyOperationalNotesEditor_AlsoDisablesOperationsCalendarNavigation()
    {
        var main = CreateMainViewModel(out _, out _, out _, out var notes, out _);
        await notes.PendingLoad;
        main.ShowOperationalNotesSectionCommand.Execute(null);

        notes.NewNoteCommand.Execute(null);
        Assert.False(main.ShowOperationsCalendarSectionCommand.CanExecute(null));

        notes.CancelCommand.Execute(null);
        Assert.True(main.ShowOperationsCalendarSectionCommand.CanExecute(null));
    }

    // ==================================================================================
    // Phase 12D — date interaction, deep-linking, and state preservation
    // ==================================================================================

    // Part G/H/AA: "Open Passdown" from Calendar flushes Associate's pending autosave for the date
    // currently being edited before switching — reusing AssociateViewModel's own existing
    // SwitchDateAsync (flush-then-load) path rather than a second dirty-state framework. No unsaved
    // work is silently discarded.
    [Fact]
    public async Task CalendarOpenAssociateCommand_FlushesDirtyAssociateAutosaveBeforeSwitchingDate()
    {
        var main = CreateMainViewModel(out _, out var calendar, out var associate, out var associateService, out _, out _);
        await associate.PendingLoad;
        await calendar.PendingLoad;

        associate.ProjectedAttendanceText = "9000"; // dirty; the default 800ms debounce has not fired.
        Assert.Equal(AssociateAutosaveState.Unsaved, associate.AutosaveState);

        var targetDate = new DateOnly(2026, 8, 5);
        calendar.SelectDateCommand.Execute(targetDate);
        await calendar.PendingLoad;

        calendar.OpenAssociateCommand.Execute(null);
        await associate.PendingLoad;

        Assert.Contains(associateService.SaveCalls, call => call.OperatingDate == AssociateDate && call.Draft.ProjectedAttendance == 9000);
        Assert.Equal(targetDate, associate.SelectedDate);
        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
    }

    // Part I/J/AK: the full round trip using a Calendar service scripted to report the selected date
    // as Finalized — proving MainViewModel wires OpenHistoryRequested's carried date into
    // PassdownHistoryViewModel.SelectByDateAsync, never constructing/searching a presentation object
    // itself and never reaching into History's internals.
    [Fact]
    public async Task CalendarOpenHistoryCommand_WhenFinalized_RoutesAndSelectsMatchingHistoryRecord()
    {
        var calendarService = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) => FakeOperationsCalendarService.DefaultSnapshot(month, selected) with
            {
                Selected = FakeOperationsCalendarService.DefaultSnapshot(month, selected).Selected with
                {
                    PassdownStatus = OperationsCalendarPassdownStatus.Finalized,
                },
            },
        };
        var calendar = new OperationsCalendarViewModel(calendarService, new FakeClock(new DateTimeOffset(AssociateDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)));
        var dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        var associate = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(new DateTimeOffset(AssociateDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)));
        var notes = new OperationalNotesViewModel(new FakeOperationalNotesService(), new SystemClock());
        var historyService = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(HistoryDate, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        var history = new PassdownHistoryViewModel(historyService, new FakeFinalizedPassdownPdfRetrievalService());
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        var main = new MainViewModel(dashboard, calendar, associate, notes, history, settings);
        await calendar.PendingLoad;
        await history.PendingLoad;

        calendar.SelectDateCommand.Execute(HistoryDate);
        await calendar.PendingLoad;
        Assert.True(calendar.OpenHistoryCommand.CanExecute(null));

        calendar.OpenHistoryCommand.Execute(null);
        await history.PendingSelectByDate;

        Assert.Equal(AppSection.History, main.SelectedSection);
        Assert.Equal(HistoryDate, history.SelectedItem?.PassdownDate);
    }

    // Part K: plain top-level History navigation (not routed through Calendar's explicit date action)
    // must preserve whatever History already had selected — only Calendar's own date-specific "Open
    // History" is allowed to replace the selection.
    [Fact]
    public async Task GenericHistoryNavigation_PreservesExistingSelection_EvenAfterVisitingCalendar()
    {
        var main = CreateMainViewModel(out _, out var calendar, out _, out _, out var history);
        await history.PendingLoad;
        await calendar.PendingLoad;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        main.ShowOperationsCalendarSectionCommand.Execute(null);
        main.ShowHistorySectionCommand.Execute(null);

        Assert.Equal(HistoryDate, history.SelectedItem?.PassdownDate);
    }

    // Part R/S/T: Calendar's own DisplayedMonth/SelectedDate survive routing into Associate, History,
    // and Operational Notes and back — none of those explicit "Open ..." actions re-anchor Calendar to
    // Today or otherwise disturb its own state.
    [Fact]
    public async Task CalendarState_SurvivesOpenAssociateThenReturning()
    {
        var main = CreateMainViewModel(out _, out var calendar, out _, out _, out _);
        await calendar.PendingLoad;

        calendar.NextMonthCommand.Execute(null);
        calendar.SelectDateCommand.Execute(new DateOnly(2026, 9, 12));
        await calendar.PendingLoad;

        calendar.OpenAssociateCommand.Execute(null);
        main.ShowOperationsCalendarSectionCommand.Execute(null);

        Assert.Equal(new DateOnly(2026, 9, 1), calendar.DisplayedMonth);
        Assert.Equal(new DateOnly(2026, 9, 12), calendar.SelectedDate);
    }

    [Fact]
    public async Task CalendarState_SurvivesOpenOperationalNotesThenReturning()
    {
        var main = CreateMainViewModel(out _, out var calendar, out _, out _, out _);
        await calendar.PendingLoad;

        calendar.PreviousMonthCommand.Execute(null);
        await calendar.PendingLoad;
        var monthBefore = calendar.DisplayedMonth;
        var selectedBefore = calendar.SelectedDate;

        calendar.OpenOperationalNotesCommand.Execute(null);
        main.ShowOperationsCalendarSectionCommand.Execute(null);

        Assert.Equal(monthBefore, calendar.DisplayedMonth);
        Assert.Equal(selectedBefore, calendar.SelectedDate);
    }

    // ==================================================================================
    // Phase 15C — cross-screen workflow/state consistency
    // ==================================================================================

    // Spec §17: an off-month selection (a different month than whatever Calendar started
    // displaying, and not the 1st, so a clamp-to-month-start bug would be caught) must reach
    // Associate exactly as selected — SelectDate/SetDisplayedMonthAndSelectedDate never clamp the
    // day itself, only re-anchor DisplayedMonth to match it (Phase 12D Part B).
    [Fact]
    public async Task CalendarOffMonthSelection_OpenAssociate_PreservesExactSelectedDate()
    {
        var main = CreateMainViewModel(out _, out var calendar, out var associate, out _, out _);
        await calendar.PendingLoad;
        Assert.Equal(new DateOnly(2026, 8, 1), calendar.DisplayedMonth);

        var offMonthDate = new DateOnly(2026, 9, 15); // a different month than the one Calendar started on.
        calendar.SelectDateCommand.Execute(offMonthDate);
        await calendar.PendingLoad;
        Assert.Equal(new DateOnly(2026, 9, 1), calendar.DisplayedMonth); // Calendar re-anchored itself.

        calendar.OpenAssociateCommand.Execute(null);
        await associate.PendingLoad;

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(offMonthDate, associate.SelectedDate); // the exact date, not clamped to the 1st.
    }

    // Same proof for "View Date Pricing" (Products), the other Calendar -> Associate route.
    [Fact]
    public async Task CalendarOffMonthSelection_ViewDatePricing_PreservesExactSelectedDate()
    {
        var main = CreateMainViewModel(out _, out var calendar, out var associate, out _, out _);
        await calendar.PendingLoad;

        var offMonthDate = new DateOnly(2026, 7, 3); // a different month, before the one Calendar started on.
        calendar.SelectDateCommand.Execute(offMonthDate);
        await calendar.PendingLoad;

        calendar.ViewDatePricingCommand.Execute(null);
        await associate.PendingLoad;

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(AssociateSection.Products, associate.SelectedSection);
        Assert.Equal(offMonthDate, associate.SelectedDate);
    }
}
