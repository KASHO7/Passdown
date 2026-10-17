using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 19A: the Associate screen's quiet automatic date check and its reaction to newer official
/// data. The today/future/past and missing/stale/fresh decisions themselves live in (and are tested on)
/// <c>OfficialDataRefreshCoordinator</c>; these tests cover what the screen does with them — show cached
/// data first, never block or disable anything, supersede on date changes, never apply a late result to
/// the wrong date, never overwrite unsaved edits, and never autosave because official data changed.
/// </summary>
public class AssociateViewModelAutomaticCheckTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 15, 16, 0, 0, TimeSpan.Zero); // 12:00 PM EDT
    private static readonly DateOnly Today = new(2026, 10, 15);

    private sealed record Setup(AssociateViewModel ViewModel, FakeAssociatePassdownService Service, FakeOfficialDataRefreshCoordinator Coordinator, DataChangeNotifier Notifier, ManualDebounceScheduler Debounce);

    private static Setup Create(Action<FakeOfficialDataRefreshCoordinator>? configure = null)
    {
        var service = new FakeAssociatePassdownService { OfficialHours = (new TimeOnly(10, 0), new TimeOnly(20, 0)) };
        var coordinator = new FakeOfficialDataRefreshCoordinator();
        var notifier = new DataChangeNotifier();
        coordinator.OnChecked = date => notifier.Publish(new DataChange(DataChangeKind.OfficialData, date)); // what the real gate does.
        configure?.Invoke(coordinator);
        var debounce = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: debounce.Delay,
            refreshCoordinator: coordinator, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        return new Setup(viewModel, service, coordinator, notifier, debounce);
    }

    [Fact]
    public async Task OpeningToday_ShowsCachedDataFirst_ThenRequestsAQuietCheck()
    {
        var gate = new TaskCompletionSource();
        var s = Create(c => c.Gate = gate);

        await s.ViewModel.PendingLoad;

        Assert.NotNull(s.ViewModel.Workspace); // cached data is already on screen…
        Assert.Equal([Today], s.Coordinator.DateCheckRequests); // …and the check was requested after it.
        Assert.False(s.ViewModel.IsBusy); // never blocks or disables the screen.
        Assert.True(s.ViewModel.CheckOnlineCommand.CanExecute(null));
        gate.SetResult();
        await s.ViewModel.PendingAutoCheck;
    }

    [Fact]
    public async Task WhileOnline_ShowsSubtleStatus_WithoutDisablingActions()
    {
        var gate = new TaskCompletionSource();
        var s = Create(c => { c.Gate = gate; c.OutcomeFor = _ => DateCheckOutcome.Checked; });
        await s.ViewModel.PendingLoad;

        Assert.True(s.ViewModel.IsAutoCheckingDate);
        Assert.False(s.ViewModel.IsCheckingOnline);
        Assert.True(s.ViewModel.CheckOnlineCommand.CanExecute(null));

        gate.SetResult();
        await s.ViewModel.PendingAutoCheck;
        Assert.False(s.ViewModel.IsAutoCheckingDate);
    }

    [Fact]
    public async Task FreshData_NoReload_NoSave()
    {
        var s = Create(c => c.OutcomeFor = _ => DateCheckOutcome.SkippedFresh);
        await s.ViewModel.PendingLoad;
        await s.ViewModel.PendingAutoCheck;

        Assert.Single(s.Service.LoadCalls);
        Assert.Empty(s.Service.SaveCalls);
        Assert.False(s.ViewModel.IsAutoCheckingDate);
    }

    [Fact]
    public async Task CheckedData_UpdatesThePresentation_WithoutAutosaving()
    {
        var gate = new TaskCompletionSource();
        var s = Create(c => { c.Gate = gate; c.OutcomeFor = _ => DateCheckOutcome.Checked; });
        await s.ViewModel.PendingLoad;
        Assert.Equal(new TimeOnly(20, 0), s.ViewModel.Workspace!.ParkHours.Effective.Closing);

        s.Service.OfficialHours = (new TimeOnly(10, 0), new TimeOnly(22, 0)); // the check found later closing
        gate.SetResult();
        await s.ViewModel.PendingAutoCheck;
        await s.ViewModel.PendingOfficialDataReload;

        Assert.Equal(new TimeOnly(22, 0), s.ViewModel.Workspace!.ParkHours.Effective.Closing);
        Assert.Equal(2, s.Service.LoadCalls.Count);
        Assert.Empty(s.Service.SaveCalls); // official data never triggers a draft write.
        Assert.Equal(AssociateAutosaveState.Saved, s.ViewModel.AutosaveState);
    }

    [Fact]
    public async Task RapidDateNavigation_SupersedesEarlierChecks_NoFanOut()
    {
        var gate = new TaskCompletionSource();
        var s = Create(c => { c.Gate = gate; c.OutcomeFor = _ => DateCheckOutcome.Checked; });
        await s.ViewModel.PendingLoad;

        var saturday = new DateOnly(2026, 10, 17);
        foreach (var date in new[] { Today.AddDays(1), saturday, Today })
        {
            s.ViewModel.SelectedDate = date;
            await s.ViewModel.PendingLoad;
        }

        Assert.Equal([Today, Today.AddDays(1), saturday, Today], s.Coordinator.DateCheckRequests);
        Assert.Equal([Today, Today.AddDays(1), saturday], s.Coordinator.CancelledDateChecks); // only the latest is still pending.

        gate.SetResult();
        await s.ViewModel.PendingAutoCheck;
        Assert.Equal(Today, s.ViewModel.SelectedDate);
        Assert.Equal(Today, s.ViewModel.Workspace!.OperatingDate);
    }

    [Fact]
    public async Task LateResultForAnotherDate_IsNeverAppliedToTheSelectedDate()
    {
        var s = Create();
        await s.ViewModel.PendingLoad;
        s.ViewModel.SelectedDate = Today.AddDays(2);
        await s.ViewModel.PendingLoad;
        var loadsBefore = s.Service.LoadCalls.Count;

        // A check for the previously selected date finishes after the user moved on.
        s.Notifier.Publish(new DataChange(DataChangeKind.OfficialData, Today));
        await s.ViewModel.PendingOfficialDataReload;

        Assert.Equal(loadsBefore, s.Service.LoadCalls.Count);
        Assert.Equal(Today.AddDays(2), s.ViewModel.Workspace!.OperatingDate);
    }

    [Fact]
    public async Task NewerOfficialData_WhileEditing_IsDeferred_ManualValuesKept_ThenShownAfterSave()
    {
        var s = Create();
        await s.ViewModel.PendingLoad;

        s.ViewModel.ProjectedAttendanceText = "15000"; // unsaved manual value
        s.Service.OfficialHours = (new TimeOnly(10, 0), new TimeOnly(23, 0));
        s.Notifier.Publish(new DataChange(DataChangeKind.OfficialData, null)); // e.g. the four-hour check finished
        await s.ViewModel.PendingOfficialDataReload;

        Assert.Equal("15000", s.ViewModel.ProjectedAttendanceText); // never overwritten mid-edit
        Assert.Equal(new TimeOnly(20, 0), s.ViewModel.Workspace!.ParkHours.Effective.Closing);

        s.Debounce.ElapseLatest();
        await s.ViewModel.PendingAutosave;
        await s.ViewModel.PendingOfficialDataReload;

        Assert.Equal(new TimeOnly(23, 0), s.ViewModel.Workspace!.ParkHours.Effective.Closing);
        Assert.Equal("15000", s.ViewModel.ProjectedAttendanceText);
        Assert.Equal(15000, s.ViewModel.Workspace.Draft.ProjectedAttendance);
        Assert.Single(s.Service.SaveCalls); // the one user edit — nothing extra from the official-data reload.
    }

    [Fact]
    public async Task ManualGateOverride_SurvivesNewerOfficialHours()
    {
        var s = Create();
        await s.ViewModel.PendingLoad;
        s.ViewModel.AvenueOverrideText = "9:45 am";
        s.Debounce.ElapseLatest();
        await s.ViewModel.PendingAutosave;

        s.Service.OfficialHours = (new TimeOnly(11, 0), new TimeOnly(20, 0)); // official opening moved
        s.Notifier.Publish(new DataChange(DataChangeKind.OfficialData, Today));
        await s.ViewModel.PendingOfficialDataReload;

        Assert.Equal(new TimeOnly(9, 45), s.ViewModel.Workspace!.ParkHours.Effective.AvenueOpening); // user value authoritative
        Assert.Equal(new TimeOnly(10, 30), s.ViewModel.Workspace.ParkHours.Effective.NorthGateOpening); // default follows official
        Assert.Equal("9:45 AM", s.ViewModel.AvenueOverrideText);
    }

    [Fact]
    public async Task CheckOnline_StillWorks_AndSupersedesTheQuietCheck()
    {
        var gate = new TaskCompletionSource();
        var s = Create(c => { c.Gate = gate; c.OutcomeFor = _ => DateCheckOutcome.Checked; });
        await s.ViewModel.PendingLoad;

        await s.ViewModel.CheckOnlineCommand.ExecuteAsync(null);

        Assert.Equal([Today], s.Service.CheckOnlineCalls);
        Assert.Equal([Today], s.Coordinator.CancelledDateChecks);
        Assert.False(s.ViewModel.IsAutoCheckingDate);
        Assert.False(string.IsNullOrEmpty(s.ViewModel.RefreshResultMessage));
    }

    [Fact]
    public async Task WithoutACoordinator_NoAutomaticCheck_ExistingBehaviorUnchanged()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow));
        await viewModel.PendingLoad;

        Assert.Empty(service.CheckOnlineCalls);
        Assert.False(viewModel.IsAutoCheckingDate);
    }

    [Fact]
    public void ConstructorStillHasNoDirectRefreshOrchestratorDependency()
    {
        var parameterTypes = typeof(AssociateViewModel).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(typeof(IRefreshOrchestrator), parameterTypes);
        Assert.Contains(typeof(IOfficialDataRefreshCoordinator), parameterTypes);
    }
}
