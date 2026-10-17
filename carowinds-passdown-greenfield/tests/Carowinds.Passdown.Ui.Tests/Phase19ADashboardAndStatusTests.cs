using System.Globalization;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 19A: the Dashboard's reaction to local change notifications (replacing its header Refresh
/// button), the shared official-data freshness line, and the app-lifetime wiring of automatic refresh.
/// </summary>
public class Phase19ADashboardAndStatusTests
{
    private static readonly DateOnly Shown = FakeOperationsDashboardService.DefaultDate;

    private static (OperationsDashboardViewModel ViewModel, FakeOperationsDashboardService Service, DataChangeNotifier Notifier) CreateDashboard()
    {
        var service = new FakeOperationsDashboardService();
        var notifier = new DataChangeNotifier();
        var viewModel = new OperationsDashboardViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        return (viewModel, service, notifier);
    }

    // ------------------------------------------------------------------ Dashboard reacts to local changes

    [Theory]
    [InlineData(DataChangeKind.AssociateDraft)]
    [InlineData(DataChangeKind.EarlyRideTime)]
    [InlineData(DataChangeKind.OperationalNotes)]
    [InlineData(DataChangeKind.PersonalSync)]
    [InlineData(DataChangeKind.OfficialData)]
    [InlineData(DataChangeKind.FinalizedHistory)]
    [InlineData(DataChangeKind.OperatingDay)]
    public async Task EveryRelevantLocalChange_ReloadsTodaysSummary_WithoutAnyButton(DataChangeKind kind)
    {
        var (viewModel, service, notifier) = CreateDashboard();
        await viewModel.PendingLoad;

        notifier.Publish(new DataChange(kind));
        await viewModel.PendingLoad;

        Assert.Equal(2, service.GetTodayCallCount);
        Assert.Equal(0, service.CheckOnlineCallCount); // local reload only — never a network check.
    }

    [Fact]
    public async Task ChangeForAnotherDate_IsIgnored()
    {
        var (viewModel, service, notifier) = CreateDashboard();
        await viewModel.PendingLoad;

        notifier.Publish(new DataChange(DataChangeKind.AssociateDraft, Shown.AddDays(1)));
        notifier.Publish(new DataChange(DataChangeKind.OfficialData, Shown.AddDays(5)));
        await viewModel.PendingLoad;

        Assert.Equal(1, service.GetTodayCallCount);
    }

    [Fact]
    public async Task ChangeForTheShownDate_ReloadsAndShowsTheNewValue()
    {
        var (viewModel, service, notifier) = CreateDashboard();
        await viewModel.PendingLoad;

        service.SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with { ProjectedAttendance = 18000 };
        notifier.Publish(new DataChange(DataChangeKind.AssociateDraft, Shown));
        await viewModel.PendingLoad;

        Assert.Equal(18000, viewModel.Snapshot!.ProjectedAttendance);
    }

    [Fact]
    public async Task BurstOfChanges_CoalescesIntoOneFollowUpReload()
    {
        var (viewModel, service, notifier) = CreateDashboard();
        await viewModel.PendingLoad;

        var gate = new TaskCompletionSource<bool>();
        service.Gate = gate;
        for (var i = 0; i < 6; i++)
        {
            notifier.Publish(new DataChange(DataChangeKind.AssociateDraft, Shown));
        }

        var pending = viewModel.PendingLoad;
        service.Gate = null;
        gate.SetResult(true);
        await pending;

        Assert.Equal(3, service.GetTodayCallCount); // initial + the one in flight + exactly one follow-up.
    }

    [Fact]
    public async Task ChangeDuringTheInitialLoad_IsNotLost()
    {
        var service = new FakeOperationsDashboardService { Gate = new TaskCompletionSource<bool>() };
        var notifier = new DataChangeNotifier();
        var viewModel = new OperationsDashboardViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        var initial = viewModel.PendingLoad;

        notifier.Publish(new DataChange(DataChangeKind.AssociateDraft));
        var gate = service.Gate;
        service.Gate = null;
        gate.SetResult(true);
        await initial;
        await viewModel.PendingLoad;

        Assert.Equal(2, service.GetTodayCallCount);
    }

    [Fact]
    public async Task QuietReloadFailure_KeepsShowingTheLastGoodSummary()
    {
        var (viewModel, service, notifier) = CreateDashboard();
        await viewModel.PendingLoad;
        var good = viewModel.Snapshot;

        service.Exception = new InvalidOperationException("database is locked");
        notifier.Publish(new DataChange(DataChangeKind.AssociateDraft));
        await viewModel.PendingLoad;

        Assert.Same(good, viewModel.Snapshot);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task FailedInitialLoad_RecoversOnTheNextLocalChange_OrTryAgain()
    {
        var service = new FakeOperationsDashboardService { Exception = new InvalidOperationException("transient") };
        var notifier = new DataChangeNotifier();
        var viewModel = new OperationsDashboardViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        await viewModel.PendingLoad;
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.True(viewModel.ReloadCommand.CanExecute(null)); // the error state's "Try again"

        service.Exception = null;
        notifier.Publish(new DataChange(DataChangeKind.PersonalSync));
        await viewModel.PendingLoad;

        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.HasSnapshot);
    }

    [Fact]
    public void DashboardConstructor_StillHasNoRefreshPdfOrHttpDependency()
    {
        var names = typeof(OperationsDashboardViewModel).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Refresh", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Http", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ freshness line (checked, never "updated")

    private static readonly DateTimeOffset At1241PmEastern = new(2026, 10, 15, 16, 41, 0, TimeSpan.Zero);
    private static readonly OfficialSourceIssue FastLaneIssue = new("dated-fast-lane.official", SourceCategory.DatedFastLane);
    private static readonly OfficialSourceIssue PromotionsIssue = new("promotions.official", SourceCategory.Promotions);

    [Fact]
    public void Status_NotCheckedYet_HasNoTimestamp()
    {
        var p = OfficialDataStatusPresentation.Build(OfficialDataStatus.NotCheckedYet, At1241PmEastern);

        Assert.Equal("Official data not checked yet", p.Text);
        Assert.DoesNotMatch(@"\d", p.Text);
        Assert.False(p.HasIssue);
    }

    [Fact]
    public void Status_Checking()
    {
        var p = OfficialDataStatusPresentation.Build(OfficialDataStatus.NotCheckedYet with { IsChecking = true }, At1241PmEastern);

        Assert.Equal("Checking official data…", p.Text);
        Assert.True(p.IsChecking);
    }

    [Fact]
    public void Status_Checked_SaysCheckedNotUpdated_InEasternUsTime()
    {
        var status = new OfficialDataStatus(OfficialDataCheckState.Checked, At1241PmEastern, At1241PmEastern, At1241PmEastern, []);

        var p = OfficialDataStatusPresentation.Build(status, At1241PmEastern.AddMinutes(30));

        Assert.Equal("Official data checked 12:41 PM", p.Text);
        Assert.DoesNotContain("updated", p.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(p.Text, p.AutomationText);
    }

    [Fact]
    public void Status_CheckedOnAnEarlierDay_ShowsTheDate()
    {
        var status = new OfficialDataStatus(OfficialDataCheckState.Checked, At1241PmEastern, At1241PmEastern, At1241PmEastern, []);

        var p = OfficialDataStatusPresentation.Build(status, At1241PmEastern.AddDays(1));

        Assert.Equal("Official data checked Oct 15, 12:41 PM", p.Text);
    }

    [Fact]
    public void Status_PartialFailure_IsConcise_WithSourceNamesInDetailOnly()
    {
        var status = new OfficialDataStatus(OfficialDataCheckState.PartiallyFailed, At1241PmEastern, null, At1241PmEastern, [FastLaneIssue, PromotionsIssue]);

        var p = OfficialDataStatusPresentation.Build(status, At1241PmEastern);

        Assert.Equal("Some official data couldn't be updated · Last checked 12:41 PM", p.Text);
        Assert.Equal("Not updated: Fast Lane, Promotions", p.DetailText);
        Assert.True(p.HasIssue);
        Assert.Contains("Not updated: Fast Lane, Promotions", p.AutomationText);
        Assert.DoesNotContain("http", p.AutomationText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_FullFailure_NeverClaimsSuccess()
    {
        var status = new OfficialDataStatus(OfficialDataCheckState.Failed, At1241PmEastern, null, At1241PmEastern, [FastLaneIssue]);

        var p = OfficialDataStatusPresentation.Build(status, At1241PmEastern);

        Assert.Equal("Official data couldn't be updated · Last checked 12:41 PM", p.Text);
        Assert.True(p.HasIssue);
    }

    [Fact]
    public void Status_TimeFormat_IsUsEnglish_RegardlessOfMachineCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var status = new OfficialDataStatus(OfficialDataCheckState.Checked, At1241PmEastern, At1241PmEastern, At1241PmEastern, []);

            Assert.Equal("Official data checked 12:41 PM", OfficialDataStatusPresentation.Build(status, At1241PmEastern).Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void StatusViewModel_FollowsTheCoordinator_OnTheUiThread()
    {
        var coordinator = new FakeOfficialDataRefreshCoordinator();
        var posted = new List<Action>();
        var viewModel = new OfficialDataStatusViewModel(coordinator, new FakeClock(At1241PmEastern), new RecordingDispatcher(posted));

        coordinator.SetStatus(OfficialDataStatus.NotCheckedYet with { IsChecking = true });
        Assert.Equal("Official data not checked yet", viewModel.Presentation.Text); // not applied off the UI thread

        posted.Single()();
        Assert.Equal("Checking official data…", viewModel.Presentation.Text);
    }

    private sealed class RecordingDispatcher(List<Action> posted) : IUiDispatcher
    {
        public void Post(Action action) => posted.Add(action);
    }

    [Fact]
    public async Task AssociateView_ShowsTheSharedStatusLine_AndAQuietAutoCheckNote()
    {
        var coordinator = new FakeOfficialDataRefreshCoordinator();
        var clock = new FakeClock(At1241PmEastern);
        coordinator.SetStatus(new OfficialDataStatus(OfficialDataCheckState.Checked, At1241PmEastern, At1241PmEastern, At1241PmEastern, []));
        var status = new OfficialDataStatusViewModel(coordinator, clock, new ImmediateUiDispatcher());
        var viewModel = new AssociateViewModel(new FakeAssociatePassdownService(), clock, officialDataStatus: status);
        await viewModel.PendingLoad;

        var view = new AssociateView { DataContext = viewModel };
        var texts = view.GetLogicalDescendants().OfType<TextBlock>().ToList();

        // Structure through the real XAML loader; bound values are asserted on the view models (this
        // assembly's headless setup evaluates bindings thread-dependently).
        Assert.Single(texts, t => t.Name == "AssociateOfficialDataStatusText");
        var note = texts.Single(t => t.Name == "AutoCheckStatusText");
        Assert.Equal("Checking online for this date…", note.Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(note));
        Assert.Same(status, viewModel.OfficialDataStatus);
        Assert.Equal("Official data checked 12:41 PM", viewModel.OfficialDataStatus!.Presentation.Text);

        var markup = File.ReadAllText(FindRepoFile("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));
        Assert.Contains("Text=\"{Binding OfficialDataStatus.Presentation.Text}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding OfficialDataStatus.Presentation.AutomationText}\"", markup);
        Assert.Contains("IsVisible=\"{Binding IsAutoCheckingDate}\"", markup);
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' above {AppContext.BaseDirectory}.");
    }

    // ------------------------------------------------------------------ lifetime / composition (spec §34/§35)

    [Fact]
    public void Composition_OneCoordinator_OneGate_OneNotifier_OneSharedStatus()
    {
        var provider = App.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IOfficialDataRefreshCoordinator>(), provider.GetRequiredService<IOfficialDataRefreshCoordinator>());
        Assert.Same(provider.GetRequiredService<IOfficialRefreshGate>(), provider.GetRequiredService<IOfficialRefreshGate>());
        Assert.Same(provider.GetRequiredService<IDataChangeNotifier>(), provider.GetRequiredService<IDataChangeNotifier>());
        Assert.Same(provider.GetRequiredService<OfficialDataStatusViewModel>(), provider.GetRequiredService<OfficialDataStatusViewModel>());
    }

    [Fact]
    public void Composition_DashboardAndAssociateShareTheStatus_AndResolvingNeverStartsRefresh()
    {
        var provider = App.BuildServiceProvider();
        var main = provider.GetRequiredService<MainViewModel>();
        var coordinator = (OfficialDataRefreshCoordinator)provider.GetRequiredService<IOfficialDataRefreshCoordinator>();

        Assert.NotNull(main.Dashboard.OfficialDataStatus);
        Assert.Same(main.Dashboard.OfficialDataStatus, main.Associate.OfficialDataStatus);
        Assert.Null(coordinator.LoopTask); // started only by the interactive desktop lifetime.
    }

    [Fact]
    public void Navigation_BetweenSections_CreatesNoNewRefreshWork()
    {
        var provider = App.BuildServiceProvider();
        var main = provider.GetRequiredService<MainViewModel>();
        var coordinator = (OfficialDataRefreshCoordinator)provider.GetRequiredService<IOfficialDataRefreshCoordinator>();
        var dashboard = main.Dashboard;
        var associate = main.Associate;

        main.ShowAssociateSectionCommand.Execute(null);
        main.ShowOperationsCalendarSectionCommand.Execute(null);
        main.ShowDashboardSectionCommand.Execute(null);

        Assert.Same(dashboard, main.Dashboard);
        Assert.Same(associate, main.Associate);
        Assert.Null(coordinator.LoopTask);
    }

    [Fact]
    public void HeadlessScheduledRefreshDependencies_NeverStartTheInteractiveTimer()
    {
        // The --scheduled-refresh host resolves only these (ScheduledRefreshHost) from the same composition root.
        var provider = App.BuildServiceProvider();
        _ = provider.GetRequiredService<IScheduledRefreshRunner>();
        _ = provider.GetRequiredService<IScheduledPassdownFinalizer>();

        var coordinator = (OfficialDataRefreshCoordinator)provider.GetRequiredService<IOfficialDataRefreshCoordinator>();
        Assert.Null(coordinator.LoopTask);
    }
}
