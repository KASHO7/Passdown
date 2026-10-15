using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Refresh;

/// <summary>
/// Phase 19A building blocks: the in-process refresh gate, the aggregate official-data status, the
/// local change notifier, the services that publish local changes after committed saves, the gated
/// Check Online/Deep Refresh paths, and the diagnostics section.
/// </summary>
public sealed class Phase19ASupportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 16, 41, 0, TimeSpan.Zero); // 12:41 PM EDT
    private static readonly DateOnly Today = new(2026, 10, 15);

    private sealed class StubRegistration(string sourceId, SourceCategory category) : SourceRegistration(sourceId, category, requiresBrowserSession: false)
    {
        public override SourceScope Scope => SourceScope.Dated;

        public override Task<SourceOperationResult> ExecuteAsync(DateOnly? requestedDate, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }

    // ------------------------------------------------------------------ gate

    [Fact]
    public async Task Gate_WithoutWaiting_ReturnsNullWhileHeld()
    {
        var gate = new OfficialRefreshGate();
        using var held = await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: false);

        Assert.NotNull(held);
        Assert.Null(await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: false));
    }

    [Fact]
    public async Task Gate_Waiting_QueuesBehindTheHolder_NeverOverlaps()
    {
        var gate = new OfficialRefreshGate();
        var held = await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: true);

        var waiting = gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.DeepRefresh), waitForInProcessWork: true);
        Assert.False(waiting.IsCompleted);

        held!.Dispose();
        using var next = await waiting;
        Assert.NotNull(next);
        Assert.Equal(OfficialRefreshKind.DeepRefresh, gate.Current!.Kind);
    }

    [Fact]
    public async Task Gate_CrossProcessLockHeld_ReturnsNull_AndDoesNotBlockLaterAttempts()
    {
        var crossProcess = new FakeScheduledRefreshLock { AcquireSucceeds = false };
        var gate = new OfficialRefreshGate(() => crossProcess);

        Assert.Null(await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.DateCheck, Today), waitForInProcessWork: true));
        Assert.Equal(0, crossProcess.DisposeCallCount);

        crossProcess.AcquireSucceeds = true;
        using var entry = await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.DateCheck, Today), waitForInProcessWork: false);
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task Gate_ReleasesCrossProcessLock_AndRaisesStartedThenCompletedOnce()
    {
        var crossProcess = new FakeScheduledRefreshLock();
        var gate = new OfficialRefreshGate(() => crossProcess);
        var events = new List<string>();
        gate.Started += (_, a) => events.Add($"started:{a.Kind}");
        gate.Completed += (_, a) => events.Add($"completed:{a.Kind}");

        var entry = await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: true);
        entry!.Dispose();
        entry.Dispose();

        Assert.Equal(["started:Comprehensive", "completed:Comprehensive"], events);
        Assert.Equal(1, crossProcess.DisposeCallCount);
        Assert.Null(gate.Current);
    }

    [Fact]
    public async Task Gate_SubscriberThrows_DoesNotBreakTheRefresh()
    {
        var gate = new OfficialRefreshGate();
        gate.Started += (_, _) => throw new InvalidOperationException("subscriber");
        gate.Completed += (_, _) => throw new InvalidOperationException("subscriber");

        var entry = await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: true);
        entry!.Dispose();

        using var again = await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: false);
        Assert.NotNull(again);
    }

    [Theory]
    [InlineData(OfficialRefreshKind.Comprehensive, true)]
    [InlineData(OfficialRefreshKind.StartupMaintenance, true)]
    [InlineData(OfficialRefreshKind.DeepRefresh, true)]
    [InlineData(OfficialRefreshKind.DateCheck, false)]
    public void Activity_OnlyAllSourceKindsCountAsComprehensive(OfficialRefreshKind kind, bool expected) =>
        Assert.Equal(expected, new OfficialRefreshActivity(kind).IsComprehensive);

    // ------------------------------------------------------------------ status aggregation

    private static readonly SourceRegistration ParkHours = new StubRegistration("park-hours.official", SourceCategory.ParkHours);
    private static readonly SourceRegistration FastLane = new StubRegistration("dated-fast-lane.official", SourceCategory.DatedFastLane);

    private static SourceStateRecord Ok(SourceRegistration r, DateTimeOffset at) => new(r.SourceId, r.Category, at, at, null, null, 0);

    private static SourceStateRecord Failed(SourceRegistration r, DateTimeOffset at, DateTimeOffset? lastSuccess) => new(r.SourceId, r.Category, at, lastSuccess, null, null, 1);

    [Fact]
    public void Status_NoAttemptsYet_IsNotCheckedYet_WithNoTimestamp()
    {
        var status = OfficialDataStatus.Compute([(ParkHours, null), (FastLane, null)]);

        Assert.Equal(OfficialDataCheckState.NotCheckedYet, status.State);
        Assert.Null(status.LastCheckedAt);
        Assert.Null(status.LastSuccessfulCheckAt);
        Assert.Null(status.NextAutomaticCheckAt(Now)); // due as soon as possible.
    }

    [Fact]
    public void Status_AllSucceeded_IsChecked_AsOfTheOldestSuccess()
    {
        var status = OfficialDataStatus.Compute([(ParkHours, Ok(ParkHours, Now)), (FastLane, Ok(FastLane, Now.AddMinutes(-3)))]);

        Assert.Equal(OfficialDataCheckState.Checked, status.State);
        Assert.Equal(Now.AddMinutes(-3), status.LastSuccessfulCheckAt);
        Assert.Equal(Now.AddMinutes(-3), status.LastComprehensiveCheckAt);
        Assert.Equal(Now.AddMinutes(-3).AddHours(4), status.NextAutomaticCheckAt(Now));
        Assert.Empty(status.FailedSources);
    }

    [Fact]
    public void Status_OneFailed_IsPartial_NeverReportsSuccess()
    {
        var status = OfficialDataStatus.Compute([(ParkHours, Ok(ParkHours, Now)), (FastLane, Failed(FastLane, Now, Now.AddDays(-1)))]);

        Assert.Equal(OfficialDataCheckState.PartiallyFailed, status.State);
        Assert.Null(status.LastSuccessfulCheckAt);
        Assert.Equal(Now, status.LastCheckedAt);
        Assert.Equal([FastLane.SourceId], status.FailedSources.Select(s => s.SourceId));
    }

    [Fact]
    public void Status_EveryFailed_IsFailed()
    {
        var status = OfficialDataStatus.Compute([(ParkHours, Failed(ParkHours, Now, null)), (FastLane, Failed(FastLane, Now, null))]);

        Assert.Equal(OfficialDataCheckState.Failed, status.State);
        Assert.Equal(2, status.FailedSources.Count);
    }

    [Fact]
    public void Status_SourceNeverAttempted_IsNotComprehensive()
    {
        var status = OfficialDataStatus.Compute([(ParkHours, Ok(ParkHours, Now)), (FastLane, null)]);

        Assert.Equal(OfficialDataCheckState.PartiallyFailed, status.State);
        Assert.Null(status.LastComprehensiveCheckAt);
        Assert.True(OfficialRefreshPolicy.IsAutomaticCheckDue(status, Now));
    }

    // ------------------------------------------------------------------ date freshness (existing daily cadence)

    [Fact]
    public void DateFreshness_UsesTheExistingDailySixAmBoundary()
    {
        var policies = FreshnessPolicyCatalog.Build(new FreshnessOptions());
        var key = new DatedResultKey(SourceCategory.ParkHours, Today);
        var checkedAt7Am = new DateTimeOffset(2026, 10, 15, 11, 0, 0, TimeSpan.Zero);
        var checkedAt5Am = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

        Assert.False(DateCheckFreshness.IsMissingOrStale(Today, [new DatedResultRecord(key, FakeEvidence.Verified(checkedAt7Am, Today), "{}", FakeEvidence.Verified(checkedAt7Am, Today))], [SourceCategory.ParkHours], policies, Now));
        Assert.True(DateCheckFreshness.IsMissingOrStale(Today, [new DatedResultRecord(key, FakeEvidence.Verified(checkedAt5Am, Today), "{}", FakeEvidence.Verified(checkedAt5Am, Today))], [SourceCategory.ParkHours], policies, Now));
        Assert.True(DateCheckFreshness.IsMissingOrStale(Today, [], [SourceCategory.ParkHours], policies, Now));
    }

    // ------------------------------------------------------------------ notifier

    private sealed class FakeSyncNotifier : ISyncLocalChangeNotifier
    {
        public event EventHandler<IReadOnlyCollection<SyncEntityKey>>? LocalDataChanged;

        public void Publish(IReadOnlyCollection<SyncEntityKey> changedEntities) => LocalDataChanged?.Invoke(this, changedEntities);
    }

    [Fact]
    public void Notifier_ForwardsPersonalSyncChangesThroughTheSamePathway()
    {
        var sync = new FakeSyncNotifier();
        var notifier = new DataChangeNotifier(sync);
        var received = new List<DataChange>();
        notifier.Changed += (_, c) => received.Add(c);

        sync.Publish([new SyncEntityKey(SyncEntityType.AssociateDraft, "2026-10-15")]);

        Assert.Equal([new DataChange(DataChangeKind.PersonalSync)], received);
    }

    [Fact]
    public void Notifier_BrokenSubscriber_DoesNotStopOthersOrThrow()
    {
        var notifier = new DataChangeNotifier();
        var received = 0;
        notifier.Changed += (_, _) => throw new InvalidOperationException("broken");
        notifier.Changed += (_, _) => received++;

        notifier.Publish(new DataChange(DataChangeKind.AssociateDraft, Today));

        Assert.Equal(1, received);
    }

    [Fact]
    public void DataChange_MayAffect()
    {
        Assert.True(new DataChange(DataChangeKind.EarlyRideTime).MayAffect(Today));
        Assert.True(new DataChange(DataChangeKind.AssociateDraft, Today).MayAffect(Today));
        Assert.False(new DataChange(DataChangeKind.AssociateDraft, Today.AddDays(1)).MayAffect(Today));
    }

    // ------------------------------------------------------------------ services publish after committed saves

    private static (AssociatePassdownService Service, FakeRefreshOrchestrator Orchestrator, List<DataChange> Changes) CreateAssociate(IOfficialRefreshGate? gate = null)
    {
        var clock = new FakeClock(Now);
        var orchestrator = new FakeRefreshOrchestrator();
        var notifier = new DataChangeNotifier();
        var changes = new List<DataChange>();
        notifier.Changed += (_, c) => changes.Add(c);
        var service = new AssociatePassdownService(
            new InMemoryAssociateDraftRepository(), new InMemoryDatedResultRepository(), new InMemoryCatalogProductRepository(),
            orchestrator, new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock), clock,
            refreshGate: gate, dataChangeNotifier: notifier);
        return (service, orchestrator, changes);
    }

    [Fact]
    public async Task AssociateDraftSaves_PublishTheirDate_WithoutAnyNetworkCall()
    {
        var (service, orchestrator, changes) = CreateAssociate();

        await service.SaveDraftAsync(Today, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 12000 });
        await service.SaveEditorDraftAsync(Today.AddDays(1), AssociateDraftPayload.CreateDefault() with { PreSolds = 300 }, expectedContentHash: null);

        Assert.Equal([new DataChange(DataChangeKind.AssociateDraft, Today), new DataChange(DataChangeKind.AssociateDraft, Today.AddDays(1))], changes);
        Assert.Empty(orchestrator.ManualDateRefreshCalls);
        Assert.Equal(0, orchestrator.ComprehensiveRefreshCallCount);
        Assert.Empty(orchestrator.DeepRefreshCalls);
    }

    [Fact]
    public async Task ErtSavesAndRemovals_PublishEarlyRideTimeChanges()
    {
        var notifier = new DataChangeNotifier();
        var changes = new List<DataChange>();
        notifier.Changed += (_, c) => changes.Add(c);
        var service = new ErtConfigurationService(new InMemoryMonthlyErtRepository(), new InMemoryErtDateOverrideRepository(), new FakeClock(Now), dataChangeNotifier: notifier);

        await service.SaveMonthlyAsync(new MonthlyErtDraft(2026, 10, ErtConfigurationState.Configured, null, ["Fury 325"], null));
        await service.SaveOverrideAsync(new ErtDateOverrideDraft(Today, ErtConfigurationState.NoErt, null, [], "Weather"));
        await service.RemoveOverrideAsync(Today);

        Assert.Equal(3, changes.Count);
        Assert.All(changes, c => Assert.Equal(DataChangeKind.EarlyRideTime, c.Kind));
    }

    [Fact]
    public async Task OperationalNoteSaves_PublishNoteChanges()
    {
        var notifier = new DataChangeNotifier();
        var changes = new List<DataChange>();
        notifier.Changed += (_, c) => changes.Add(c);
        var service = new OperationalNotesService(new InMemoryOperationalNoteRepository(), new FakeClock(Now), dataChangeNotifier: notifier);

        var note = await service.CreateAsync(new OperationalNoteDraft("Gate 3 closed", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, Today, null));
        await service.ResolveAsync(note.Id);

        Assert.Equal(2, changes.Count(c => c.Kind == DataChangeKind.OperationalNotes));
    }

    // ------------------------------------------------------------------ manual actions through the gate

    [Fact]
    public async Task CheckOnline_WaitsForAnInProcessAutomaticCheck_ThenStillRunsInFull()
    {
        var gate = new OfficialRefreshGate();
        var (service, orchestrator, _) = CreateAssociate(gate);
        var automatic = await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: true);

        var manual = service.CheckAssociateDateOnlineAsync(Today);
        Assert.False(manual.IsCompleted);
        Assert.Empty(orchestrator.ManualDateRefreshCalls);

        automatic!.Dispose();
        var result = await manual;

        Assert.False(result.Summary.WasSkippedDueToOverlap);
        Assert.Equal([Today], orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task CheckOnline_ScheduledTaskHoldsTheCrossProcessLock_ReportsAlreadyInProgress()
    {
        var gate = new OfficialRefreshGate(() => new FakeScheduledRefreshLock { AcquireSucceeds = false });
        var (service, orchestrator, _) = CreateAssociate(gate);

        var result = await service.CheckAssociateDateOnlineAsync(Today);

        Assert.True(result.Summary.WasSkippedDueToOverlap);
        Assert.Empty(orchestrator.ManualDateRefreshCalls);
    }

    [Fact]
    public async Task DeepRefresh_WaitsForAnInProcessAutomaticCheck_ThenRunsUnconditionally()
    {
        var gate = new OfficialRefreshGate();
        var clock = new FakeClock(Now);
        var orchestrator = new FakeRefreshOrchestrator();
        var notes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        var associate = new AssociatePassdownService(new InMemoryAssociateDraftRepository(), new InMemoryDatedResultRepository(), new InMemoryCatalogProductRepository(), orchestrator, notes, clock);
        var dashboard = new OperationsDashboardService(associate, new InMemoryFinalizedPassdownRepository(), notes, clock, orchestrator, new InMemoryDeepRefreshStatusRepository(), refreshGate: gate);
        var automatic = await gate.TryEnterAsync(new OfficialRefreshActivity(OfficialRefreshKind.Comprehensive), waitForInProcessWork: true);

        var deep = dashboard.RunDeepRefreshAsync();
        Assert.False(deep.IsCompleted);

        automatic!.Dispose();
        var result = await deep;

        Assert.False(result.Summary.WasSkippedDueToOverlap);
        Assert.Equal([(true, (DateOnly?)Today)], orchestrator.DeepRefreshCalls);
    }

    // ------------------------------------------------------------------ diagnostics

    [Fact]
    public void Diagnostics_SummaryIncludesAutomaticRefreshFacts_ButNoPayloads()
    {
        var report = new DiagnosticReport("1.0", Now, [],
            new DatabaseDiagnostics(true, 15, 1024, false, null),
            new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.Enabled, null), [])
        {
            OfficialData = OfficialDataStatus.Compute([(ParkHours, Ok(ParkHours, Now)), (FastLane, Failed(FastLane, Now, null))]),
        };

        var text = DiagnosticReportFormatter.BuildSummaryText(report);

        Assert.Contains("Automatic official-data refresh", text);
        Assert.Contains("interval: 4 hours", text);
        Assert.Contains("aggregate status: PartiallyFailed", text);
        Assert.Contains("next automatic check (estimate): 2026-10-15T20:41:00.0000000+00:00", text);
        Assert.Contains("not updated on last attempt: dated-fast-lane.official", text);
    }
}
