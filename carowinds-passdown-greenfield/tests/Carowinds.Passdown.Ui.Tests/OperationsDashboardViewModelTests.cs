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

    [Fact]
    public async Task OpenPassdownCommand_RaisesOpenAssociateRequested()
    {
        var service = new FakeOperationsDashboardService();
        var viewModel = new OperationsDashboardViewModel(service);
        await viewModel.PendingLoad;

        var raised = false;
        viewModel.OpenAssociateRequested += (_, _) => raised = true;
        viewModel.OpenPassdownCommand.Execute(null);

        Assert.True(raised);
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
}
