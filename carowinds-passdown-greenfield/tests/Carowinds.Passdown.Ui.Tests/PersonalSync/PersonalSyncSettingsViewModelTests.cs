using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests.PersonalSync;

/// <summary>Phase 16B §4/§14: the Settings / Personal Sync screen's ViewModel — service-level tests only, no real Window (CLAUDE.md/spec §19 preference).</summary>
public class PersonalSyncSettingsViewModelTests
{
    [Fact]
    public async Task Constructor_LoadsSnapshotAutomatically()
    {
        var service = new FakePersonalSyncSettingsService
        {
            Status = PersonalSyncStatus.Ready,
            SuggestedRoots = ["/Users/associate/OneDrive"],
        };

        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        Assert.Equal(1, service.LoadCallCount);
        Assert.Equal(PersonalSyncStatus.Ready, viewModel.Status);
        Assert.True(viewModel.HasSuggestedRoots);
        Assert.Equal("/Users/associate/OneDrive", viewModel.FirstSuggestedRoot);
    }

    [Fact]
    public async Task TogglingPersonalSyncEnabled_CallsSetEnabledOnce()
    {
        var service = new FakePersonalSyncSettingsService();
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        viewModel.PersonalSyncEnabled = true;
        await viewModel.PendingLoad;

        Assert.Equal(1, service.SetEnabledCallCount);
        Assert.True(service.PersonalSyncEnabled);
    }

    [Fact]
    public async Task ApplyingALoadedSnapshot_DoesNotItselfTriggerASetEnabledCall()
    {
        var service = new FakePersonalSyncSettingsService { PersonalSyncEnabled = true, Status = PersonalSyncStatus.NotConfigured };

        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        Assert.True(viewModel.PersonalSyncEnabled);
        Assert.Equal(0, service.SetEnabledCallCount);
    }

    [Fact]
    public async Task ChooseFolderCommand_UserCancelsPicker_NeverCallsSetSyncRoot()
    {
        var service = new FakePersonalSyncSettingsService();
        var picker = new FakeSyncFolderPicker { NextResult = null };
        var viewModel = new PersonalSyncSettingsViewModel(service, picker, new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        await viewModel.ChooseFolderCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.CallCount);
        Assert.Equal(0, service.SetSyncRootCallCount);
    }

    [Fact]
    public async Task ChooseFolderCommand_UserPicksAFolder_CallsSetSyncRoot_AndAppliesTheResultingSnapshot()
    {
        var service = new FakePersonalSyncSettingsService
        {
            SnapshotFactory = () => new PersonalSyncSettingsSnapshot(true, "/Users/associate/OneDrive/Carowinds", PersonalSyncStatus.Ready, null, [], Guid.NewGuid()),
        };
        var picker = new FakeSyncFolderPicker { NextResult = "/Users/associate/OneDrive/Carowinds" };
        var viewModel = new PersonalSyncSettingsViewModel(service, picker, new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        await viewModel.ChooseFolderCommand.ExecuteAsync(null);

        Assert.Equal(1, service.SetSyncRootCallCount);
        Assert.Equal("/Users/associate/OneDrive/Carowinds", viewModel.SyncRootPath);
        Assert.Equal(PersonalSyncStatus.Ready, viewModel.Status);
    }

    [Fact]
    public async Task StatusPresentation_ReflectsCurrentStatus()
    {
        var service = new FakePersonalSyncSettingsService { Status = PersonalSyncStatus.FolderUnavailable };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        Assert.Equal("Folder unavailable", viewModel.StatusPresentation.Text);
    }

    [Fact]
    public async Task DeviceId_IsPopulatedFromTheService()
    {
        var fixedId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        var service = new FakePersonalSyncSettingsService { DeviceId = fixedId };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        Assert.Equal(fixedId, viewModel.DeviceId);
    }

    [Fact]
    public async Task SyncNowCommand_NotReady_CannotExecute()
    {
        var service = new FakePersonalSyncSettingsService { Status = PersonalSyncStatus.NotConfigured };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        Assert.False(viewModel.SyncNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task SyncNowCommand_Ready_RunsSyncAndDisplaysTheResultSummary()
    {
        var service = new FakePersonalSyncSettingsService { Status = PersonalSyncStatus.Ready };
        var coordinator = new FakePersonalSyncCoordinator
        {
            NextSyncNowResult = new PersonalSyncResult(PersonalSyncOutcome.Completed, ImportedCount: 3, ExportedCount: 2, UnchangedCount: 1, ConflictCount: 0, MalformedCount: 0, IoFailureCount: 0, Message: null),
        };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), coordinator);
        await viewModel.PendingLoad;

        Assert.True(viewModel.SyncNowCommand.CanExecute(null));
        await viewModel.SyncNowCommand.ExecuteAsync(null);

        // Phase 16F: manual Sync Now routes through the coordinator (bypasses debounce, respects
        // overlap protection) rather than calling IPersonalSyncService directly.
        Assert.Equal(1, coordinator.SyncNowCallCount);
        Assert.Contains("3 imported", viewModel.SyncResultSummary);
        Assert.Contains("2 exported", viewModel.SyncResultSummary);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SyncNowCommand_ConflictsDetected_SummaryMentionsConflictCount()
    {
        var service = new FakePersonalSyncSettingsService { Status = PersonalSyncStatus.Ready };
        var coordinator = new FakePersonalSyncCoordinator
        {
            NextSyncNowResult = new PersonalSyncResult(PersonalSyncOutcome.Completed, 0, 0, 1, ConflictCount: 1, MalformedCount: 0, IoFailureCount: 0, Message: null),
        };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), coordinator);
        await viewModel.PendingLoad;

        await viewModel.SyncNowCommand.ExecuteAsync(null);

        Assert.Contains("1 conflict", viewModel.SyncResultSummary);
    }

    // ------------------------------------------------------------------
    // Phase 16F §12/§36: automatic-sync status surfaced from IPersonalSyncCoordinator.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Constructor_AppliesTheCoordinatorSCurrentStatusImmediately()
    {
        var service = new FakePersonalSyncSettingsService { Status = PersonalSyncStatus.Ready };
        var coordinator = new FakePersonalSyncCoordinator
        {
            CurrentStatus = new PersonalSyncAutomationSnapshot(
                PersonalSyncAutomationState.UpToDate, false, new DateTimeOffset(2026, 9, 9, 14, 41, 0, TimeSpan.Zero), null, false, 0, 0, null, null, 0),
        };

        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), coordinator);
        await viewModel.PendingLoad;

        Assert.Equal("Up to date", viewModel.AutomationStatusPresentation.Text);
        Assert.NotNull(viewModel.LastSyncedText);
        Assert.StartsWith("Last synced:", viewModel.LastSyncedText);
    }

    [Theory]
    [InlineData(PersonalSyncAutomationState.Off, "Off")]
    [InlineData(PersonalSyncAutomationState.NotConfigured, "Not configured")]
    [InlineData(PersonalSyncAutomationState.Syncing, "Syncing…")]
    [InlineData(PersonalSyncAutomationState.Pending, "Changes pending")]
    [InlineData(PersonalSyncAutomationState.Offline, "Sync unavailable")]
    [InlineData(PersonalSyncAutomationState.ConflictAttention, "Attention needed")]
    [InlineData(PersonalSyncAutomationState.HistoryConflictAttention, "History sync conflict")]
    [InlineData(PersonalSyncAutomationState.Error, "Sync error")]
    public async Task AutomationStatusPresentation_ReflectsEveryState(PersonalSyncAutomationState state, string expectedText)
    {
        var coordinator = new FakePersonalSyncCoordinator
        {
            CurrentStatus = new PersonalSyncAutomationSnapshot(state, false, null, null, false, 0, 0, null, null, 0),
        };
        var viewModel = new PersonalSyncSettingsViewModel(
            new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), coordinator);
        await viewModel.PendingLoad;

        Assert.Equal(expectedText, viewModel.AutomationStatusPresentation.Text);
    }

    [Fact]
    public async Task LastSyncedText_NullBeforeAnySuccessfulSync()
    {
        var coordinator = new FakePersonalSyncCoordinator
        {
            CurrentStatus = PersonalSyncAutomationSnapshot.Initial,
        };
        var viewModel = new PersonalSyncSettingsViewModel(
            new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), coordinator);
        await viewModel.PendingLoad;

        Assert.Null(viewModel.LastSyncedText);
    }

    [Fact]
    public async Task OfflineStatus_NeverUsesAlarmingDataLossLanguage()
    {
        var coordinator = new FakePersonalSyncCoordinator
        {
            CurrentStatus = new PersonalSyncAutomationSnapshot(PersonalSyncAutomationState.Offline, false, null, null, true, 0, 0, null, "unavailable", 1),
        };
        var viewModel = new PersonalSyncSettingsViewModel(
            new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), coordinator);
        await viewModel.PendingLoad;

        var detail = viewModel.AutomationStatusPresentation.DetailText;
        Assert.NotNull(detail);
        Assert.DoesNotContain("lost", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fail", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("saved locally", detail, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Phase 16D §4/§27: the minimal conflict summary/review surface.
    // ------------------------------------------------------------------

    private static readonly DateOnly Date = new(2026, 9, 9);
    private static readonly Guid RemoteDeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);

    private static SyncConflictSummary DraftConflictSummary() => new(
        SyncEntityType.AssociateDraft, "2026-09-09", "Sep 9 Draft", IsRemoteDeletion: false, IsLocalDeletion: false,
        RemoteDeviceId, RemoteRevision: 2, Now, Now, HasNewerRemoteActivity: false);

    [Fact]
    public async Task Constructor_LoadsConflictCount()
    {
        var service = new FakePersonalSyncSettingsService();
        var conflictService = new FakePersonalSyncConflictResolutionService { NextConflicts = [DraftConflictSummary()] };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), conflictService, new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        Assert.Equal(1, viewModel.ConflictCount);
        Assert.True(viewModel.HasConflicts);
    }

    [Fact]
    public async Task ReviewConflictsCommand_OpensTheConflictList()
    {
        var service = new FakePersonalSyncSettingsService();
        var conflictService = new FakePersonalSyncConflictResolutionService { NextConflicts = [DraftConflictSummary()] };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), conflictService, new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        await viewModel.ReviewConflictsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsReviewingConflicts);
        Assert.Single(viewModel.Conflicts);
    }

    [Fact]
    public async Task OpenConflictCommand_ForADraft_LoadsDraftDetail_AndDisablesUseRemoteWhenNotADeletion()
    {
        var service = new FakePersonalSyncSettingsService();
        var summary = DraftConflictSummary();
        var localPayload = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 };
        var remotePayload = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 500 };
        var conflictService = new FakePersonalSyncConflictResolutionService
        {
            NextConflicts = [summary],
            NextDraftDetail = new DraftConflictDetail(Date, localPayload, remotePayload, IsRemoteDeletion: false, IsLocalDeletion: false, RemoteDeviceId, Now, HasNewerRemoteActivity: false),
        };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), conflictService, new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        await viewModel.OpenConflictCommand.ExecuteAsync(summary);

        Assert.NotNull(viewModel.SelectedConflictDetail);
        Assert.False(viewModel.SelectedConflictDetail!.IsRemoteDeletion);
        Assert.True(viewModel.UseRemoteCommand.CanExecute(null));
        Assert.False(viewModel.AcceptDeletionCommand.CanExecute(null));
        Assert.True(viewModel.KeepLocalCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpenConflictCommand_ForARemoteDeletion_OnlyOffersKeepLocalAndAcceptDeletion()
    {
        var service = new FakePersonalSyncSettingsService();
        var summary = DraftConflictSummary() with { IsRemoteDeletion = true };
        var localPayload = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 };
        var conflictService = new FakePersonalSyncConflictResolutionService
        {
            NextConflicts = [summary],
            NextDraftDetail = new DraftConflictDetail(Date, localPayload, null, IsRemoteDeletion: true, IsLocalDeletion: false, RemoteDeviceId, Now, HasNewerRemoteActivity: false),
        };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), conflictService, new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;

        await viewModel.OpenConflictCommand.ExecuteAsync(summary);

        Assert.False(viewModel.UseRemoteCommand.CanExecute(null));
        Assert.True(viewModel.AcceptDeletionCommand.CanExecute(null));
        Assert.True(viewModel.KeepLocalCommand.CanExecute(null));
    }

    [Fact]
    public async Task KeepLocalCommand_OnResolved_ClosesTheDetailAndRefreshesTheConflictList()
    {
        var service = new FakePersonalSyncSettingsService();
        var summary = DraftConflictSummary();
        var conflictService = new FakePersonalSyncConflictResolutionService
        {
            NextConflicts = [summary],
            NextDraftDetail = new DraftConflictDetail(Date, AssociateDraftPayload.CreateDefault(), AssociateDraftPayload.CreateDefault(), false, false, RemoteDeviceId, Now, false),
            NextResolution = ConflictResolutionResult.Resolved(),
        };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), conflictService, new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;
        await viewModel.OpenConflictCommand.ExecuteAsync(summary);

        conflictService.NextConflicts = []; // Resolved — the next refresh finds none.
        await viewModel.KeepLocalCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedConflictDetail);
        Assert.Equal(0, viewModel.ConflictCount);
        Assert.Equal(("KeepLocal", (SyncEntityType.AssociateDraft, "2026-09-09")), (conflictService.LastResolutionKind, conflictService.LastResolvedEntity));
    }

    [Fact]
    public async Task KeepLocalCommand_OnExportUnavailable_KeepsTheConflictOpen_AndShowsAMessage()
    {
        var service = new FakePersonalSyncSettingsService();
        var summary = DraftConflictSummary();
        var conflictService = new FakePersonalSyncConflictResolutionService
        {
            NextConflicts = [summary],
            NextDraftDetail = new DraftConflictDetail(Date, AssociateDraftPayload.CreateDefault(), AssociateDraftPayload.CreateDefault(), false, false, RemoteDeviceId, Now, false),
            NextResolution = ConflictResolutionResult.ExportUnavailable(),
        };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), conflictService, new FakePersonalSyncCoordinator());
        await viewModel.PendingLoad;
        await viewModel.OpenConflictCommand.ExecuteAsync(summary);

        await viewModel.KeepLocalCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SelectedConflictDetail); // Still open — never falsely marked resolved.
        Assert.NotNull(viewModel.ConflictActionMessage);
    }
}
