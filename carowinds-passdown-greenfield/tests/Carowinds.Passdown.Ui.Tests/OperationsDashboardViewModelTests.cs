using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 10A Part L/Q: covers <see cref="OperationsDashboardViewModel"/>'s load/error state and its
/// navigation events — it never owns a <c>SelectedDate</c> of its own and never navigates directly
/// (that stays <see cref="MainViewModel"/>'s job; see <see cref="MainViewModelNavigationTests"/>).
/// </summary>
public class OperationsDashboardViewModelTests
{
    [Fact]
    public async Task Constructor_LoadsTodaySnapshotAutomatically()
    {
        var service = new FakeOperationsDashboardService();

        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        Assert.True(viewModel.HasSnapshot);
        Assert.NotNull(viewModel.Presentation);
        Assert.Equal(1, service.GetTodayCallCount);
    }

    [Fact]
    public async Task LoadFailure_SetsErrorMessage_AndNoSnapshot()
    {
        var service = new FakeOperationsDashboardService { Exception = new InvalidOperationException("boom") };

        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        Assert.False(viewModel.HasSnapshot);
        Assert.Null(viewModel.Presentation);
        Assert.False(string.IsNullOrEmpty(viewModel.ErrorMessage));
    }

    [Fact]
    public async Task ReloadCommand_ReloadsFromService()
    {
        var service = new FakeOperationsDashboardService();
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        await viewModel.ReloadCommand.ExecuteAsync(null);

        Assert.Equal(2, service.GetTodayCallCount);
    }

    // Phase 10C Part G: "Open Passdown" raises its own event, distinct from "Open Notes" — so
    // MainViewModel can route each to a different Associate internal section.
    [Fact]
    public async Task OpenPassdownCommand_RaisesOpenAssociateOverviewRequested()
    {
        var service = new FakeOperationsDashboardService();
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var raised = false;
        viewModel.OpenAssociateOverviewRequested += (_, _) => raised = true;
        viewModel.OpenPassdownCommand.Execute(null);

        Assert.True(raised);
    }

    // Phase 10C Part F: "Open Notes" is now a true deep link — its own event, distinct from
    // OpenPassdownCommand's, so MainViewModel can route it to Associate's Notes section specifically
    // (Phase 10B routed both to the same OpenAssociateRequested event; no Notes section existed yet).
    [Fact]
    public async Task OpenNotesCommand_RaisesOpenAssociateNotesRequested()
    {
        var service = new FakeOperationsDashboardService();
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var raised = false;
        viewModel.OpenAssociateNotesRequested += (_, _) => raised = true;
        viewModel.OpenNotesCommand.Execute(null);

        Assert.True(raised);
    }

    // Phase 10C Part J/K: Reload is local-only (still calls the same service method, no new
    // dependency) and picks up newly finalized History metadata that appeared since the initial load —
    // e.g. after a background startup catch-up finalization completed.
    [Fact]
    public async Task ReloadCommand_PicksUpNewlyFinalizedMetadata()
    {
        var latestFinalized = new FinalizedPassdownSummary(new DateOnly(2026, 8, 25), DateTimeOffset.UtcNow, FinalizationOrigin.Recovery, true, 100);
        var service = new FakeOperationsDashboardService();
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;
        Assert.Null(viewModel.Snapshot!.LatestFinalized);

        service.SnapshotFactory = date => FakeOperationsDashboardService.DefaultSnapshot(date) with { LatestFinalized = latestFinalized };
        await viewModel.ReloadCommand.ExecuteAsync(null);

        Assert.Equal(latestFinalized, viewModel.Snapshot!.LatestFinalized);
        Assert.NotNull(viewModel.Presentation!.LatestFinalized);
    }

    [Fact]
    public async Task OpenHistoryCommand_RaisesOpenHistoryRequested()
    {
        var service = new FakeOperationsDashboardService();
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var raised = false;
        viewModel.OpenHistoryRequested += (_, _) => raised = true;
        viewModel.OpenHistoryCommand.Execute(null);

        Assert.True(raised);
    }

    // Phase 10A Part K: the dashboard load is local-only — the fake service records every call, and
    // this view model has no dependency capable of reaching the network in the first place (no
    // constructor parameter beyond IOperationsDashboardService/ILogger).
    [Fact]
    public void Constructor_HasNoRefreshOrPdfDependency()
    {
        var constructor = typeof(OperationsDashboardViewModel).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType.Name).ToArray();

        Assert.DoesNotContain(parameterTypes, name => name.Contains("Refresh", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, name => name.Contains("Pdf", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, name => name.Contains("Http", StringComparison.OrdinalIgnoreCase));
    }

    // Phase 10D Part L/N: Check Online calls the service's own CheckOnlineAsync (the same manual
    // refresh workflow Associate's own Check Online reuses) and repaints Snapshot/Presentation from
    // its result — no second local load is needed.
    [Fact]
    public async Task CheckOnlineCommand_UpdatesSnapshotAndResultMessage()
    {
        var newSnapshot = FakeOperationsDashboardService.DefaultSnapshot(FakeOperationsDashboardService.DefaultDate);
        var summary = new RefreshSummary(RefreshJobType.UserForced, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, OperationsAttempted: 1, Succeeded: 1, Failed: 0, Cancelled: 0, Results: []);
        var service = new FakeOperationsDashboardService
        {
            CheckOnlineResultFactory = () => new OperationsDashboardCheckOnlineResult(newSnapshot, summary),
        };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        await viewModel.CheckOnlineCommand.ExecuteAsync(null);

        Assert.Equal(1, service.CheckOnlineCallCount);
        Assert.Same(newSnapshot, viewModel.Snapshot);
        Assert.NotNull(viewModel.Presentation);
        Assert.False(string.IsNullOrEmpty(viewModel.CheckOnlineResultMessage));
        Assert.False(viewModel.CheckOnlineHadIssues);
        Assert.False(viewModel.IsCheckingOnline);
    }

    // Phase 10D Part N: a Check Online failure must never crash the screen or wipe the existing
    // snapshot — the prior locally-loaded values stay exactly as they were.
    [Fact]
    public async Task CheckOnlineCommand_Failure_PreservesExistingSnapshotAndSetsMessage()
    {
        var service = new FakeOperationsDashboardService { CheckOnlineException = new InvalidOperationException("boom") };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;
        var originalSnapshot = viewModel.Snapshot;

        await viewModel.CheckOnlineCommand.ExecuteAsync(null);

        Assert.Same(originalSnapshot, viewModel.Snapshot);
        Assert.True(viewModel.CheckOnlineHadIssues);
        Assert.False(string.IsNullOrEmpty(viewModel.CheckOnlineResultMessage));
        Assert.DoesNotContain("boom", viewModel.CheckOnlineResultMessage);
        Assert.False(viewModel.IsCheckingOnline);
    }

    // Phase 10D Part N: while a check is running, the command cannot be re-entered (no concurrent
    // duplicate refresh from repeated clicks).
    [Fact]
    public async Task CheckOnlineCommand_CannotRunConcurrently()
    {
        var service = new FakeOperationsDashboardService { CheckOnlineGate = new TaskCompletionSource<bool>() };
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var firstRun = viewModel.CheckOnlineCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsCheckingOnline);
        Assert.False(viewModel.CheckOnlineCommand.CanExecute(null));

        service.CheckOnlineGate!.SetResult(true);
        await firstRun;

        Assert.Equal(1, service.CheckOnlineCallCount);
    }
}
