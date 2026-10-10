using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Tests.TestSupport;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>
/// Phase 16F §33: coordinator scheduling behavior — startup trigger, debounce/coalescing, overlap
/// protection, retry/backoff, manual bypass, periodic safety sync, status classification, and clean
/// shutdown. Uses <see cref="ManualDelayScheduler"/> so every timing assertion is deterministic (§32) —
/// no test here sleeps for a real interval.
/// </summary>
public class PersonalSyncCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Periodic = TimeSpan.FromMinutes(5);

    private static PersonalSyncCoordinator CreateCoordinator(
        FakePersonalSyncService syncService,
        FakePersonalSyncConflictResolutionService? conflictService = null,
        FakeClock? clock = null,
        ManualDelayScheduler? scheduler = null,
        IReadOnlyList<TimeSpan>? backoff = null) =>
        new(
            syncService,
            conflictService ?? new FakePersonalSyncConflictResolutionService(),
            clock ?? new FakeClock(Now),
            logger: null,
            debounceInterval: Debounce,
            periodicInterval: Periodic,
            backoffSchedule: backoff,
            delayAsync: (scheduler ?? new ManualDelayScheduler()).Delay);

    private static SyncConflictSummary MakeConflictSummary(SyncEntityType type) => new(
        type, "id", "Label", IsRemoteDeletion: false, IsLocalDeletion: false,
        Guid.NewGuid(), 1, Now, Now, HasNewerRemoteActivity: false);

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Condition was not met within the timeout.");
            }

            await Task.Delay(5);
        }
    }

    // A. Startup triggers one sync.
    [Fact]
    public void Start_TriggersExactlyOneStartupSyncPass()
    {
        var syncService = new FakePersonalSyncService();
        var coordinator = CreateCoordinator(syncService);

        coordinator.Start();

        Assert.Equal(1, syncService.RunCallCount);
    }

    [Fact]
    public void RequestSync_BeforeStart_IsANoOp()
    {
        var syncService = new FakePersonalSyncService();
        var coordinator = CreateCoordinator(syncService);

        coordinator.RequestSync(SyncTriggerReason.LocalChange);

        Assert.Equal(0, syncService.RunCallCount);
    }

    [Fact]
    public void Start_CalledTwice_OnlyTriggersOneStartupPass()
    {
        var syncService = new FakePersonalSyncService();
        var coordinator = CreateCoordinator(syncService);

        coordinator.Start();
        coordinator.Start();

        Assert.Equal(1, syncService.RunCallCount);
    }

    // B. Multiple quick local changes coalesce.
    [Fact]
    public async Task MultipleQuickLocalChanges_CoalesceIntoOneDebouncedPass()
    {
        var syncService = new FakePersonalSyncService();
        var scheduler = new ManualDelayScheduler();
        var coordinator = CreateCoordinator(syncService, scheduler: scheduler);
        coordinator.Start();
        Assert.Equal(1, syncService.RunCallCount); // startup

        coordinator.RequestSync(SyncTriggerReason.LocalChange);
        coordinator.RequestSync(SyncTriggerReason.LocalChange);
        coordinator.RequestSync(SyncTriggerReason.LocalChange);

        Assert.True(scheduler.Elapse(Debounce));
        await WaitForAsync(() => syncService.RunCallCount == 2);

        await Task.Delay(20); // give any (incorrect) extra coalesced run a chance to appear.
        Assert.Equal(2, syncService.RunCallCount); // startup + exactly one coalesced pass, not three.
    }

    // C. Local change during an active sync produces exactly one follow-up.
    [Fact]
    public async Task LocalChangeDuringActiveSync_ProducesExactlyOneFollowUpPass_NeverParallel()
    {
        var syncService = new FakePersonalSyncService { HoldRuns = true };
        var scheduler = new ManualDelayScheduler();
        var coordinator = CreateCoordinator(syncService, scheduler: scheduler);

        coordinator.Start(); // startup pass begins and blocks inside RunAsync.
        Assert.Equal(1, syncService.RunCallCount);

        coordinator.RequestSync(SyncTriggerReason.LocalChange);
        scheduler.Elapse(Debounce); // debounce elapses while the first pass is still running -> marks a follow-up pending.
        coordinator.RequestSync(SyncTriggerReason.LocalChange); // a second edit before the follow-up starts -> still just one follow-up.
        scheduler.Elapse(Debounce);

        Assert.Equal(1, syncService.RunCallCount); // no parallel run.

        syncService.ReleaseNextRun(); // let the first (startup) pass finish -> the one follow-up starts immediately.
        await WaitForAsync(() => syncService.RunCallCount == 2);
        Assert.Equal(2, syncService.RunCallCount);

        syncService.ReleaseNextRun(); // let the follow-up finish.
        await Task.Delay(20); // give any (incorrect) extra chained run a chance to appear.
        Assert.Equal(2, syncService.RunCallCount); // still exactly one follow-up — not five.
    }

    // D. Successful sync clears pending -> UpToDate, and records LastSuccessfulSyncUtc.
    [Fact]
    public void CleanSuccess_SetsUpToDate_AndRecordsLastSuccessfulSync()
    {
        var syncService = new FakePersonalSyncService();
        var clock = new FakeClock(Now);
        var coordinator = CreateCoordinator(syncService, clock: clock);

        coordinator.Start();

        var status = coordinator.CurrentStatus;
        Assert.Equal(PersonalSyncAutomationState.UpToDate, status.State);
        Assert.Equal(Now, status.LastSuccessfulSyncUtc);
        Assert.False(status.HasPendingChanges);
        Assert.False(status.IsSyncRunning);
    }

    // E/F. Unavailable folder schedules a retry; backoff increases and caps at the schedule's last step.
    [Fact]
    public async Task FolderUnavailable_SchedulesRetry_WithIncreasingBackoff_CappedAtScheduleEnd()
    {
        IReadOnlyList<TimeSpan> backoff = [TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1)];
        var syncService = new FakePersonalSyncService
        {
            NextResult = new PersonalSyncResult(PersonalSyncOutcome.FolderUnavailable, 0, 0, 0, 0, 0, 0, "unavailable"),
        };
        var scheduler = new ManualDelayScheduler();
        var coordinator = CreateCoordinator(syncService, scheduler: scheduler, backoff: backoff);

        coordinator.Start(); // startup pass fails -> schedules retry #1 (15s).
        Assert.Equal(PersonalSyncAutomationState.Offline, coordinator.CurrentStatus.State);
        Assert.Equal(1, coordinator.CurrentStatus.RetryAttempt);
        Assert.True(coordinator.CurrentStatus.HasPendingChanges);

        Assert.True(scheduler.Elapse(TimeSpan.FromSeconds(15))); // retry #1 -> schedules retry #2 (30s).
        await WaitForAsync(() => syncService.RunCallCount == 2);
        Assert.Equal(2, coordinator.CurrentStatus.RetryAttempt);

        Assert.True(scheduler.Elapse(TimeSpan.FromSeconds(30))); // retry #2 -> schedules retry #3 (1min).
        await WaitForAsync(() => syncService.RunCallCount == 3);
        Assert.Equal(3, coordinator.CurrentStatus.RetryAttempt);

        Assert.True(scheduler.Elapse(TimeSpan.FromMinutes(1))); // retry #3 -> attempt 4, capped at the schedule's last (1min) step.
        await WaitForAsync(() => syncService.RunCallCount == 4);
        Assert.Equal(4, coordinator.CurrentStatus.RetryAttempt);

        Assert.True(scheduler.Elapse(TimeSpan.FromMinutes(1))); // still capped at 1min, not growing further.
        await WaitForAsync(() => syncService.RunCallCount == 5);
    }

    // G. Success resets backoff.
    [Fact]
    public async Task SuccessAfterTransientFailures_ResetsBackoffToTheFirstStep()
    {
        IReadOnlyList<TimeSpan> backoff = [TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)];
        var syncService = new FakePersonalSyncService
        {
            NextResult = new PersonalSyncResult(PersonalSyncOutcome.FolderUnavailable, 0, 0, 0, 0, 0, 0, "unavailable"),
        };
        var scheduler = new ManualDelayScheduler();
        var coordinator = CreateCoordinator(syncService, scheduler: scheduler, backoff: backoff);

        coordinator.Start(); // fails -> retry #1 scheduled (15s).
        Assert.Equal(1, coordinator.CurrentStatus.RetryAttempt);

        syncService.NextResult = new PersonalSyncResult(PersonalSyncOutcome.Completed, 0, 0, 0, 0, 0, 0, null);
        Assert.True(scheduler.Elapse(TimeSpan.FromSeconds(15))); // retry #1 now succeeds.
        await WaitForAsync(() => coordinator.CurrentStatus.State == PersonalSyncAutomationState.UpToDate);

        Assert.Equal(0, coordinator.CurrentStatus.RetryAttempt);

        // A later failure starts back at the first backoff step, not continuing from attempt #2.
        syncService.NextResult = new PersonalSyncResult(PersonalSyncOutcome.FolderUnavailable, 0, 0, 0, 0, 0, 0, "unavailable");
        await coordinator.SyncNowAsync(); // Manual pass to observe the fresh failure synchronously.

        Assert.Equal(1, coordinator.CurrentStatus.RetryAttempt);
    }

    // I. Manual Sync Now bypasses debounce.
    [Fact]
    public async Task SyncNowAsync_BypassesDebounce_AndReturnsAFreshResultImmediately()
    {
        var syncService = new FakePersonalSyncService();
        var scheduler = new ManualDelayScheduler();
        var coordinator = CreateCoordinator(syncService, scheduler: scheduler);
        coordinator.Start();
        Assert.Equal(1, syncService.RunCallCount); // startup

        coordinator.RequestSync(SyncTriggerReason.LocalChange); // would otherwise wait out the debounce.

        var result = await coordinator.SyncNowAsync();

        Assert.Equal(2, syncService.RunCallCount); // ran immediately, not after 5s.
        Assert.Equal(PersonalSyncOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task SyncNowAsync_WhileAPassIsActive_WaitsThenRunsOneFreshPass_NeverInParallel()
    {
        var syncService = new FakePersonalSyncService { HoldRuns = true };
        var scheduler = new ManualDelayScheduler();
        var coordinator = CreateCoordinator(syncService, scheduler: scheduler);
        coordinator.Start(); // startup pass begins, blocked.
        Assert.Equal(1, syncService.RunCallCount);

        var manualTask = coordinator.SyncNowAsync();
        await Task.Delay(20);
        Assert.Equal(1, syncService.RunCallCount); // still waiting on the active pass — never parallel.

        syncService.ReleaseNextRun(); // startup pass finishes.
        await WaitForAsync(() => syncService.RunCallCount == 2);
        syncService.ReleaseNextRun(); // manual's own fresh pass finishes.

        var result = await manualTask;
        Assert.Equal(2, syncService.RunCallCount);
        Assert.Equal(PersonalSyncOutcome.Completed, result.Outcome);
    }

    // Periodic safety sync.
    [Fact]
    public async Task PeriodicIntervalElapsing_TriggersAnAdditionalPass_AndReArmsItself()
    {
        var syncService = new FakePersonalSyncService();
        var scheduler = new ManualDelayScheduler();
        var coordinator = CreateCoordinator(syncService, scheduler: scheduler);
        coordinator.Start();
        Assert.Equal(1, syncService.RunCallCount);

        Assert.True(scheduler.Elapse(Periodic));
        await WaitForAsync(() => syncService.RunCallCount == 2);

        Assert.True(scheduler.Elapse(Periodic)); // re-armed for the next interval.
        await WaitForAsync(() => syncService.RunCallCount == 3);
    }

    // StopAsync cancels the periodic loop and any pending debounce/retry.
    [Fact]
    public async Task StopAsync_CancelsThePeriodicLoop_NoFurtherAutomaticPassesRun()
    {
        var syncService = new FakePersonalSyncService();
        var scheduler = new ManualDelayScheduler();
        var coordinator = CreateCoordinator(syncService, scheduler: scheduler);
        coordinator.Start();
        Assert.Equal(1, syncService.RunCallCount);

        await coordinator.StopAsync();

        Assert.False(scheduler.Elapse(Periodic)); // nothing left pending to elapse.
        Assert.Equal(1, syncService.RunCallCount);
    }

    [Fact]
    public async Task StopAsync_CancelsAPendingDebouncedLocalChange()
    {
        var syncService = new FakePersonalSyncService();
        var scheduler = new ManualDelayScheduler();
        var coordinator = CreateCoordinator(syncService, scheduler: scheduler);
        coordinator.Start();
        coordinator.RequestSync(SyncTriggerReason.LocalChange);

        await coordinator.StopAsync();

        Assert.False(scheduler.Elapse(Debounce));
        Assert.Equal(1, syncService.RunCallCount); // only the startup pass ever ran.
    }

    // Status classification.
    [Fact]
    public void CompletedWithMutableConflicts_SetsConflictAttentionState()
    {
        var conflictService = new FakePersonalSyncConflictResolutionService
        {
            NextConflicts = [MakeConflictSummary(SyncEntityType.AssociateDraft)],
        };
        var syncService = new FakePersonalSyncService();
        var coordinator = CreateCoordinator(syncService, conflictService);

        coordinator.Start();

        var status = coordinator.CurrentStatus;
        Assert.Equal(PersonalSyncAutomationState.ConflictAttention, status.State);
        Assert.Equal(1, status.ConflictCount);
        Assert.Equal(0, status.HistoryConflictCount);
    }

    [Fact]
    public void CompletedWithHistoryConflict_SetsHistoryConflictAttentionState_DistinctFromMutableConflict()
    {
        var conflictService = new FakePersonalSyncConflictResolutionService
        {
            NextConflicts = [MakeConflictSummary(SyncEntityType.FinalizedPassdown)],
        };
        var syncService = new FakePersonalSyncService();
        var coordinator = CreateCoordinator(syncService, conflictService);

        coordinator.Start();

        var status = coordinator.CurrentStatus;
        Assert.Equal(PersonalSyncAutomationState.HistoryConflictAttention, status.State);
        Assert.Equal(1, status.HistoryConflictCount);
        Assert.Equal(0, status.ConflictCount);
    }

    [Fact]
    public void Disabled_MapsToOffState()
    {
        var syncService = new FakePersonalSyncService { NextResult = PersonalSyncResult.Disabled() };
        var coordinator = CreateCoordinator(syncService);

        coordinator.Start();

        Assert.Equal(PersonalSyncAutomationState.Off, coordinator.CurrentStatus.State);
    }

    [Fact]
    public void NotConfigured_MapsToNotConfiguredState()
    {
        var syncService = new FakePersonalSyncService
        {
            NextResult = PersonalSyncResult.NotReady(PersonalSyncStatus.NotConfigured, null),
        };
        var coordinator = CreateCoordinator(syncService);

        coordinator.Start();

        Assert.Equal(PersonalSyncAutomationState.NotConfigured, coordinator.CurrentStatus.State);
    }

    [Fact]
    public void StatusChanged_RaisesOnEveryTransition()
    {
        var syncService = new FakePersonalSyncService();
        var coordinator = CreateCoordinator(syncService);
        var raisedCount = 0;
        coordinator.StatusChanged += (_, _) => raisedCount++;

        coordinator.Start();

        Assert.True(raisedCount >= 2); // at least "Syncing" then the final classified state.
    }
}
