using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 21C: show-schedule presentation (shared by Dashboard and Operations Calendar), the Dashboard's
/// "Today's Shows", and the Associate Shows section's selection, autosave, date isolation and offline behavior.
/// </summary>
public class Phase21CShowsUiTests
{
    /// <summary>2026-08-21 11:00 AM Eastern.</summary>
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 21);

    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    private static readonly ScheduledShow Parade = new("SHOW-030-00174", "Splash Parade", null, [T(12), T(17)]);
    private static readonly ScheduledShow Bubbles = new("SHOW-030-00178", "Gazillion Bubble Show", "Carowinds Theatre", [T(13, 15), T(14, 45)]);
    private static readonly ScheduledShow Trivia = new("SHOW-030-00182", "Uncle Sam's American Trivia", "Plaza", [T(10, 30)]);

    private static ShowScheduleView Scheduled(DateOnly date, params ScheduledShow[] shows) =>
        new(date, ShowScheduleState.Scheduled, shows, false, FixedNow.AddHours(-1), FixedNow.AddHours(-1));

    // ---- Presentation ------------------------------------------------------------------------------

    [Theory]
    [InlineData(ShowScheduleState.NoShows, "No shows scheduled today.")]
    [InlineData(ShowScheduleState.NotPublished, "Today's show schedule is not published.")]
    [InlineData(ShowScheduleState.CouldNotCheck, "Couldn't check today's shows.")]
    [InlineData(ShowScheduleState.NotChecked, "Today's shows haven't been checked yet.")]
    [InlineData(ShowScheduleState.ParkClosed, "No shows — the park is closed today.")]
    public void EmptyStates_AreDistinct_AndOnlyAConfirmedEmptyScheduleSaysNoShows(ShowScheduleState state, string expected)
    {
        var presentation = ShowSchedulePresentation.Build(new ShowScheduleView(Today, state, [], false, null, null), FixedNow, isDashboard: true);

        Assert.Equal("Today's Shows", presentation.Heading);
        Assert.Equal(expected, presentation.StatusText);
        Assert.False(presentation.HasRows);
    }

    [Fact]
    public void Today_OrdersByNextPerformance_AndMovesFinishedShowsToStartedEarlier()
    {
        var presentation = ShowSchedulePresentation.Build(Scheduled(Today, Parade, Bubbles, Trivia), FixedNow, isDashboard: true);

        Assert.Equal(["Splash Parade", "Gazillion Bubble Show"], presentation.Rows.Select(r => r.Name));
        Assert.Equal("12:00 PM · 5:00 PM", presentation.Rows[0].TimesText);
        Assert.Null(presentation.Rows[0].EarlierText);
        Assert.Equal("Started earlier today: Uncle Sam's American Trivia", presentation.StartedEarlierText);
    }

    [Fact]
    public void Today_PastPerformances_AreNeverListedAsUpcoming()
    {
        var presentation = ShowSchedulePresentation.Build(Scheduled(Today, Parade), FixedNow.AddHours(2), isDashboard: true); // 1:00 PM

        var row = Assert.Single(presentation.Rows);
        Assert.Equal("5:00 PM", row.TimesText);
        Assert.Equal("+1 earlier", row.EarlierText);
    }

    [Fact]
    public void Today_AllStarted_SaysSo_NeverClaimsAShowIsStillRunning()
    {
        var presentation = ShowSchedulePresentation.Build(Scheduled(Today, Trivia), FixedNow, isDashboard: true);

        Assert.False(presentation.HasRows);
        Assert.Equal("All of today's scheduled performances have started.", presentation.StatusText);
    }

    [Fact]
    public void OtherDate_ListsEveryPerformance_UnderADatedHeading()
    {
        var presentation = ShowSchedulePresentation.Build(Scheduled(Today.AddDays(1), Trivia, Parade), FixedNow);

        Assert.Equal("Shows — August 22", presentation.Heading);
        Assert.Equal(["Uncle Sam's American Trivia", "Splash Parade"], presentation.Rows.Select(r => r.Name));
        Assert.Equal("Plaza", presentation.Rows[0].Venue);
        Assert.False(presentation.Rows[1].HasVenue);
    }

    [Fact]
    public void Freshness_ShowsCheckedTime_AndAFailedLatestCheck()
    {
        var view = Scheduled(Today, Parade) with { LastCheckFailed = true };

        var presentation = ShowSchedulePresentation.Build(view, FixedNow);

        Assert.Equal("Official schedule checked 10:00 AM ET · latest check failed", presentation.FreshnessText);
        Assert.True(presentation.IsProblem);
    }

    [Fact]
    public void PastDateWithoutRecord_SaysHistoricalScheduleIsNotAvailable()
    {
        var view = new ShowScheduleView(Today.AddDays(-3), ShowScheduleState.HistoricalNotAvailable, [], false, null, null);

        Assert.Equal("Show schedule not available for this past date.", ShowSchedulePresentation.Build(view, FixedNow).StatusText);
    }

    // ---- Dashboard ---------------------------------------------------------------------------------

    [Fact]
    public async Task Dashboard_ShowsTodaysSchedule_AndUpdatesLiveOnOfficialDataChange()
    {
        var service = new FakeOperationsDashboardService();
        var schedule = new ShowScheduleView(FakeOperationsDashboardService.DefaultDate, ShowScheduleState.NotChecked, [], false, null, null);
        service.SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with { TodayShows = schedule };
        var notifier = new DataChangeNotifier();
        var viewModel = new OperationsDashboardViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        await viewModel.PendingLoad;
        Assert.Equal("Today's Shows", viewModel.ShowsPresentation!.Heading);
        Assert.False(viewModel.ShowsPresentation.HasRows);

        schedule = new ShowScheduleView(FakeOperationsDashboardService.DefaultDate, ShowScheduleState.NoShows, [], false, null, null);
        notifier.Publish(new DataChange(DataChangeKind.OfficialData));
        await viewModel.PendingLoad;

        Assert.StartsWith("No shows scheduled", viewModel.ShowsPresentation!.StatusText);
    }

    // ---- Associate ---------------------------------------------------------------------------------

    private static (AssociateViewModel ViewModel, FakeAssociatePassdownService Service, ManualDebounceScheduler Scheduler) CreateAssociate()
    {
        var service = new FakeAssociatePassdownService();
        service.ShowSchedules[Today] = Scheduled(Today, Trivia, Parade, Bubbles);
        var scheduler = new ManualDebounceScheduler();
        return (new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay), service, scheduler);
    }

    private static async Task SaveAsync(AssociateViewModel viewModel, ManualDebounceScheduler scheduler)
    {
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
    }

    [Fact]
    public async Task Associate_ListsTheDatesShows_NoneSelected_SectionOffByDefault()
    {
        var (viewModel, _, _) = CreateAssociate();
        await viewModel.PendingLoad;

        Assert.False(viewModel.IncludeShows);
        Assert.Equal(3, viewModel.ShowRows.Count);
        Assert.All(viewModel.ShowRows, row => Assert.False(row.IsSelected));
        Assert.Equal("Official schedule checked 10:00 AM ET", viewModel.ShowsFreshnessText);
    }

    [Fact]
    public async Task Associate_SelectingAShow_AutosavesItIntoThisDatesDraft()
    {
        var (viewModel, service, scheduler) = CreateAssociate();
        await viewModel.PendingLoad;

        viewModel.IncludeShows = true;
        viewModel.ShowRows.Single(r => r.Name == "Splash Parade").IsSelected = true;
        await SaveAsync(viewModel, scheduler);

        var saved = service.SaveCalls.Last().Draft.Shows!;
        Assert.True(saved.IncludeInPassdown);
        Assert.Equal(["SHOW-030-00174"], saved.SelectedShows.Select(s => s.ShowId));
        Assert.Equal(["Splash Parade"], viewModel.Workspace!.Shows.PrintableRows.Select(r => r.Name));
    }

    [Fact]
    public async Task Associate_SelectionsSurviveDateSwitch_AndNeverAppearOnAnotherDate()
    {
        var (viewModel, service, scheduler) = CreateAssociate();
        service.ShowSchedules[Today.AddDays(1)] = Scheduled(Today.AddDays(1), Parade);
        await viewModel.PendingLoad;

        viewModel.IncludeShows = true;
        viewModel.ShowRows.Single(r => r.Name == "Gazillion Bubble Show").IsSelected = true;
        await SaveAsync(viewModel, scheduler);

        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;
        Assert.False(viewModel.IncludeShows);
        Assert.False(Assert.Single(viewModel.ShowRows).IsSelected);

        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;
        Assert.True(viewModel.IncludeShows);
        Assert.True(viewModel.ShowRows.Single(r => r.Name == "Gazillion Bubble Show").IsSelected);
    }

    [Fact]
    public async Task Associate_SelectAll_SelectsEveryScheduledShow_AndClearDeselectsAll()
    {
        var (viewModel, service, scheduler) = CreateAssociate();
        await viewModel.PendingLoad;
        viewModel.IncludeShows = true;
        viewModel.SelectAllShowsCommand.Execute(null);
        await SaveAsync(viewModel, scheduler);
        Assert.Equal(3, service.SaveCalls.Last().Draft.Shows!.SelectedShows.Count);

        viewModel.ClearShowsCommand.Execute(null);
        await SaveAsync(viewModel, scheduler);

        Assert.Empty(service.SaveCalls.Last().Draft.Shows!.SelectedShows);
    }

    [Fact]
    public async Task Associate_RemovedShow_StaysListedWithAReason_AndNewShowsAreNotSelected()
    {
        var (viewModel, service, scheduler) = CreateAssociate();
        await viewModel.PendingLoad;
        viewModel.IncludeShows = true;
        viewModel.ShowRows.Single(r => r.Name == "Uncle Sam's American Trivia").IsSelected = true;
        await SaveAsync(viewModel, scheduler);

        // The official schedule changes: Trivia removed, a new show published.
        var newShow = new ScheduledShow("SHOW-030-00999", "New Show", null, [T(18)]);
        service.ShowSchedules[Today] = Scheduled(Today, Parade, newShow);
        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;
        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;

        var removed = viewModel.ShowRows.Single(r => r.Name == "Uncle Sam's American Trivia");
        Assert.True(removed.IsSelected);
        Assert.Equal(AssociateShowRowState.NoLongerListed, removed.Row.State);
        Assert.True(removed.HasStateNote);
        Assert.False(viewModel.ShowRows.Single(r => r.Name == "New Show").IsSelected);
        Assert.Empty(viewModel.Workspace!.Shows.PrintableRows);
    }

    [Fact]
    public async Task Associate_ScheduleUnavailable_DoesNotBlockEditing_AndExplainsWhy()
    {
        var service = new FakeAssociatePassdownService();
        service.ShowSchedules[Today] = new ShowScheduleView(Today, ShowScheduleState.CouldNotCheck, [], true, null, FixedNow);
        var scheduler = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay);
        await viewModel.PendingLoad;

        Assert.False(viewModel.HasShowRows);
        Assert.Equal("Couldn't check today's shows.", viewModel.ShowsStatusText);

        viewModel.ProjectedAttendanceText = "9000";
        await SaveAsync(viewModel, scheduler);
        Assert.Equal(9000, service.SaveCalls.Last().Draft.ProjectedAttendance);
        Assert.Null(service.SaveCalls.Last().Draft.Shows);
    }
}
