using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.HistoricalBackfill;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 20C: the Operations Calendar follows arriving historical (and other local) data without a
/// restart or polling, promotes the viewed month for the backfill, and shows the restrained historical
/// status line — kept separate from the operational freshness line.
/// </summary>
public sealed class Phase20CHistoricalCalendarTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    private static FakeClock Clock() => new(new DateTimeOffset(2026, 10, 16, 16, 0, 0, TimeSpan.Zero));

    private sealed class FakeBackfill : IHistoricalBackfillService
    {
        public HistoricalBackfillStatus CurrentStatus { get; private set; } = HistoricalBackfillStatus.NotStarted(2026);

        public List<DateOnly> PrioritizedMonths { get; } = [];

        public event EventHandler<HistoricalBackfillStatus>? StatusChanged;

        public void Raise(HistoricalBackfillStatus status)
        {
            CurrentStatus = status;
            StatusChanged?.Invoke(this, status);
        }

        public void Start()
        {
        }

        public Task StopAsync() => Task.CompletedTask;

        public void PrioritizeMonth(DateOnly month) => PrioritizedMonths.Add(month);
    }

    private static async Task<(OperationsCalendarViewModel Vm, FakeOperationsCalendarService Service, DataChangeNotifier Notifier, FakeBackfill Backfill)> CreateAsync()
    {
        var service = new FakeOperationsCalendarService();
        var notifier = new DataChangeNotifier();
        var backfill = new FakeBackfill();
        var vm = new OperationsCalendarViewModel(service, Clock(), logger: null, notifier, backfill, new ImmediateUiDispatcher());
        await vm.PendingLoad;
        return (vm, service, notifier, backfill);
    }

    [Fact]
    public async Task HistoricalDataArrivingForTheViewedMonth_ReloadsItQuietly_KeepingMonthAndSelection()
    {
        var (vm, service, notifier, _) = await CreateAsync();
        vm.PreviousMonthCommand.Execute(null); // September
        await vm.PendingLoad;
        var loadsBefore = service.LoadCalls.Count;

        notifier.Publish(new DataChange(DataChangeKind.HistoricalOfficialData, new DateOnly(2026, 9, 12)));
        await vm.PendingLoad;

        Assert.Equal(loadsBefore + 1, service.LoadCalls.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), vm.DisplayedMonth);
        Assert.Equal(new DateOnly(2026, 9, 16), vm.SelectedDate);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task HistoricalDataForAnotherMonth_DoesNotReload()
    {
        var (vm, service, notifier, _) = await CreateAsync();
        var loadsBefore = service.LoadCalls.Count;

        notifier.Publish(new DataChange(DataChangeKind.HistoricalOfficialData, new DateOnly(2026, 4, 12)));
        await vm.PendingLoad;

        Assert.Equal(loadsBefore, service.LoadCalls.Count);
    }

    [Theory]
    [InlineData(DataChangeKind.OfficialData)]
    [InlineData(DataChangeKind.AssociateDraft)]
    [InlineData(DataChangeKind.OperationalNotes)]
    [InlineData(DataChangeKind.PersonalSync)]
    public async Task OtherLocalChanges_AlsoRefreshTheCalendar_NoNetwork(DataChangeKind kind)
    {
        var (vm, service, notifier, _) = await CreateAsync();
        var loadsBefore = service.LoadCalls.Count;

        notifier.Publish(new DataChange(kind));
        await vm.PendingLoad;

        Assert.Equal(loadsBefore + 1, service.LoadCalls.Count);
    }

    [Fact]
    public async Task BurstOfChangesDuringAReload_CoalescesIntoOneFollowUp()
    {
        var (vm, service, notifier, _) = await CreateAsync();
        var loadsBefore = service.LoadCalls.Count;
        service.Gate = new TaskCompletionSource<bool>();

        for (var i = 0; i < 5; i++)
        {
            notifier.Publish(new DataChange(DataChangeKind.HistoricalOfficialData, Today.AddDays(-1)));
        }

        service.Gate.SetResult(true);
        await vm.PendingLoad;
        await vm.PendingLoad;

        Assert.Equal(loadsBefore + 2, service.LoadCalls.Count);
    }

    [Fact]
    public async Task NavigatingMonths_PromotesTheViewedMonthForTheBackfill()
    {
        var (vm, _, _, backfill) = await CreateAsync();

        vm.PreviousMonthCommand.Execute(null);
        vm.PreviousMonthCommand.Execute(null);
        await vm.PendingLoad;

        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1)], backfill.PrioritizedMonths);
    }

    [Fact]
    public async Task HistoryStatus_FollowsTheBackfill_AndIsHiddenUntilItHasSomethingToSay()
    {
        var (vm, _, _, backfill) = await CreateAsync();
        Assert.False(vm.HasHistoryStatus);

        backfill.Raise(new HistoricalBackfillStatus(2026, HistoricalBackfillState.Running, 288, 100, SessionQueued: 127));
        Assert.Equal("Loading this year's history…", vm.HistoryStatus.Text);

        backfill.Raise(new HistoricalBackfillStatus(2026, HistoricalBackfillState.Running, 288, 184, SessionQueued: 127, SessionChecked: 84));
        Assert.Equal("Historical data: 84 of 127 dates checked", vm.HistoryStatus.Text);
        Assert.True(vm.HasHistoryStatus);

        backfill.Raise(new HistoricalBackfillStatus(2026, HistoricalBackfillState.UpToDate, 288, 288));
        Assert.Equal("Historical data up to date", vm.HistoryStatus.Text);
    }

    [Fact]
    public void Presentation_PartialStates_ExplainWhyWithoutAlarm()
    {
        var retention = HistoricalBackfillStatusPresentation.Build(new HistoricalBackfillStatus(
            2026, HistoricalBackfillState.PartiallyAvailable, 288, 205, OldestNotRetainedDate: new DateOnly(2026, 3, 24)));
        Assert.Equal("Historical data partially available", retention.Text);
        Assert.Equal("Official park hours on or before Mar 24 are no longer published.", retention.DetailText);

        var offline = HistoricalBackfillStatusPresentation.Build(new HistoricalBackfillStatus(
            2026, HistoricalBackfillState.PartiallyAvailable, 288, 0, SessionQueued: 288, SessionFailed: 3));
        Assert.Equal("Some dates couldn't be checked; they'll be tried again the next time the app starts.", offline.DetailText);

        Assert.Null(HistoricalBackfillStatusPresentation.Build(HistoricalBackfillStatus.NotStarted(2026)).Text);
        Assert.Null(HistoricalBackfillStatusPresentation.Build(new HistoricalBackfillStatus(2027, HistoricalBackfillState.NotApplicable)).Text);

        // Never the operational wording.
        Assert.DoesNotContain("Official data", retention.Text);
        Assert.DoesNotContain("couldn't be updated", offline.Text);
    }

    [Fact]
    public void Markup_HistoryStatusIsAMutedLineInTheCalendarHeader()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml"));

        Assert.Matches(new Regex(@"<TextBlock Text=""\{Binding HistoryStatus.Text\}"" Classes=""muted""\s+IsVisible=""\{Binding HasHistoryStatus\}"""), markup);
        Assert.Contains("ToolTip.Tip=\"{Binding HistoryStatus.DetailText}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding HistoryStatus.AutomationText}\"", markup);
        Assert.True(markup.IndexOf("HistoryStatus.Text", StringComparison.Ordinal) < markup.IndexOf("<ScrollViewer", StringComparison.Ordinal)); // header, not a Dashboard-style card
    }

    /// <summary>Spec §34: the 6 AM scheduled task stays operational-only; the backfill starts after (and stops with) the interactive coordinator.</summary>
    [Fact]
    public void Composition_BackfillIsInteractiveOnly_StartedAfterTheRefreshCoordinator()
    {
        var host = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/ScheduledRefresh/ScheduledRefreshHost.cs"));
        var app = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/App.axaml.cs"));

        Assert.DoesNotContain("HistoricalBackfill", host);
        Assert.True(app.IndexOf("refreshCoordinator.Start();", StringComparison.Ordinal) < app.IndexOf("historicalBackfill.Start();", StringComparison.Ordinal));
        Assert.Contains("historicalBackfill.StopAsync()", app);
    }
}
