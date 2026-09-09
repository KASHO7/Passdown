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

        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncService());
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
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncService());
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

        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncService());
        await viewModel.PendingLoad;

        Assert.True(viewModel.PersonalSyncEnabled);
        Assert.Equal(0, service.SetEnabledCallCount);
    }

    [Fact]
    public async Task ChooseFolderCommand_UserCancelsPicker_NeverCallsSetSyncRoot()
    {
        var service = new FakePersonalSyncSettingsService();
        var picker = new FakeSyncFolderPicker { NextResult = null };
        var viewModel = new PersonalSyncSettingsViewModel(service, picker, new FakePersonalSyncService());
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
        var viewModel = new PersonalSyncSettingsViewModel(service, picker, new FakePersonalSyncService());
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
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncService());
        await viewModel.PendingLoad;

        Assert.Equal("Folder unavailable", viewModel.StatusPresentation.Text);
    }

    [Fact]
    public async Task DeviceId_IsPopulatedFromTheService()
    {
        var fixedId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        var service = new FakePersonalSyncSettingsService { DeviceId = fixedId };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), new FakePersonalSyncService());
        await viewModel.PendingLoad;

        Assert.Equal(fixedId, viewModel.DeviceId);
    }

    [Fact]
    public async Task SyncNowCommand_NotReady_CannotExecute()
    {
        var service = new FakePersonalSyncSettingsService { Status = PersonalSyncStatus.NotConfigured };
        var syncService = new FakePersonalSyncService();
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), syncService);
        await viewModel.PendingLoad;

        Assert.False(viewModel.SyncNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task SyncNowCommand_Ready_RunsSyncAndDisplaysTheResultSummary()
    {
        var service = new FakePersonalSyncSettingsService { Status = PersonalSyncStatus.Ready };
        var syncService = new FakePersonalSyncService
        {
            NextResult = new PersonalSyncResult(PersonalSyncOutcome.Completed, ImportedCount: 3, ExportedCount: 2, UnchangedCount: 1, ConflictCount: 0, MalformedCount: 0, IoFailureCount: 0, Message: null),
        };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), syncService);
        await viewModel.PendingLoad;

        Assert.True(viewModel.SyncNowCommand.CanExecute(null));
        await viewModel.SyncNowCommand.ExecuteAsync(null);

        Assert.Equal(1, syncService.RunCallCount);
        Assert.Contains("3 imported", viewModel.SyncResultSummary);
        Assert.Contains("2 exported", viewModel.SyncResultSummary);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SyncNowCommand_ConflictsDetected_SummaryMentionsConflictCount()
    {
        var service = new FakePersonalSyncSettingsService { Status = PersonalSyncStatus.Ready };
        var syncService = new FakePersonalSyncService
        {
            NextResult = new PersonalSyncResult(PersonalSyncOutcome.Completed, 0, 0, 1, ConflictCount: 1, MalformedCount: 0, IoFailureCount: 0, Message: null),
        };
        var viewModel = new PersonalSyncSettingsViewModel(service, new FakeSyncFolderPicker(), syncService);
        await viewModel.PendingLoad;

        await viewModel.SyncNowCommand.ExecuteAsync(null);

        Assert.Contains("1 conflict", viewModel.SyncResultSummary);
    }
}
