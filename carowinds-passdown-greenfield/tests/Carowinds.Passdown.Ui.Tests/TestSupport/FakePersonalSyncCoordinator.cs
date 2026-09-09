using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IPersonalSyncCoordinator"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.PersonalSyncSettingsViewModel"/> tests — never runs a real debounce/periodic/retry loop.</summary>
internal sealed class FakePersonalSyncCoordinator : IPersonalSyncCoordinator
{
    public PersonalSyncAutomationSnapshot CurrentStatus { get; set; } = PersonalSyncAutomationSnapshot.Initial;

    public event EventHandler<PersonalSyncAutomationSnapshot>? StatusChanged;

    public int StartCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public int SyncNowCallCount { get; private set; }

    public List<SyncTriggerReason> RequestedReasons { get; } = [];

    public PersonalSyncResult NextSyncNowResult { get; set; } = new(PersonalSyncOutcome.Completed, 0, 0, 0, 0, 0, 0, null);

    public void Start() => StartCallCount++;

    public Task StopAsync()
    {
        StopCallCount++;
        return Task.CompletedTask;
    }

    public void RequestSync(SyncTriggerReason reason) => RequestedReasons.Add(reason);

    public Task<PersonalSyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        SyncNowCallCount++;
        return Task.FromResult(NextSyncNowResult);
    }

    /// <summary>Simulates the coordinator publishing a new status — a test-only hook, never called by production code.</summary>
    public void RaiseStatusChanged(PersonalSyncAutomationSnapshot snapshot)
    {
        CurrentStatus = snapshot;
        StatusChanged?.Invoke(this, snapshot);
    }
}
