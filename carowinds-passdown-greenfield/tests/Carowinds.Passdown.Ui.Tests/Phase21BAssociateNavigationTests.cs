using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 21B: Associate date navigation (previous/next, Today, the calendar popup's commit/dismiss
/// rules), the Eastern operational date behind every "today", and the reorganized toolbar (More menu,
/// PDF style, Export Diagnostics moved to Settings). No real Window here — popup behavior that needs
/// realized day cells lives in the run-alone <see cref="Phase15ERealWindowLayoutTests"/>.
/// </summary>
public class Phase21BAssociateNavigationTests
{
    /// <summary>2026-10-16 10:00 Eastern (EDT).</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 16);

    private static async Task<(AssociateViewModel ViewModel, FakeAssociatePassdownService Service, FakeClock Clock)> CreateAsync(
        DateTimeOffset? now = null,
        FakeOfficialDataRefreshCoordinator? coordinator = null,
        ManualDebounceScheduler? debounce = null,
        FakeAssociatePassdownTemplatePreferenceStore? templateStore = null)
    {
        var clock = new FakeClock(now ?? Now);
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, clock,
            autosaveDelay: (debounce ?? new ManualDebounceScheduler()).Delay,
            templatePreferenceStore: templateStore ?? new FakeAssociatePassdownTemplatePreferenceStore(),
            refreshCoordinator: coordinator,
            uiDispatcher: new ImmediateUiDispatcher());
        await viewModel.PendingLoad;
        await viewModel.PendingTemplatePreferenceLoad;
        return (viewModel, service, clock);
    }

    // ---------------------------------------------------------------- previous / next

    [Theory]
    [InlineData(2026, 10, 16, 2026, 10, 15, 2026, 10, 17)]
    [InlineData(2026, 11, 1, 2026, 10, 31, 2026, 11, 2)]   // month rollover backward
    [InlineData(2026, 10, 31, 2026, 10, 30, 2026, 11, 1)]  // month rollover forward
    [InlineData(2027, 1, 1, 2026, 12, 31, 2027, 1, 2)]     // year rollover backward
    [InlineData(2026, 12, 31, 2026, 12, 30, 2027, 1, 1)]   // year rollover forward
    [InlineData(2028, 3, 1, 2028, 2, 29, 2028, 3, 2)]      // leap day
    public async Task PreviousAndNextDay_MoveExactlyOneCalendarDay(int y, int m, int d, int py, int pm, int pd, int ny, int nm, int nd)
    {
        var (viewModel, service, _) = await CreateAsync();
        var start = new DateOnly(y, m, d);
        viewModel.SelectedDate = start;
        await viewModel.PendingLoad;

        viewModel.PreviousDayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal(new DateOnly(py, pm, pd), viewModel.SelectedDate);
        Assert.Equal(new DateOnly(py, pm, pd), service.LoadCalls[^1]);

        viewModel.SelectedDate = start;
        await viewModel.PendingLoad;
        viewModel.NextDayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal(new DateOnly(ny, nm, nd), viewModel.SelectedDate);
        Assert.Equal(new DateOnly(ny, nm, nd), service.LoadCalls[^1]);
        Assert.Empty(service.CheckOnlineCalls); // navigation never retrieves on its own.
    }

    [Fact]
    public async Task FutureDates_AreNotRestricted()
    {
        var (viewModel, _, _) = await CreateAsync();

        viewModel.SelectedDate = Today.AddYears(1);
        await viewModel.PendingLoad;
        viewModel.NextDayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Equal(Today.AddYears(1).AddDays(1), viewModel.SelectedDate);
        Assert.True(viewModel.NextDayCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- Today

    [Theory]
    [InlineData(-1)]
    [InlineData(-45)]
    [InlineData(1)]
    [InlineData(90)]
    public async Task GoToToday_FromPastOrFuture_SelectsTodaysEasternDate(int offsetDays)
    {
        var (viewModel, service, _) = await CreateAsync();
        viewModel.SelectedDate = Today.AddDays(offsetDays);
        await viewModel.PendingLoad;

        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Equal(Today, viewModel.SelectedDate);
        Assert.Equal(Today, service.LoadCalls[^1]);
    }

    [Fact]
    public async Task GoToToday_WhenTodayIsAlreadySelected_DoesNoWork()
    {
        var coordinator = new FakeOfficialDataRefreshCoordinator();
        var (viewModel, service, _) = await CreateAsync(coordinator: coordinator);
        await viewModel.PendingAutoCheck;
        var loads = service.LoadCalls.Count;
        var checks = coordinator.DateCheckRequests.Count;

        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Equal(Today, viewModel.SelectedDate);
        Assert.Equal(loads, service.LoadCalls.Count);
        Assert.Equal(checks, coordinator.DateCheckRequests.Count);
        Assert.Empty(service.CheckOnlineCalls);
    }

    [Fact]
    public async Task GoToToday_UsesTheEasternDate_NotUtc()
    {
        // 2026-10-17 02:30 UTC is still 2026-10-16 22:30 in Eastern time.
        var (viewModel, _, _) = await CreateAsync(new DateTimeOffset(2026, 10, 17, 2, 30, 0, TimeSpan.Zero));
        viewModel.SelectedDate = new DateOnly(2026, 10, 17);
        await viewModel.PendingLoad;

        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Equal(Today, viewModel.SelectedDate);
    }

    [Fact]
    public async Task GoToToday_ReadsTheClockAtClickTime_AcrossMidnight()
    {
        var (viewModel, _, clock) = await CreateAsync(new DateTimeOffset(2026, 10, 17, 3, 50, 0, TimeSpan.Zero)); // 23:50 Eastern, Oct 16
        Assert.Equal(Today, viewModel.SelectedDate);
        Assert.True(viewModel.DateNavigation.IsToday);

        clock.UtcNow = new DateTimeOffset(2026, 10, 17, 4, 10, 0, TimeSpan.Zero); // 00:10 Eastern, Oct 17
        Assert.Equal("Yesterday", viewModel.DateNavigation.RelativeText); // not a startup-cached "today".

        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Equal(new DateOnly(2026, 10, 17), viewModel.SelectedDate);
        Assert.True(viewModel.DateNavigation.IsToday);
    }

    [Theory]
    [InlineData(2026, 3, 8, 6, 30, 2026, 3, 8)]    // spring forward: 01:30 EST → still Mar 8
    [InlineData(2026, 3, 8, 4, 59, 2026, 3, 7)]    // 23:59 EST Mar 7
    [InlineData(2026, 11, 1, 5, 30, 2026, 11, 1)]  // fall back: 01:30 EDT (first pass) Nov 1
    [InlineData(2026, 11, 1, 3, 59, 2026, 10, 31)] // 23:59 EDT Oct 31
    [InlineData(2027, 1, 1, 4, 30, 2026, 12, 31)]  // 23:30 EST Dec 31 — year rollover not yet reached
    [InlineData(2027, 1, 1, 5, 0, 2027, 1, 1)]     // 00:00 EST Jan 1
    [InlineData(2026, 11, 1, 3, 30, 2026, 10, 31)] // month rollover not yet reached in Eastern
    public async Task GoToToday_AcrossDaylightSavingAndRollovers_UsesEasternWallClock(int uy, int um, int ud, int uh, int umin, int ey, int em, int ed)
    {
        var (viewModel, _, _) = await CreateAsync(new DateTimeOffset(uy, um, ud, uh, umin, 0, TimeSpan.Zero));
        viewModel.SelectedDate = new DateOnly(2020, 1, 1);
        await viewModel.PendingLoad;

        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Equal(new DateOnly(ey, em, ed), viewModel.SelectedDate);
    }

    // ---------------------------------------------------------------- date header presentation

    [Theory]
    [InlineData(0, "Today", true)]
    [InlineData(-1, "Yesterday", false)]
    [InlineData(1, "Tomorrow", false)]
    [InlineData(2, null, false)]
    [InlineData(-30, null, false)]
    public void DateNavigationPresentation_LabelsRelativeDays(int offset, string? expected, bool isToday)
    {
        var presentation = AssociateDateNavigationPresentation.Build(Today.AddDays(offset), Today);

        Assert.Equal(expected, presentation.RelativeText);
        Assert.Equal(isToday, presentation.IsToday);
        Assert.StartsWith("Selected date: ", presentation.AutomationText, StringComparison.Ordinal);
        if (expected is not null)
        {
            Assert.Contains($"({expected})", presentation.AutomationText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DateNavigationPresentation_UsesTheLongUsDateFormat()
    {
        Assert.Equal("Friday, October 16, 2026", AssociateDateNavigationPresentation.Build(Today, Today).DateText);
        Assert.Equal("Thursday, December 31, 2026", AssociateDateNavigationPresentation.Build(new DateOnly(2026, 12, 31), Today).DateText);
    }

    [Fact]
    public void CalendarTodayHighlight_ShownOnlyWhenTheHostDateMatchesTheEasternDate()
    {
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

        // 10:00 Eastern Oct 16 = 07:00 Pacific Oct 16 (same date) = 23:00 Tokyo Oct 16 (same date).
        Assert.True(AssociateDateNavigationPresentation.ShouldHighlightHostToday(Now, pacific, Today));
        Assert.True(AssociateDateNavigationPresentation.ShouldHighlightHostToday(Now, tokyo, Today));

        // 23:30 Eastern Oct 16 = 12:30 Tokyo Oct 17: the host's "today" differs, so no host-based marker.
        var lateEvening = new DateTimeOffset(2026, 10, 17, 3, 30, 0, TimeSpan.Zero);
        Assert.False(AssociateDateNavigationPresentation.ShouldHighlightHostToday(lateEvening, tokyo, Today));
        Assert.True(AssociateDateNavigationPresentation.ShouldHighlightHostToday(lateEvening, pacific, Today));
    }

    // ---------------------------------------------------------------- loads / retrieval

    [Fact]
    public async Task ReselectingTheSameDate_DoesNotReload()
    {
        var (viewModel, service, _) = await CreateAsync();
        var loads = service.LoadCalls.Count;

        viewModel.SelectedDate = Today;
        viewModel.SelectedDateForPicker = Today.ToDateTime(TimeOnly.MinValue);
        await viewModel.PendingLoad;

        Assert.Equal(loads, service.LoadCalls.Count);
    }

    [Fact]
    public async Task SteppingThroughNearbyDates_LeavesOnlyTheLatestAutomaticCheckPending()
    {
        var gate = new TaskCompletionSource();
        var coordinator = new FakeOfficialDataRefreshCoordinator { Gate = gate };
        var (viewModel, service, _) = await CreateAsync(coordinator: coordinator);

        viewModel.NextDayCommand.Execute(null);
        await viewModel.PendingLoad;
        viewModel.NextDayCommand.Execute(null);
        await viewModel.PendingLoad;
        viewModel.NextDayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Equal([Today, Today.AddDays(1), Today.AddDays(2)], coordinator.CancelledDateChecks);
        Assert.Empty(service.CheckOnlineCalls);
        gate.SetResult();
        await viewModel.PendingAutoCheck;
    }

    [Fact]
    public async Task ALoadFailure_DoesNotBlockFurtherNavigation()
    {
        var (viewModel, service, _) = await CreateAsync();
        var failingDate = Today.AddDays(1);
        service.WorkspaceFactory = date => date == failingDate
            ? throw new InvalidOperationException("source unavailable")
            : FakeAssociatePassdownService.DefaultWorkspace(date);

        viewModel.NextDayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.NotNull(viewModel.LoadErrorMessage);
        Assert.True(viewModel.NextDayCommand.CanExecute(null));
        Assert.True(viewModel.GoToTodayCommand.CanExecute(null));

        viewModel.NextDayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Equal(Today.AddDays(2), viewModel.SelectedDate);
        Assert.Null(viewModel.LoadErrorMessage);
        Assert.Equal(Today.AddDays(2), viewModel.Workspace!.OperatingDate);
    }

    // ---------------------------------------------------------------- draft preservation

    [Fact]
    public async Task DateArrowsAndToday_FlushUnsavedEditsToTheirOwnDate()
    {
        var (viewModel, service, _) = await CreateAsync();
        viewModel.DailyFeatureText = "Ask about the Fury 325 drop";
        viewModel.OperationalNotesText = "Gate 3 scanner slow";
        viewModel.ProjectedAttendanceText = "18000";

        viewModel.NextDayCommand.Execute(null); // debounce never elapsed — the switch must flush.
        await viewModel.PendingLoad;

        var saved = Assert.Single(service.SaveCalls, call => call.OperatingDate == Today);
        Assert.Equal("Ask about the Fury 325 drop", saved.Draft.DailyFeatureText);
        Assert.Equal(["Gate 3 scanner slow"], saved.Draft.OperationalNotes);
        Assert.Equal(18000, saved.Draft.ProjectedAttendance);

        viewModel.PreSoldsText = "250";
        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Contains(service.SaveCalls, call => call.OperatingDate == Today.AddDays(1) && call.Draft.PreSolds == 250);
        Assert.Equal("Ask about the Fury 325 drop", viewModel.DailyFeatureText);
        Assert.Equal("Gate 3 scanner slow", viewModel.OperationalNotesText);
        Assert.Equal("18000", viewModel.ProjectedAttendanceText);
    }

    // ---------------------------------------------------------------- app routes

    private static async Task<(MainViewModel Main, OperationsDashboardViewModel Dashboard, OperationsCalendarViewModel Calendar, AssociateViewModel Associate, PassdownHistoryViewModel History)> CreateShellAsync()
    {
        var clock = new FakeClock(Now);
        var dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        var calendar = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock);
        var associate = new AssociateViewModel(new FakeAssociatePassdownService(), clock, templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore());
        var notes = new OperationalNotesViewModel(new FakeOperationalNotesService(), new SystemClock());
        var history = new PassdownHistoryViewModel(
            new FakePassdownHistoryService { Summaries = [new FinalizedPassdownSummary(new DateOnly(2026, 10, 3), Now, FinalizationOrigin.Scheduled, true, 100)] },
            new FakeFinalizedPassdownPdfRetrievalService());
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        var main = new MainViewModel(dashboard, calendar, associate, notes, history, settings);
        await dashboard.PendingLoad;
        await associate.PendingLoad;
        return (main, dashboard, calendar, associate, history);
    }

    [Fact]
    public async Task DashboardOpenPassdown_StillOpensToday_AfterArrowNavigation()
    {
        var shell = await CreateShellAsync();
        shell.Associate.PreviousDayCommand.Execute(null);
        shell.Associate.PreviousDayCommand.Execute(null);
        await shell.Associate.PendingLoad;

        shell.Dashboard.OpenPassdownCommand.Execute(null);
        await shell.Associate.PendingLoad;

        Assert.Equal(Today, shell.Associate.SelectedDate);
        Assert.Equal(AppSection.AssociatePassdown, shell.Main.SelectedSection);
    }

    [Fact]
    public async Task CalendarRoute_PreservesItsSelectedDate_AndArrowsContinueFromIt()
    {
        var shell = await CreateShellAsync();
        var routed = new DateOnly(2026, 7, 4);
        await shell.Calendar.PendingLoad;
        shell.Calendar.SelectDateCommand.Execute(routed);
        await shell.Calendar.PendingLoad;
        shell.Calendar.OpenAssociateCommand.Execute(null);
        await shell.Associate.PendingLoad;

        Assert.Equal(routed, shell.Associate.SelectedDate);

        shell.Associate.NextDayCommand.Execute(null);
        await shell.Associate.PendingLoad;
        Assert.Equal(routed.AddDays(1), shell.Associate.SelectedDate);
    }

    [Fact]
    public async Task HistoryRoute_IsUnaffectedByAssociateDateNavigation()
    {
        var shell = await CreateShellAsync();
        var historyDate = new DateOnly(2026, 10, 3);
        await shell.Calendar.PendingLoad;
        shell.Calendar.SelectDateCommand.Execute(historyDate);
        await shell.Calendar.PendingLoad;
        shell.Calendar.OpenHistoryCommand.Execute(null);
        await shell.History.PendingSelectByDate;
        var selectedBefore = shell.History.SelectedItem;

        shell.Associate.GoToTodayCommand.Execute(null);
        shell.Associate.PreviousDayCommand.Execute(null);
        await shell.Associate.PendingLoad;

        Assert.Equal(AppSection.History, shell.Main.SelectedSection);
        Assert.Equal(historyDate, shell.History.SelectedItem!.PassdownDate);
        Assert.Same(selectedBefore, shell.History.SelectedItem);
    }

    // ---------------------------------------------------------------- toolbar

    [Fact]
    public async Task AssociateView_Toolbar_ExposesEveryActionByName_AndNoDiagnostics()
    {
        var (viewModel, _, _) = await CreateAsync();
        var view = new AssociateView { DataContext = viewModel };

        var names = view.GetLogicalDescendants().OfType<Control>()
            .Select(AutomationProperties.GetName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        foreach (var expected in new[] { "Previous day", "Operating date", "Next day", "Today", "Check this date online", "Create PDF", "Copy for Teams", "More passdown actions" })
        {
            Assert.Single(names, name => name == expected);
        }

        Assert.DoesNotContain("Export Diagnostics", names);
    }

    [Fact]
    public void AssociateViewMarkup_KeepsCopyMenu_MovesPdfStyleIntoMore_AndDropsDiagnostics()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));

        // Copy: the same three entries, same command and parameters (Phase 18F).
        Assert.Contains("AssociateTeamsCopyKind.OperationsSummary", markup, StringComparison.Ordinal);
        Assert.Contains("AssociateTeamsCopyKind.TodaysPricing", markup, StringComparison.Ordinal);
        Assert.Contains("AssociateTeamsCopyKind.OperationsAndPricing", markup, StringComparison.Ordinal);
        Assert.Equal(3, CountOf(markup, "Command=\"{Binding CopyForTeamsCommand}\""));

        // PDF: one Create PDF entry point; all three styles reachable under More.
        Assert.Equal(1, CountOf(markup, "Command=\"{Binding CreatePdfCommand}\""));
        Assert.Contains("AssociatePassdownTemplate.Classic", markup, StringComparison.Ordinal);
        Assert.Contains("AssociatePassdownTemplate.Compact", markup, StringComparison.Ordinal);
        Assert.Contains("AssociatePassdownTemplate.Modern", markup, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedTemplateOption.Label}", markup, StringComparison.Ordinal);

        Assert.DoesNotContain("ExportDiagnosticsCommand", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticsExportMessage", markup, StringComparison.Ordinal);

        // Icon-only date controls carry tooltips; the date button is described for screen readers.
        Assert.Contains("ToolTip.Tip=\"Previous day\"", markup, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"Next day\"", markup, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"Choose date\"", markup, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding DateNavigation.AutomationText}\"", markup, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    [InlineData(AssociatePassdownTemplate.Classic)]
    public async Task MorePdfStyle_SelectsTheTemplate_SavesThePreference_AndChecksExactlyOneItem(AssociatePassdownTemplate template)
    {
        var store = new FakeAssociatePassdownTemplatePreferenceStore();
        var (viewModel, service, _) = await CreateAsync(templateStore: store);
        var savesBefore = service.SaveCalls.Count;

        viewModel.SelectTemplateCommand.Execute(template);
        await viewModel.PendingTemplatePreferenceSave;

        Assert.Equal(template, viewModel.SelectedTemplateOption.Value);
        Assert.Equal(1, new[] { viewModel.IsClassicTemplateSelected, viewModel.IsCompactTemplateSelected, viewModel.IsModernTemplateSelected }.Count(selected => selected));
        Assert.Equal(savesBefore, service.SaveCalls.Count); // a template change is never draft data.
        if (template != AssociatePassdownTemplate.Classic)
        {
            Assert.Equal(template, store.SaveCalls[^1].Template);
        }
    }

    [Fact]
    public async Task OpeningTheViewAndItsMenus_NeverChangesTheSavedTemplatePreference()
    {
        var store = new FakeAssociatePassdownTemplatePreferenceStore();
        var (viewModel, _, _) = await CreateAsync(templateStore: store);

        _ = new AssociateView { DataContext = viewModel };

        Assert.Empty(store.SaveCalls);
        Assert.True(viewModel.IsClassicTemplateSelected);
    }

    [Fact]
    public async Task MoreSeasonPriceHistory_RequestsTheOverlay_WithoutTouchingTheDraft()
    {
        var (viewModel, service, _) = await CreateAsync();
        var requests = new List<string?>();
        viewModel.PriceHistoryRequested += (_, key) => requests.Add(key);

        viewModel.OpenPriceHistoryCommand.Execute(null);

        Assert.Equal([null], requests);
        Assert.Empty(service.SaveCalls);
        Assert.Equal(Today, viewModel.SelectedDate);
    }
}
