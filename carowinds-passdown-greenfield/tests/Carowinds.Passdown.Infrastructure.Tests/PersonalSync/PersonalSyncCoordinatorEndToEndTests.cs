using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>
/// Phase 16G — closes the one documented Phase 16F gap (docs/personal-sync-architecture.md §16F.20 /
/// "Known limitations" / "Deferred to 16G"): no prior test combined <see cref="PersonalSyncCoordinator"/>'s
/// own startup/periodic/debounce scheduling with a real two-device shared sync folder in one scenario.
/// <see cref="PersonalSyncCoordinatorTests"/> (Application.Tests) proves the coordinator's timing against a
/// fake <see cref="IPersonalSyncService"/>; <see cref="PersonalSyncServiceTests"/> (this project) proves
/// two real devices reconcile correctly through direct <c>RunAsync()</c> calls. This file wires a real
/// <see cref="PersonalSyncCoordinator"/> around each of two real, independent <see cref="PersonalSyncService"/>
/// instances (each backed by its own SQLite database, both pointed at one shared temp folder standing in
/// for OneDrive) and drives them purely through the coordinator's own triggers (startup / debounced local
/// change / periodic tick) via a local, deterministic delay scheduler — never a real sleep or a direct call
/// into <see cref="IPersonalSyncService.RunAsync"/>.
/// </summary>
public class PersonalSyncCoordinatorEndToEndTests : IDisposable
{
    private readonly string _sharedRoot = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-e2e-shared-{Guid.NewGuid():N}");
    private readonly string _tempDataRoot = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-e2e-devices-{Guid.NewGuid():N}");
    private static readonly Guid DeviceAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DeviceBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateOnly OperatingDate = new(2026, 9, 9);
    private static readonly DateTimeOffset BaseNow = new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Periodic = TimeSpan.FromMinutes(5);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_sharedRoot))
        {
            Directory.Delete(_sharedRoot, recursive: true);
        }
        if (Directory.Exists(_tempDataRoot))
        {
            Directory.Delete(_tempDataRoot, recursive: true);
        }
    }

    private sealed record TestDevice(
        IAssociateDraftRepository Drafts,
        IOperationalNotesService Notes,
        IOperationalNoteRepository NoteRepository,
        ISyncChangeTracker Tracker,
        IPersonalSyncService Sync,
        IPersonalSyncConflictResolutionService Resolution,
        FakeClock Clock,
        TestDelayScheduler Scheduler,
        PersonalSyncCoordinator Coordinator,
        IErtConfigurationService ErtConfiguration,
        IMonthlyErtRepository MonthlyErts,
        IErtDateOverrideRepository ErtOverrides);

    private TestDevice CreateDevice(string name, Guid deviceId, FakeClock clock)
    {
        var databasePath = Path.Combine(_tempDataRoot, $"{name}.db");
        var connectionFactory = new SqliteConnectionFactory(databasePath);
        IReadOnlyList<IMigration> migrations =
        [
            new Migration0001CreateSchemaVersionTable(),
            new Migration0002CreateSourceStateTable(),
            new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(),
            new Migration0005CreateProductHistoryTable(),
            new Migration0006CreateAssociateDraftsTable(),
            new Migration0007CreateManagerDraftsTable(),
            new Migration0008CreateFinalizedPassdownsTable(),
            new Migration0009AddFinalizedPassdownPdfColumns(),
            new Migration0010CreateOperationalNotesTable(),
            new Migration0011CreateDeepRefreshStatusTable(),
            new Migration0012CreateSyncEntityStateTable(),
            new Migration0013CreateSyncConflictsTable(),
            new Migration0014CreateMonthlyErtConfigurationsTable(),
            new Migration0015CreateErtDateOverridesTable(),
        ];
        new MigrationRunner(connectionFactory, migrations, NullLogger<MigrationRunner>.Instance).Run();

        var drafts = new SqliteAssociateDraftRepository(connectionFactory);
        var noteRepository = new SqliteOperationalNoteRepository(connectionFactory);
        var finalizedPassdowns = new SqliteFinalizedPassdownRepository(connectionFactory);
        var states = new SqliteSyncEntityStateRepository(connectionFactory);
        var conflicts = new SqliteSyncConflictRepository(connectionFactory);
        var unitOfWorkFactory = new SqliteUnitOfWorkFactory(connectionFactory);
        var tracker = new SyncChangeTracker(states, clock);
        var notes = new OperationalNotesService(noteRepository, clock, tracker);
        var monthlyErtRepository = new SqliteMonthlyErtRepository(connectionFactory);
        var ertDateOverrideRepository = new SqliteErtDateOverrideRepository(connectionFactory);
        var ertConfiguration = new ErtConfigurationService(monthlyErtRepository, ertDateOverrideRepository, clock, tracker);

        var deviceIdProvider = new FakeDeviceIdProvider(deviceId);
        var configStore = new FakeSyncConfigurationStore { Current = new SyncConfiguration(true, _sharedRoot) };
        var settingsService = new PersonalSyncSettingsService(
            configStore,
            new SyncFolderValidator(),
            new FakeOneDriveLocationSuggestionProvider(),
            deviceIdProvider,
            clock);

        var sync = new PersonalSyncService(settingsService, configStore, deviceIdProvider, drafts, noteRepository, finalizedPassdowns, states, conflicts, clock, monthlyErtRepository, ertDateOverrideRepository);
        var resolution = new PersonalSyncConflictResolutionService(settingsService, deviceIdProvider, drafts, noteRepository, states, conflicts, unitOfWorkFactory, clock, monthlyErtRepository, ertDateOverrideRepository);

        var scheduler = new TestDelayScheduler();
        var coordinator = new PersonalSyncCoordinator(
            sync,
            resolution,
            clock,
            logger: null,
            debounceInterval: Debounce,
            periodicInterval: Periodic,
            delayAsync: scheduler.Delay);

        return new TestDevice(drafts, notes, noteRepository, tracker, sync, resolution, clock, scheduler, coordinator, ertConfiguration, monthlyErtRepository, ertDateOverrideRepository);
    }

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

    /// <summary>
    /// The exact chain the documented gap calls out: coordinator trigger (debounced local change) on
    /// Device A -> Device A's <see cref="PersonalSyncService"/> exports to the shared folder -> Device B's
    /// own coordinator, already running, discovers the change on its next unprompted periodic tick (no
    /// manual Sync Now, no direct call into Device B's sync service) -> the record lands in Device B's
    /// local database.
    /// </summary>
    [Fact]
    public async Task DraftEditedOnDeviceA_PropagatesToDeviceB_ThroughCoordinatorLocalChangeAndPeriodicTriggers()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        b.Coordinator.Start(); // B is already running, waiting on its periodic safety sweep.
        // Real SQLite/file I/O underlies this pass (unlike the fake-service coordinator tests), so its
        // completion is genuinely asynchronous — wait for it rather than asserting immediately after Start().
        await WaitForAsync(() => b.Coordinator.CurrentStatus.State == PersonalSyncAutomationState.UpToDate);

        a.Coordinator.Start(); // A's own startup pass — folder still empty.
        await WaitForAsync(() => a.Coordinator.CurrentStatus.State == PersonalSyncAutomationState.UpToDate);

        var payload = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 14500 };
        var record = new AssociateDraft(OperatingDate, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(payload), a.Clock.UtcNow);
        await a.Drafts.SaveAsync(record);
        await a.Tracker.MarkLocalChangeAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(OperatingDate));

        a.Coordinator.RequestSync(SyncTriggerReason.LocalChange); // debounced, per §4 — not an immediate call.
        Assert.True(a.Scheduler.Elapse(Debounce));
        await WaitForAsync(() => a.Coordinator.CurrentStatus.LastResultSummary?.Contains("1 exported") == true);
        Assert.Equal(PersonalSyncAutomationState.UpToDate, a.Coordinator.CurrentStatus.State);

        // Device B has done nothing yet — no manual sync, no direct RunAsync call. Only its own periodic
        // tick (already armed since Start() above) is allowed to discover the remote change.
        var importedBeforeTick = await b.Drafts.GetAsync(OperatingDate);
        Assert.Null(importedBeforeTick);

        Assert.True(b.Scheduler.Elapse(Periodic));
        await WaitForAsync(() => b.Coordinator.CurrentStatus.LastResultSummary?.Contains("1 imported") == true);

        var imported = await b.Drafts.GetAsync(OperatingDate);
        Assert.NotNull(imported);
        Assert.Equal(14500, AssociateDraftPayloadSerializer.Deserialize(imported!.PayloadJson).ProjectedAttendance);
        Assert.Equal(PersonalSyncAutomationState.UpToDate, b.Coordinator.CurrentStatus.State);
        Assert.Equal(0, b.Coordinator.CurrentStatus.ConflictCount);

        await a.Coordinator.StopAsync();
        await b.Coordinator.StopAsync();
    }

    /// <summary>
    /// Same chain, exercised through the other two coordinator-owned triggers named in the gap
    /// (startup, both sides) and a different entity kind (Operational Note), per the instruction to cover
    /// at least one of draft-or-note distinctly from the periodic case above.
    /// </summary>
    [Fact]
    public async Task NoteCreatedOnDeviceA_PropagatesToDeviceB_ThroughStartupTriggersOnBothSides()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft(
            "Gate 3 scanner down", "Use gate 2 until replaced.", OperationalNoteCategory.Equipment,
            OperationalNotePriority.Important, OperatingDate, null));

        a.Coordinator.Start(); // startup trigger exports the note that was already dirty before Start().
        await WaitForAsync(() => a.Coordinator.CurrentStatus.LastResultSummary?.Contains("1 exported") == true);

        b.Coordinator.Start(); // startup trigger on B imports it — folder already holds A's export.
        await WaitForAsync(() => b.Coordinator.CurrentStatus.LastResultSummary?.Contains("1 imported") == true);

        var importedNote = await b.NoteRepository.GetByIdAsync(created.Id);
        Assert.NotNull(importedNote);
        Assert.Equal(created.Id, importedNote!.Id); // stable GUID preserved across the coordinator-driven path.
        Assert.Equal("Gate 3 scanner down", importedNote.Title);
        Assert.Equal(0, b.Coordinator.CurrentStatus.ConflictCount);

        await a.Coordinator.StopAsync();
        await b.Coordinator.StopAsync();
    }

    // Phase 17B: the same startup-trigger chain as the note test above, for a MonthlyErt record —
    // proves the new entity type is wired through the coordinator's own triggers, not just RunAsync().
    [Fact]
    public async Task MonthlyErtEditedOnDeviceA_PropagatesToDeviceB_ThroughStartupTriggersOnBothSides()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await a.ErtConfiguration.SaveMonthlyAsync(new MonthlyErtDraft(
            2026, 9, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325", "Copperhead Strike"], null));

        a.Coordinator.Start();
        await WaitForAsync(() => a.Coordinator.CurrentStatus.LastResultSummary?.Contains("1 exported") == true);

        b.Coordinator.Start();
        await WaitForAsync(() => b.Coordinator.CurrentStatus.LastResultSummary?.Contains("1 imported") == true);

        var imported = await b.MonthlyErts.GetAsync(2026, 9);
        Assert.NotNull(imported);
        Assert.Equal(new TimeOnly(9, 30), imported!.StartTime);
        Assert.Equal(["Fury 325", "Copperhead Strike"], imported.Rides);
        Assert.Equal(0, b.Coordinator.CurrentStatus.ConflictCount);

        await a.Coordinator.StopAsync();
        await b.Coordinator.StopAsync();
    }

    // Phase 17B: same chain for an ErtDateOverride — proves the resolver on Device B immediately
    // prefers the override over Device B's own monthly default once sync delivers it (spec Part 4/26).
    [Fact]
    public async Task ErtDateOverrideCreatedOnDeviceA_PropagatesToDeviceB_ThroughStartupTriggersOnBothSides()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        // Device B already has its own monthly default before the override arrives (spec Part 27).
        await b.ErtConfiguration.SaveMonthlyAsync(new MonthlyErtDraft(
            2026, 9, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Fury 325", "Copperhead Strike"], null));

        await a.ErtConfiguration.SaveOverrideAsync(new ErtDateOverrideDraft(
            OperatingDate, ErtConfigurationState.Configured, new TimeOnly(10, 0), ["Copperhead Strike"], "Fury maintenance"));

        a.Coordinator.Start();
        await WaitForAsync(() => a.Coordinator.CurrentStatus.LastResultSummary?.Contains("1 exported") == true);

        b.Coordinator.Start();
        await WaitForAsync(() => b.Coordinator.CurrentStatus.LastResultSummary?.Contains("1 imported") == true);

        var imported = await b.ErtOverrides.GetAsync(OperatingDate);
        Assert.NotNull(imported);
        Assert.Equal(new TimeOnly(10, 0), imported!.StartTime);
        Assert.Equal("Fury maintenance", imported.Reason);

        var effective = await new EffectiveErtResolver(b.MonthlyErts, b.ErtOverrides).ResolveAsync(OperatingDate);
        Assert.Equal(EffectiveErtSource.Override, effective.Source);
        Assert.Equal(new TimeOnly(10, 0), effective.StartTime);
        Assert.Equal(0, b.Coordinator.CurrentStatus.ConflictCount);

        await a.Coordinator.StopAsync();
        await b.Coordinator.StopAsync();
    }

    private sealed class FakeDeviceIdProvider(Guid fixedId) : IDeviceIdProvider
    {
        public Guid GetOrCreateDeviceId() => fixedId;
    }

    private sealed class FakeSyncConfigurationStore : ISyncConfigurationStore
    {
        public SyncConfiguration Current { get; set; } = SyncConfiguration.Disabled;

        public Task<SyncConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task SaveAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default)
        {
            Current = configuration;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOneDriveLocationSuggestionProvider : IOneDriveLocationSuggestionProvider
    {
        public IReadOnlyList<string> GetSuggestedRoots() => [];
    }

    /// <summary>
    /// Local equivalent of Application.Tests' internal <c>ManualDelayScheduler</c> — duplicated rather than
    /// shared because that type is internal to a different test assembly with no <c>InternalsVisibleTo</c>
    /// grant to this one (Phase 16G orientation finding). Same shape: records every delay request instead
    /// of waiting, and a test elapses a specific pending delay by the exact interval it was scheduled with,
    /// so debounce/periodic/retry chains on each independent <see cref="PersonalSyncCoordinator"/> instance
    /// advance deterministically with no real sleep.
    /// </summary>
    private sealed class TestDelayScheduler
    {
        private readonly List<(TimeSpan Interval, TaskCompletionSource Tcs)> _pending = [];
        private readonly Lock _lock = new();

        public Task Delay(TimeSpan interval, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource();
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            lock (_lock)
            {
                _pending.Add((interval, tcs));
            }

            return tcs.Task;
        }

        public bool Elapse(TimeSpan interval)
        {
            TaskCompletionSource? target = null;
            lock (_lock)
            {
                for (var i = _pending.Count - 1; i >= 0; i--)
                {
                    if (_pending[i].Interval == interval && _pending[i].Tcs.Task.Status == TaskStatus.WaitingForActivation)
                    {
                        target = _pending[i].Tcs;
                        break;
                    }
                }
            }

            return target?.TrySetResult() ?? false;
        }
    }
}
