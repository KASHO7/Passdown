using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Refresh;

/// <summary>
/// Phase 19A: the interactive app's automatic official-data refresh. Time is a <see cref="FakeClock"/>
/// advanced explicitly — nothing here waits real hours. <see cref="FakeRefreshOrchestrator"/> stands in
/// for retrieval and, like the real orchestrator, writes source state when a comprehensive check runs,
/// so every decision is exercised against persisted state exactly as production derives it.
/// </summary>
public sealed class OfficialDataRefreshCoordinatorTests
{
    /// <summary>8:00 AM Eastern (EDT) on 2026-10-15 — after the 6:00 AM daily boundary.</summary>
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 15);

    private sealed class StubRegistration(string sourceId, SourceCategory category, SourceScope scope) : SourceRegistration(sourceId, category, requiresBrowserSession: false)
    {
        public override SourceScope Scope => scope;

        public override Task<SourceOperationResult> ExecuteAsync(DateOnly? requestedDate, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Tests never execute registrations directly.");
    }

    private sealed class StubFinalizer : IScheduledPassdownFinalizer
    {
        public int CallCount { get; private set; }
        public Exception? Throw { get; set; }
        public ScheduledFinalizationSummary Result { get; set; } = ScheduledFinalizationSummary.None;
        public List<string> Log { get; init; } = [];

        public Task<ScheduledFinalizationSummary> FinalizeEligibleAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            Log.Add("finalize");
            return Throw is not null ? throw Throw : Task.FromResult(Result);
        }
    }

    private sealed class StubRunner : IScheduledRefreshRunner
    {
        public List<RefreshMode> Modes { get; } = [];
        public Exception? Throw { get; set; }
        public List<string> Log { get; init; } = [];

        public Task<ScheduledRefreshResult> RunAsync(RefreshMode mode, CancellationToken cancellationToken = default)
        {
            Modes.Add(mode);
            Log.Add("maintenance");
            if (Throw is not null)
            {
                throw Throw;
            }

            var summary = new RefreshSummary(RefreshJobType.DeepRefresh, T0, T0, 0, 0, 0, 0, []);
            return Task.FromResult(new ScheduledRefreshResult(ScheduledRefreshOutcome.Success, summary));
        }
    }

    private sealed class Harness
    {
        public required FakeClock Clock { get; init; }
        public required FakeRefreshOrchestrator Orchestrator { get; init; }
        public required InMemorySourceStateRepository States { get; init; }
        public required InMemoryDatedResultRepository Dated { get; init; }
        public required InMemoryDeepRefreshStatusRepository Deep { get; init; }
        public required FakeScheduledRefreshLock CrossProcessLock { get; init; }
        public required OfficialRefreshGate Gate { get; init; }
        public required List<DataChange> Changes { get; init; }
        public required StubFinalizer Finalizer { get; init; }
        public required StubRunner Runner { get; init; }
        public required ManualDelayScheduler Delay { get; init; }
        public required IReadOnlyList<SourceRegistration> Registrations { get; init; }
        public required OfficialDataRefreshCoordinator Coordinator { get; init; }

        /// <summary>Source ids the next comprehensive check (simulated, like the real orchestrator) reports as failed.</summary>
        public HashSet<string> FailingSources { get; } = [];

        /// <summary>Writes source state for every registration at <paramref name="at"/>, exactly as the orchestrator's success/failure branches would.</summary>
        public async Task WriteSourceStateAsync(DateTimeOffset at, params string[] failing)
        {
            foreach (var registration in Registrations)
            {
                var existing = await States.GetAsync(registration.SourceId);
                var failed = failing.Contains(registration.SourceId);
                await States.SaveAsync(new SourceStateRecord(
                    registration.SourceId, registration.Category, at,
                    failed ? existing?.LastSuccessAt : at,
                    NextDueAt: null, SourceFingerprint: null,
                    ConsecutiveFailureCount: failed ? (existing?.ConsecutiveFailureCount ?? 0) + 1 : 0));
            }
        }

        /// <summary>Seeds a dated result for every dated category on <paramref name="date"/>, last checked at <paramref name="checkedAt"/>.</summary>
        public async Task SeedDateAsync(DateOnly date, DateTimeOffset checkedAt, bool lastAttemptFailed = false)
        {
            foreach (var registration in Registrations.Where(r => r.Scope == SourceScope.Dated))
            {
                var key = new DatedResultKey(registration.Category, date);
                await Dated.RecordSuccessAsync(key, FakeEvidence.Verified(checkedAt, date), "{}");
                if (lastAttemptFailed)
                {
                    await Dated.RecordFailedAttemptAsync(key, FakeEvidence.Error(checkedAt, date));
                }
            }
        }
    }

    private static Harness CreateHarness(DateTimeOffset? now = null, bool withStartupMaintenance = true)
    {
        var clock = new FakeClock(now ?? T0);
        var orchestrator = new FakeRefreshOrchestrator();
        var states = new InMemorySourceStateRepository();
        var dated = new InMemoryDatedResultRepository();
        var deep = new InMemoryDeepRefreshStatusRepository();
        var crossProcessLock = new FakeScheduledRefreshLock();
        var gate = new OfficialRefreshGate(() => crossProcessLock);
        var changes = new List<DataChange>();
        var notifier = new DataChangeNotifier();
        notifier.Changed += (_, change) => { lock (changes) { changes.Add(change); } };
        var log = new List<string>();
        var finalizer = new StubFinalizer { Log = log };
        var runner = new StubRunner { Log = log };
        var delay = new ManualDelayScheduler();

        IReadOnlyList<SourceRegistration> registrations =
        [
            new StubRegistration("park-hours.official", SourceCategory.ParkHours, SourceScope.Dated),
            new StubRegistration("dated-admission.official", SourceCategory.DatedAdmissionTickets, SourceScope.Dated),
            new StubRegistration("promotions.official", SourceCategory.Promotions, SourceScope.Catalog),
        ];

        var coordinator = new OfficialDataRefreshCoordinator(
            orchestrator, registrations, states, dated,
            FreshnessPolicyCatalog.Build(new FreshnessOptions()),
            gate, clock,
            dataChangeNotifier: notifier,
            finalizer: withStartupMaintenance ? finalizer : null,
            maintenanceRunner: withStartupMaintenance ? runner : null,
            deepRefreshStatusRepository: deep,
            delayAsync: delay.Delay);

        var harness = new Harness
        {
            Clock = clock, Orchestrator = orchestrator, States = states, Dated = dated, Deep = deep,
            CrossProcessLock = crossProcessLock, Gate = gate, Changes = changes, Finalizer = finalizer, Runner = runner,
            Delay = delay, Registrations = registrations, Coordinator = coordinator,
        };

        // Like the real orchestrator: a comprehensive check records an attempt (and success/failure) for every source.
        orchestrator.OnComprehensiveRefresh = _ => harness.WriteSourceStateAsync(clock.UtcNow, [.. harness.FailingSources]);
        return harness;
    }

    /// <summary>The daily scheduled maintenance already ran today (so startup only considers the comprehensive check).</summary>
    private static Task MarkMaintenanceRanTodayAsync(Harness h, DateTimeOffset at) =>
        h.Deep.SaveAsync(new DeepRefreshStatusRecord(at, at, DeepRefreshOutcome.Success, ParkHoursCoveragePlanner.TargetHorizon(Today), 10, 0));

    // ------------------------------------------------------------------ four-hour policy (spec §45)

    [Fact]
    public async Task FourHourInterval_NotDueAt3h59m_DueAt4h()
    {
        var h = CreateHarness();
        await h.WriteSourceStateAsync(T0);

        h.Clock.UtcNow = T0.AddHours(3).AddMinutes(59);
        await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);

        h.Clock.UtcNow = T0.AddHours(4);
        await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    [Fact]
    public async Task ManualComprehensiveRefreshAtT0Plus2h_MovesNextAutomaticCheckToT0Plus6h()
    {
        var h = CreateHarness();
        await h.WriteSourceStateAsync(T0);

        // A manual Deep Refresh (the qualifying manual comprehensive action) records every source at T0+2h.
        await h.WriteSourceStateAsync(T0.AddHours(2));

        h.Clock.UtcNow = T0.AddHours(4);
        await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);

        h.Clock.UtcNow = T0.AddHours(5).AddMinutes(59);
        await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);

        h.Clock.UtcNow = T0.AddHours(6);
        await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
        Assert.Equal(T0.AddHours(10), h.Coordinator.CurrentStatus.NextAutomaticCheckAt(h.Clock.UtcNow));
    }

    [Fact]
    public async Task DateSpecificCheckOnline_DoesNotResetTheFourHourClock()
    {
        var h = CreateHarness();
        await h.WriteSourceStateAsync(T0);

        // Check Online runs RunUserForcedRefreshAsync, which never writes source state.
        await h.Orchestrator.RunUserForcedRefreshAsync(Today);

        h.Clock.UtcNow = T0.AddHours(4);
        await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    [Fact]
    public async Task SleepResume_PastSeveralIntervals_RunsExactlyOneRefresh()
    {
        var h = CreateHarness();
        await h.WriteSourceStateAsync(T0);

        // PC slept 10:00 AM - 3:00 PM and beyond: 11 hours elapsed, i.e. two missed intervals.
        h.Clock.UtcNow = T0.AddHours(11);
        await h.Coordinator.RunDueCheckAsync();
        await h.Coordinator.RunDueCheckAsync();
        await h.Coordinator.RunDueCheckAsync();

        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    [Fact]
    public async Task RunDueCheck_SleepsNoLongerThanTheSlice_SoAWakeIsNoticedPromptly()
    {
        var h = CreateHarness();
        await h.WriteSourceStateAsync(T0);

        var wait = await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(OfficialRefreshPolicy.MaxSleepSlice, wait);

        h.Clock.UtcNow = T0.AddHours(4).AddMinutes(-2);
        wait = await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(TimeSpan.FromMinutes(2), wait);
    }

    [Fact]
    public void Policy_IntervalIsFourHours_AndGraceIsShorter()
    {
        Assert.Equal(TimeSpan.FromHours(4), OfficialRefreshPolicy.AutomaticInterval);
        Assert.True(OfficialRefreshPolicy.StartupFreshnessGrace < OfficialRefreshPolicy.AutomaticInterval);
    }

    [Fact]
    public async Task ClockMovedBackward_FuturePersistedCheck_DoesNotPostponeChecksIndefinitely()
    {
        var h = CreateHarness();
        await h.WriteSourceStateAsync(T0.AddDays(2));

        await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    // ------------------------------------------------------------------ startup (spec §5/§6/§36/§47)

    [Fact]
    public async Task Startup_MissingData_RunsComprehensiveCheck()
    {
        var h = CreateHarness();
        await MarkMaintenanceRanTodayAsync(h, T0.AddHours(-2));

        await h.Coordinator.RunStartupAsync();

        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
        Assert.Equal(OfficialDataCheckState.Checked, h.Coordinator.CurrentStatus.State);
    }

    [Fact]
    public async Task Startup_StaleData_RunsComprehensiveCheck()
    {
        var h = CreateHarness();
        await MarkMaintenanceRanTodayAsync(h, T0.AddHours(-2));
        await h.WriteSourceStateAsync(T0.AddHours(-2));

        await h.Coordinator.RunStartupAsync();

        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    [Theory]
    [InlineData(2)]   // scheduled refresh finished at 7:58, app opened at 8:00
    [InlineData(30)]  // scheduled task at 7:30, app at 8:00 (spec §36)
    public async Task Startup_ImmediatelyAfterFreshScheduledRefresh_SkipsDuplicateWork(int minutesAgo)
    {
        var h = CreateHarness();
        var scheduledAt = T0.AddMinutes(-minutesAgo);
        await MarkMaintenanceRanTodayAsync(h, scheduledAt);
        await h.WriteSourceStateAsync(scheduledAt);

        await h.Coordinator.RunStartupAsync();

        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);
        Assert.Empty(h.Runner.Modes);
        Assert.Equal(1, h.Finalizer.CallCount); // finalization is local and always runs.
    }

    // Found by the Phase 19A smoke run: startup maintenance attempted every source, one failed, and the
    // startup check then repeated every request seconds later. A failure that recent is shown, not
    // immediately retried by a full re-run.
    [Fact]
    public async Task Startup_EverySourceAttemptedMinutesAgo_WithAFailure_DoesNotRepeatEverything()
    {
        var h = CreateHarness();
        await MarkMaintenanceRanTodayAsync(h, T0.AddMinutes(-5));
        await h.WriteSourceStateAsync(T0.AddMinutes(-5), "promotions.official");

        await h.Coordinator.RunStartupAsync();

        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);
        Assert.Equal(OfficialDataCheckState.PartiallyFailed, h.Coordinator.CurrentStatus.State); // shown, not hidden
    }

    [Fact]
    public async Task Startup_FailedAttemptOutsideTheGrace_ChecksAgain()
    {
        var h = CreateHarness();
        await MarkMaintenanceRanTodayAsync(h, T0.AddHours(-2));
        await h.WriteSourceStateAsync(T0.AddHours(-2), "promotions.official");

        await h.Coordinator.RunStartupAsync();

        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    [Fact]
    public async Task Startup_MaintenanceAttemptsEverySourceWithAFailure_NoSecondFullCheck()
    {
        var h = CreateHarness();
        var maintenance = new StubRunnerWritingState(h.Runner, h, failing: "promotions.official");
        var coordinator = new OfficialDataRefreshCoordinator(
            h.Orchestrator, h.Registrations, h.States, h.Dated, FreshnessPolicyCatalog.Build(new FreshnessOptions()), h.Gate, h.Clock,
            finalizer: h.Finalizer, maintenanceRunner: maintenance, deepRefreshStatusRepository: h.Deep);

        await coordinator.RunStartupAsync();

        Assert.Equal([RefreshMode.StartupCatchUp], h.Runner.Modes);
        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    [Fact]
    public async Task Startup_MissedDailyMaintenance_FinalizesThenRunsCatchUpThenSkipsRedundantComprehensive()
    {
        var h = CreateHarness();
        await h.Deep.SaveAsync(new DeepRefreshStatusRecord(T0.AddDays(-1), T0.AddDays(-1), DeepRefreshOutcome.Success, ParkHoursCoveragePlanner.TargetHorizon(Today), 10, 0));

        // Like the real due-gated runner at 8 AM: every source is due, so all are checked.
        h.Runner.Log.Clear();
        var runner = h.Runner;
        h.Orchestrator.OnComprehensiveRefresh = _ => h.WriteSourceStateAsync(h.Clock.UtcNow);
        var maintenanceWritesState = new StubRunnerWritingState(runner, h);

        var coordinator = new OfficialDataRefreshCoordinator(
            h.Orchestrator, h.Registrations, h.States, h.Dated, FreshnessPolicyCatalog.Build(new FreshnessOptions()), h.Gate, h.Clock,
            finalizer: h.Finalizer, maintenanceRunner: maintenanceWritesState, deepRefreshStatusRepository: h.Deep);

        await coordinator.RunStartupAsync();

        Assert.Equal(["finalize", "maintenance"], h.Finalizer.Log);
        Assert.Equal([RefreshMode.StartupCatchUp], runner.Modes);
        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount); // maintenance already checked every source moments ago.
    }

    private sealed class StubRunnerWritingState(StubRunner inner, Harness h, params string[] failing) : IScheduledRefreshRunner
    {
        public async Task<ScheduledRefreshResult> RunAsync(RefreshMode mode, CancellationToken cancellationToken = default)
        {
            var result = await inner.RunAsync(mode, cancellationToken);
            await h.WriteSourceStateAsync(h.Clock.UtcNow, failing);
            return result;
        }
    }

    [Fact]
    public async Task Startup_MaintenanceAlreadyRanToday_DoesNotRepeatParkHoursCoverageSweep()
    {
        var h = CreateHarness();
        await MarkMaintenanceRanTodayAsync(h, T0.AddHours(-2)); // 6:00 AM scheduled task

        await h.Coordinator.RunStartupAsync();

        Assert.Empty(h.Runner.Modes);
        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    [Fact]
    public async Task Startup_CrossProcessLockHeld_NeverFinalizesOrRefreshes()
    {
        var h = CreateHarness();
        h.CrossProcessLock.AcquireSucceeds = false;

        await h.Coordinator.RunStartupAsync();

        Assert.Equal(0, h.Finalizer.CallCount);
        Assert.Empty(h.Runner.Modes);
        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);
        Assert.Equal(0, h.CrossProcessLock.DisposeCallCount); // never disposes a lock it never acquired.
    }

    [Fact]
    public async Task Startup_FinalizationThrows_NoOnlineWork_DoesNotPropagate_ReleasesGate()
    {
        var h = CreateHarness();
        h.Finalizer.Throw = new InvalidOperationException("boom");

        await h.Coordinator.RunStartupAsync();

        Assert.Empty(h.Runner.Modes);
        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);
        Assert.Null(h.Gate.Current);
        Assert.Equal(1, h.CrossProcessLock.DisposeCallCount);
    }

    [Fact]
    public async Task Startup_MaintenanceRunnerThrows_ReleasesGateAndLock()
    {
        var h = CreateHarness();
        h.Runner.Throw = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.RunStartupAsync());

        Assert.Null(h.Gate.Current);
        Assert.Equal(1, h.CrossProcessLock.DisposeCallCount);
    }

    [Fact]
    public async Task Start_StartupFailureIsContained_AndLoopKeepsRunning()
    {
        var h = CreateHarness();
        h.Runner.Throw = new InvalidOperationException("boom");

        h.Coordinator.Start();
        await WaitUntilAsync(() => h.Delay.PendingCount == 1);

        Assert.False(h.Coordinator.LoopTask!.IsFaulted);
        await h.Coordinator.StopAsync();
        Assert.True(h.Coordinator.LoopTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Startup_FinalizedSomething_PublishesHistoryChange()
    {
        var h = CreateHarness();
        await MarkMaintenanceRanTodayAsync(h, T0.AddMinutes(-1));
        await h.WriteSourceStateAsync(T0.AddMinutes(-1));
        h.Finalizer.Result = new ScheduledFinalizationSummary(1, 1, 0, []);

        await h.Coordinator.RunStartupAsync();

        Assert.Contains(h.Changes, c => c.Kind == DataChangeKind.FinalizedHistory);
    }

    // ------------------------------------------------------------------ failures (spec §9/§27/§28)

    [Fact]
    public async Task PartialFailure_StatusListsFailedSource_AndDoesNotRetryRapidly()
    {
        var h = CreateHarness(withStartupMaintenance: false);
        h.FailingSources.Add("promotions.official");

        await h.Coordinator.RunStartupAsync();

        var status = h.Coordinator.CurrentStatus;
        Assert.Equal(OfficialDataCheckState.PartiallyFailed, status.State);
        Assert.Equal(["promotions.official"], status.FailedSources.Select(s => s.SourceId));
        Assert.Null(status.LastSuccessfulCheckAt); // never claims every source succeeded.
        Assert.Equal(T0, status.LastCheckedAt);

        h.Clock.UtcNow = T0.AddMinutes(10);
        await h.Coordinator.RunDueCheckAsync();
        h.Clock.UtcNow = T0.AddHours(3);
        await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    [Fact]
    public async Task Offline_FullFailure_KeepsLastKnownGoodTimestamps_AndWaitsForTheNextInterval()
    {
        var h = CreateHarness(withStartupMaintenance: false);
        await h.WriteSourceStateAsync(T0.AddHours(-5)); // last good check yesterday-ish
        foreach (var registration in h.Registrations)
        {
            h.FailingSources.Add(registration.SourceId);
        }

        await h.Coordinator.RunStartupAsync();

        var status = h.Coordinator.CurrentStatus;
        Assert.Equal(OfficialDataCheckState.Failed, status.State);
        foreach (var registration in h.Registrations)
        {
            var state = await h.States.GetAsync(registration.SourceId);
            Assert.Equal(T0.AddHours(-5), state!.LastSuccessAt); // last-known-good untouched.
        }

        for (var minutes = 5; minutes < 240; minutes += 5)
        {
            h.Clock.UtcNow = T0.AddMinutes(minutes);
            await h.Coordinator.RunDueCheckAsync();
        }

        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount); // no rapid retry loop.

        h.Clock.UtcNow = T0.AddHours(4);
        await h.Coordinator.RunDueCheckAsync();
        Assert.Equal(2, h.Orchestrator.ComprehensiveRefreshCallCount); // the normal policy tries again.
    }

    [Fact]
    public async Task AutomaticRefresh_CrossProcessLockHeld_SkipsWithoutRunning()
    {
        var h = CreateHarness(withStartupMaintenance: false);
        h.CrossProcessLock.AcquireSucceeds = false;

        await h.Coordinator.RunDueCheckAsync();

        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    // ------------------------------------------------------------------ concurrency (spec §29)

    [Fact]
    public async Task ConcurrentDueChecks_NeverOverlap_SecondSeesFreshDataAndSkips()
    {
        var h = CreateHarness(withStartupMaintenance: false);
        var release = new TaskCompletionSource();
        var concurrent = 0;
        var maxConcurrent = 0;
        h.Orchestrator.OnComprehensiveRefresh = async _ =>
        {
            maxConcurrent = Math.Max(maxConcurrent, Interlocked.Increment(ref concurrent));
            await release.Task;
            await h.WriteSourceStateAsync(h.Clock.UtcNow);
            Interlocked.Decrement(ref concurrent);
        };

        var first = h.Coordinator.RunDueCheckAsync();
        var second = h.Coordinator.RunDueCheckAsync();
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
        Assert.Equal(1, maxConcurrent);
    }

    [Fact]
    public async Task AutomaticRefresh_WaitsForManualDeepRefreshThatResetsTheClock_ThenSkips()
    {
        var h = CreateHarness(withStartupMaintenance: false);
        await h.WriteSourceStateAsync(T0.AddHours(-5));

        var manual = await h.Gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.DeepRefresh), waitForInProcessWork: true);
        Assert.NotNull(manual);

        var automatic = h.Coordinator.RunDueCheckAsync();
        Assert.False(automatic.IsCompleted); // waiting, not overlapping.

        await h.WriteSourceStateAsync(T0); // the Deep Refresh checked everything
        manual!.Dispose();
        await automatic;

        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    [Fact]
    public async Task ComprehensiveCheck_StatusShowsChecking_ThenChecked()
    {
        var h = CreateHarness(withStartupMaintenance: false);
        var observed = new List<OfficialDataStatus>();
        h.Coordinator.StatusChanged += (_, status) => { lock (observed) { observed.Add(status); } };
        bool? checkingDuringRun = null;
        h.Orchestrator.OnComprehensiveRefresh = async _ =>
        {
            checkingDuringRun = h.Coordinator.CurrentStatus.IsChecking;
            await h.WriteSourceStateAsync(h.Clock.UtcNow);
        };

        await h.Coordinator.RunDueCheckAsync();
        await WaitUntilAsync(() => !h.Coordinator.CurrentStatus.IsChecking && h.Coordinator.CurrentStatus.State == OfficialDataCheckState.Checked);

        Assert.True(checkingDuringRun);
        Assert.Equal(T0, h.Coordinator.CurrentStatus.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task GateCompletion_PublishesOfficialDataChange_WithTheCheckedDate()
    {
        var h = CreateHarness(withStartupMaintenance: false);

        using (await h.Gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.DateCheck, Today.AddDays(3)), waitForInProcessWork: true))
        {
        }

        using (await h.Gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: true))
        {
        }

        Assert.Contains(new DataChange(DataChangeKind.OfficialData, Today.AddDays(3)), h.Changes);
        Assert.Contains(new DataChange(DataChangeKind.OfficialData, null), h.Changes);
    }

    // ------------------------------------------------------------------ lifetime (spec §7/§34/§35)

    [Fact]
    public void Construction_StartsNothing()
    {
        var h = CreateHarness();

        Assert.Null(h.Coordinator.LoopTask);
        Assert.Equal(0, h.Orchestrator.ComprehensiveRefreshCallCount);
        Assert.Equal(0, h.Finalizer.CallCount);
        Assert.Equal(0, h.Delay.PendingCount);
    }

    [Fact]
    public async Task Start_IsIdempotent_OneLoopOneTimer()
    {
        var h = CreateHarness();

        h.Coordinator.Start();
        var loop = h.Coordinator.LoopTask;
        h.Coordinator.Start();
        h.Coordinator.Start();
        await WaitUntilAsync(() => h.Delay.PendingCount == 1);

        Assert.Same(loop, h.Coordinator.LoopTask);
        Assert.Equal(1, h.Delay.PendingCount);
        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);
        await h.Coordinator.StopAsync();
    }

    [Fact]
    public async Task Loop_AfterTheIntervalElapses_RunsOneMoreCheck()
    {
        var h = CreateHarness(withStartupMaintenance: false);
        h.Coordinator.Start();
        await WaitUntilAsync(() => h.Delay.PendingCount == 1);
        Assert.Equal(1, h.Orchestrator.ComprehensiveRefreshCallCount);

        h.Clock.UtcNow = T0.AddHours(4);
        Assert.True(h.Delay.Elapse(OfficialRefreshPolicy.MaxSleepSlice));
        await WaitUntilAsync(() => h.Orchestrator.ComprehensiveRefreshCallCount == 2 && h.Delay.PendingCount == 1);

        await h.Coordinator.StopAsync();
        Assert.Equal(2, h.Orchestrator.ComprehensiveRefreshCallCount);
    }

    [Fact]
    public async Task Stop_CancelsAnInFlightCheck_Cleanly()
    {
        var h = CreateHarness(withStartupMaintenance: false);
        var started = new TaskCompletionSource();
        h.Orchestrator.OnComprehensiveRefresh = async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };

        h.Coordinator.Start();
        await started.Task;
        await h.Coordinator.StopAsync();

        Assert.True(h.Coordinator.LoopTask!.IsCompleted);
        Assert.Null(h.Gate.Current);
        Assert.Equal(1, h.CrossProcessLock.DisposeCallCount);
    }

    // ------------------------------------------------------------------ Associate date auto-check (spec §10-§12/§48)

    /// <summary>Date checks only run while the coordinator runs; start it and let its startup check finish first.</summary>
    private static async Task<Harness> CreateRunningHarnessAsync()
    {
        var h = CreateHarness();
        await MarkMaintenanceRanTodayAsync(h, T0.AddMinutes(-1));
        await h.WriteSourceStateAsync(T0.AddMinutes(-1)); // fresh: startup skips its own check
        h.Coordinator.Start();
        await h.Coordinator.StartupCompletion;
        return h;
    }

    [Fact]
    public async Task DateCheck_CoordinatorNotStarted_NeverGoesOnline()
    {
        var h = CreateHarness();

        Assert.Equal(DateCheckOutcome.SkippedNotRunning, await h.Coordinator.CheckDateIfStaleAsync(Today));
        Assert.Empty(h.Orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DateCheck_AfterStop_NeverGoesOnline()
    {
        var h = await CreateRunningHarnessAsync();
        await h.Coordinator.StopAsync();

        Assert.Equal(DateCheckOutcome.SkippedNotRunning, await h.Coordinator.CheckDateIfStaleAsync(Today));
        Assert.Empty(h.Orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DateCheck_TodayMissing_ChecksOnline()
    {
        var h = await CreateRunningHarnessAsync();

        var outcome = await h.Coordinator.CheckDateIfStaleAsync(Today);

        Assert.Equal(DateCheckOutcome.Checked, outcome);
        Assert.Equal([Today], h.Orchestrator.ManualDateRefreshCalls);
        Assert.Contains(new DataChange(DataChangeKind.OfficialData, Today), h.Changes);
    }

    [Fact]
    public async Task DateCheck_TodayStale_ChecksOnline()
    {
        var h = await CreateRunningHarnessAsync();
        await h.SeedDateAsync(Today, T0.AddHours(-3)); // 5:00 AM — before today's 6:00 AM boundary

        Assert.Equal(DateCheckOutcome.Checked, await h.Coordinator.CheckDateIfStaleAsync(Today));
        Assert.Single(h.Orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DateCheck_TodayFresh_NoRequest()
    {
        var h = await CreateRunningHarnessAsync();
        await h.SeedDateAsync(Today, T0.AddHours(-1)); // 7:00 AM — after today's boundary

        Assert.Equal(DateCheckOutcome.SkippedFresh, await h.Coordinator.CheckDateIfStaleAsync(Today));
        Assert.Empty(h.Orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DateCheck_LastAttemptFailed_CountsAsStale()
    {
        var h = await CreateRunningHarnessAsync();
        await h.SeedDateAsync(Today, T0.AddHours(-1), lastAttemptFailed: true);

        Assert.Equal(DateCheckOutcome.Checked, await h.Coordinator.CheckDateIfStaleAsync(Today));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public async Task DateCheck_FutureMissing_ChecksOnline(int daysAhead)
    {
        var h = await CreateRunningHarnessAsync();

        Assert.Equal(DateCheckOutcome.Checked, await h.Coordinator.CheckDateIfStaleAsync(Today.AddDays(daysAhead)));
        Assert.Equal([Today.AddDays(daysAhead)], h.Orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DateCheck_FutureStale_ChecksOnline()
    {
        var h = await CreateRunningHarnessAsync();
        await h.SeedDateAsync(Today.AddDays(9), T0.AddDays(-1));

        Assert.Equal(DateCheckOutcome.Checked, await h.Coordinator.CheckDateIfStaleAsync(Today.AddDays(9)));
    }

    [Fact]
    public async Task DateCheck_FutureFresh_NoRequest()
    {
        var h = await CreateRunningHarnessAsync();
        await h.SeedDateAsync(Today.AddDays(2), T0.AddMinutes(-20));

        Assert.Equal(DateCheckOutcome.SkippedFresh, await h.Coordinator.CheckDateIfStaleAsync(Today.AddDays(2)));
        Assert.Empty(h.Orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DateCheck_PastMissing_NoAutomaticRequest()
    {
        var h = await CreateRunningHarnessAsync();

        Assert.Equal(DateCheckOutcome.SkippedPastDate, await h.Coordinator.CheckDateIfStaleAsync(Today.AddDays(-1)));
        Assert.Empty(h.Orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DateCheck_PastStale_NoAutomaticRequest()
    {
        var h = await CreateRunningHarnessAsync();
        await h.SeedDateAsync(Today.AddDays(-3), T0.AddDays(-10));

        Assert.Equal(DateCheckOutcome.SkippedPastDate, await h.Coordinator.CheckDateIfStaleAsync(Today.AddDays(-3)));
        Assert.Empty(h.Orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DateCheck_RepeatedWhileOffline_CooldownPreventsARequestLoop()
    {
        var h = await CreateRunningHarnessAsync();
        var date = Today.AddDays(20);

        Assert.Equal(DateCheckOutcome.Checked, await h.Coordinator.CheckDateIfStaleAsync(date)); // fails offline: nothing cached
        h.Clock.UtcNow = T0.AddMinutes(5);
        Assert.Equal(DateCheckOutcome.SkippedCooldown, await h.Coordinator.CheckDateIfStaleAsync(date));
        h.Clock.UtcNow = T0.AddMinutes(16);
        Assert.Equal(DateCheckOutcome.Checked, await h.Coordinator.CheckDateIfStaleAsync(date));

        Assert.Equal(2, h.Orchestrator.ManualDateRefreshCalls.Count);
    }

    [Fact]
    public async Task DateCheck_SupersededWhileWaitingForTheGate_NeverRuns()
    {
        var h = await CreateRunningHarnessAsync();
        var manual = await h.Gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.DateCheck, Today), waitForInProcessWork: true);
        using var cts = new CancellationTokenSource();

        var pending = h.Coordinator.CheckDateIfStaleAsync(Today.AddDays(1), cancellationToken: cts.Token);
        cts.Cancel();
        manual!.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(h.Orchestrator.ManualDateRefreshCalls);
        Assert.Null(h.Gate.Current);
    }

    [Fact]
    public async Task DateCheck_RapidDateSwitching_OnlyTheLatestSelectionRuns()
    {
        var h = await CreateRunningHarnessAsync();
        var blocker = await h.Gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: true);

        // Today -> tomorrow -> next Saturday -> today: each newer selection supersedes the previous one.
        var dates = new[] { Today, Today.AddDays(1), new DateOnly(2026, 10, 17), Today };
        var pendings = new List<Task<DateCheckOutcome>>();
        CancellationTokenSource? previous = null;
        foreach (var date in dates)
        {
            previous?.Cancel();
            previous = new CancellationTokenSource();
            pendings.Add(h.Coordinator.CheckDateIfStaleAsync(date, cancellationToken: previous.Token));
        }

        blocker!.Dispose();
        foreach (var superseded in pendings.Take(3))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => superseded);
        }

        Assert.Equal(DateCheckOutcome.Checked, await pendings[^1]);
        Assert.Equal([Today], h.Orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DateCheck_DateCoveredWhileWaitingForComprehensive_SkipsAfterWaiting()
    {
        var h = await CreateRunningHarnessAsync();
        var comprehensive = await h.Gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: true);

        var pending = h.Coordinator.CheckDateIfStaleAsync(Today);
        await h.SeedDateAsync(Today, T0); // the comprehensive check covered today
        comprehensive!.Dispose();

        Assert.Equal(DateCheckOutcome.SkippedFresh, await pending);
        Assert.Empty(h.Orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DateCheck_DoesNotClaimAComprehensiveCheck()
    {
        var h = await CreateRunningHarnessAsync();
        var before = h.Coordinator.CurrentStatus.LastComprehensiveCheckAt;
        h.Clock.UtcNow = T0.AddMinutes(30);

        await h.Coordinator.CheckDateIfStaleAsync(Today);
        await WaitUntilAsync(() => !h.Coordinator.CurrentStatus.IsChecking);

        Assert.Equal(before, h.Coordinator.CurrentStatus.LastComprehensiveCheckAt);
        Assert.Equal(T0.AddMinutes(-1), h.Coordinator.CurrentStatus.LastSuccessfulCheckAt);
    }

    // ------------------------------------------------------------------ operating day

    [Fact]
    public async Task DayRollover_PublishesOperatingDayChange()
    {
        var h = CreateHarness();
        await h.WriteSourceStateAsync(T0);

        h.Clock.UtcNow = T0.AddHours(17); // 1:00 AM Eastern the next day
        await h.Coordinator.RunDueCheckAsync();

        Assert.Contains(new DataChange(DataChangeKind.OperatingDay, Today.AddDays(1)), h.Changes);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(5);
        }
    }
}
