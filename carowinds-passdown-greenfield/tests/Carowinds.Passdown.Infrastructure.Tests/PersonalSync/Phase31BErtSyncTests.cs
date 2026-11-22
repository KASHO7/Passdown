using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
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
/// Phase 31B: order-independent Early Ride Time comparison and the editable ERT conflict resolution,
/// across two and three simulated computers — each a real, independent SQLite database pointed at one
/// shared temp folder (the same harness shape as <see cref="PersonalSyncServiceTests"/>).
/// </summary>
public sealed class Phase31BErtSyncTests : IDisposable
{
    private readonly string _sharedRoot = Path.Combine(Path.GetTempPath(), $"carowinds-31b-shared-{Guid.NewGuid():N}");
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"carowinds-31b-devices-{Guid.NewGuid():N}");
    private static readonly Guid DeviceAId = Guid.Parse("aaaaaaaa-3131-0000-0000-00000000000a");
    private static readonly Guid DeviceBId = Guid.Parse("bbbbbbbb-3131-0000-0000-00000000000b");
    private static readonly Guid DeviceCId = Guid.Parse("cccccccc-3131-0000-0000-00000000000c");
    private static readonly DateTimeOffset BaseNow = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly string NovemberId = SyncEntityIdentity.ForMonthlyErt(2026, 11);
    private static readonly DateOnly OverrideDate = new(2026, 11, 28);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _sharedRoot, _dataRoot })
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private sealed record Device(
        IPersonalSyncService Sync,
        IPersonalSyncConflictResolutionService Resolution,
        IErtConfigurationService Ert,
        IMonthlyErtRepository MonthlyErts,
        IErtDateOverrideRepository Overrides,
        ISyncConflictRepository Conflicts,
        ISyncEntityStateRepository States,
        IAssociateDraftRepository Drafts,
        FakeClock Clock);

    private Device CreateDevice(string name, Guid deviceId, FakeClock clock)
    {
        Directory.CreateDirectory(_dataRoot);
        var connectionFactory = new SqliteConnectionFactory(Path.Combine(_dataRoot, $"{name}.db"));
        IReadOnlyList<IMigration> migrations =
        [
            new Migration0001CreateSchemaVersionTable(), new Migration0002CreateSourceStateTable(), new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(), new Migration0005CreateProductHistoryTable(), new Migration0006CreateAssociateDraftsTable(),
            new Migration0007CreateManagerDraftsTable(), new Migration0008CreateFinalizedPassdownsTable(), new Migration0009AddFinalizedPassdownPdfColumns(),
            new Migration0010CreateOperationalNotesTable(), new Migration0011CreateDeepRefreshStatusTable(), new Migration0012CreateSyncEntityStateTable(),
            new Migration0013CreateSyncConflictsTable(), new Migration0014CreateMonthlyErtConfigurationsTable(), new Migration0015CreateErtDateOverridesTable(),
        ];
        new MigrationRunner(connectionFactory, migrations, NullLogger<MigrationRunner>.Instance).Run();

        var drafts = new SqliteAssociateDraftRepository(connectionFactory);
        var notes = new SqliteOperationalNoteRepository(connectionFactory);
        var finalized = new SqliteFinalizedPassdownRepository(connectionFactory);
        var states = new SqliteSyncEntityStateRepository(connectionFactory);
        var conflicts = new SqliteSyncConflictRepository(connectionFactory);
        var unitOfWorkFactory = new SqliteUnitOfWorkFactory(connectionFactory);
        var tracker = new SyncChangeTracker(states, clock);
        var monthly = new SqliteMonthlyErtRepository(connectionFactory);
        var overrides = new SqliteErtDateOverrideRepository(connectionFactory);
        var ert = new ErtConfigurationService(monthly, overrides, clock, tracker);
        var deviceIdProvider = new FakeDeviceIdProvider(deviceId);
        var configStore = new FakeSyncConfigurationStore { Current = new SyncConfiguration(true, _sharedRoot) };
        var settings = new PersonalSyncSettingsService(configStore, new SyncFolderValidator(), new FakeOneDriveLocationSuggestionProvider(), deviceIdProvider, clock);
        var sync = new PersonalSyncService(settings, configStore, deviceIdProvider, drafts, notes, finalized, states, conflicts, clock, monthly, overrides);
        var resolution = new PersonalSyncConflictResolutionService(settings, deviceIdProvider, drafts, notes, states, conflicts, unitOfWorkFactory, clock, monthly, overrides);
        return new Device(sync, resolution, ert, monthly, overrides, conflicts, states, drafts, clock);
    }

    private static Task SaveNovemberAsync(Device device, params string[] rides) =>
        device.Ert.SaveMonthlyAsync(new MonthlyErtDraft(2026, 11, ErtConfigurationState.Configured, new TimeOnly(9, 0), rides, Note: null));

    private static Task SaveOverrideAsync(Device device, params string[] rides) =>
        device.Ert.SaveOverrideAsync(new ErtDateOverrideDraft(OverrideDate, ErtConfigurationState.Configured, new TimeOnly(8, 30), rides, Reason: "Black Friday"));

    private static async Task<IReadOnlyList<string>> NovemberRidesAsync(Device device) =>
        (await device.MonthlyErts.GetAsync(2026, 11))!.Rides;

    [Fact]
    public async Task SameRidesInADifferentOrder_OnTwoComputers_ConvergeWithoutAConflict_AndEachKeepsItsOwnOrder()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await SaveNovemberAsync(a, "Copperhead Strike", "Mountain Gliders");
        await SaveNovemberAsync(b, "Mountain Gliders", "Copperhead Strike");

        await a.Sync.RunAsync();
        var resultB = await b.Sync.RunAsync();
        var resultA = await a.Sync.RunAsync();

        Assert.Equal(0, resultB.ConflictCount);
        Assert.Equal(0, resultA.ConflictCount);
        Assert.Empty(await a.Conflicts.ListAsync());
        Assert.Empty(await b.Conflicts.ListAsync());
        Assert.Equal(["Copperhead Strike", "Mountain Gliders"], await NovemberRidesAsync(a));
        Assert.Equal(["Mountain Gliders", "Copperhead Strike"], await NovemberRidesAsync(b));
    }

    [Fact]
    public async Task OrderOnlyDifference_IsAlsoConvergedForDateOverrides_EvenWithDifferentEditTimesAndCase()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddHours(3)));
        await SaveOverrideAsync(a, "Copperhead Strike", "Mountain Gliders");
        await SaveOverrideAsync(b, "  mountain   gliders ", "COPPERHEAD STRIKE");

        await a.Sync.RunAsync();
        var result = await b.Sync.RunAsync();

        Assert.Equal(0, result.ConflictCount);
        Assert.Empty(await b.Conflicts.ListAsync());
    }

    [Fact]
    public async Task RepeatedPasses_AfterOrderOnlyConvergence_AreIdempotent_AndNeverReexportOrConflict()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await SaveNovemberAsync(a, "Copperhead Strike", "Mountain Gliders");
        await SaveNovemberAsync(b, "Mountain Gliders", "Copperhead Strike");
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        for (var pass = 0; pass < 3; pass++)
        {
            var ra = await a.Sync.RunAsync();
            var rb = await b.Sync.RunAsync();
            Assert.Equal(0, ra.ConflictCount + rb.ConflictCount);
            Assert.Equal(0, ra.ExportedCount + rb.ExportedCount);
        }
    }

    [Fact]
    public async Task ThreeComputers_WithTheSameRidesInThreeOrders_AllConverge()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));
        var c = CreateDevice("c", DeviceCId, new FakeClock(BaseNow.AddMinutes(2)));
        await SaveNovemberAsync(a, "Copperhead Strike", "Mountain Gliders", "Fury 325");
        await SaveNovemberAsync(b, "Fury 325", "Copperhead Strike", "Mountain Gliders");
        await SaveNovemberAsync(c, "Mountain Gliders", "Fury 325", "copperhead strike");

        var conflicts = 0;
        for (var round = 0; round < 2; round++)
        {
            foreach (var device in new[] { a, b, c })
            {
                conflicts += (await device.Sync.RunAsync()).ConflictCount;
            }
        }

        Assert.Equal(0, conflicts);
        foreach (var device in new[] { a, b, c })
        {
            Assert.Empty(await device.Conflicts.ListAsync());
            Assert.True(ErtRideSelection.SameSelection(["Copperhead Strike", "Mountain Gliders", "Fury 325"], await NovemberRidesAsync(device)));
        }
    }

    [Fact]
    public async Task GenuinelyDifferentRides_AreStillAConflict()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await SaveNovemberAsync(a, "Copperhead Strike", "Mountain Gliders");
        await SaveNovemberAsync(b, "Copperhead Strike", "Fury 325");

        await a.Sync.RunAsync();
        var result = await b.Sync.RunAsync();

        Assert.Equal(1, result.ConflictCount);
        var detail = await b.Resolution.GetErtConflictDetailAsync(SyncEntityType.MonthlyErt, NovemberId);
        Assert.NotNull(detail);
        Assert.True(detail!.RidesDiffer);
        Assert.Equal(["Copperhead Strike", "Fury 325", "Mountain Gliders"], detail.SuggestedMergedRides);
        Assert.Contains(detail.BuildFieldComparisons(), f => f.Label == "Rides" && f.Differs);
    }

    [Fact]
    public async Task SameRidesInADifferentOrder_ButADifferentStartTime_IsStillAConflict_AboutTheStartTimeOnly()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await SaveNovemberAsync(a, "Copperhead Strike", "Mountain Gliders");
        await b.Ert.SaveMonthlyAsync(new MonthlyErtDraft(2026, 11, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Mountain Gliders", "Copperhead Strike"], Note: null));

        await a.Sync.RunAsync();
        var result = await b.Sync.RunAsync();

        Assert.Equal(1, result.ConflictCount);
        var detail = await b.Resolution.GetErtConflictDetailAsync(SyncEntityType.MonthlyErt, NovemberId);
        Assert.NotNull(detail);
        Assert.False(detail!.RidesDiffer); // the ride order alone is not a difference
        Assert.True(detail.OtherFieldsDiffer);
    }

    [Fact]
    public async Task EditedMergedRides_BecomeTheResolvedValue_AndSyncToTheOtherComputer()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await SaveNovemberAsync(a, "Copperhead Strike", "Mountain Gliders");
        await SaveNovemberAsync(b, "Copperhead Strike", "Fury 325");
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var outcome = await b.Resolution.ResolveWithEditedErtRidesAsync(
            SyncEntityType.MonthlyErt, NovemberId,
            new ErtConflictEditedResolution(ErtResolutionBase.ThisDevice, ["Fury 325", "Copperhead Strike", "Mountain Gliders", "fury 325"]));

        Assert.Equal(ConflictResolutionOutcome.Resolved, outcome.Outcome);
        Assert.Empty(await b.Conflicts.ListAsync());
        Assert.Equal(["Fury 325", "Copperhead Strike", "Mountain Gliders"], await NovemberRidesAsync(b));

        var importA = await a.Sync.RunAsync();
        Assert.Equal(0, importA.ConflictCount);
        Assert.Equal(["Fury 325", "Copperhead Strike", "Mountain Gliders"], await NovemberRidesAsync(a));

        // Converged: further passes on both are no-ops.
        Assert.Equal(0, (await b.Sync.RunAsync()).ConflictCount + (await a.Sync.RunAsync()).ConflictCount);
    }

    [Fact]
    public async Task EditedResolution_CanKeepTheOtherComputersStartTime_WithTheEditedRides()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await a.Ert.SaveMonthlyAsync(new MonthlyErtDraft(2026, 11, ErtConfigurationState.Configured, new TimeOnly(9, 30), ["Copperhead Strike"], "Holiday hours"));
        await SaveNovemberAsync(b, "Fury 325");
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var detail = await b.Resolution.GetErtConflictDetailAsync(SyncEntityType.MonthlyErt, NovemberId);
        Assert.True(detail!.OtherFieldsDiffer);

        await b.Resolution.ResolveWithEditedErtRidesAsync(SyncEntityType.MonthlyErt, NovemberId,
            new ErtConflictEditedResolution(ErtResolutionBase.OtherDevice, ["Copperhead Strike", "Fury 325"]));

        var resolved = await b.MonthlyErts.GetAsync(2026, 11);
        Assert.Equal(new TimeOnly(9, 30), resolved!.StartTime);
        Assert.Equal("Holiday hours", resolved.Note);
        Assert.Equal(["Copperhead Strike", "Fury 325"], resolved.Rides);
    }

    [Fact]
    public async Task RestartingDuringAConflict_KeepsTheConflict_AndItCanBeResolvedAfterRestart()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await SaveOverrideAsync(a, "Copperhead Strike");
        await SaveOverrideAsync(b, "Fury 325");
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        // "Restart": brand-new services over the same database files.
        var restartedB = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddHours(1)));
        var overrideId = SyncEntityIdentity.ForErtDateOverride(OverrideDate);
        Assert.Single(await restartedB.Conflicts.ListAsync());
        Assert.Equal(0, (await restartedB.Sync.RunAsync()).ImportedCount);
        Assert.Single(await restartedB.Conflicts.ListAsync()); // re-scans never drop it

        var detail = await restartedB.Resolution.GetErtConflictDetailAsync(SyncEntityType.ErtDateOverride, overrideId);
        Assert.Equal(["Fury 325"], detail!.Local!.Rides);
        Assert.Equal(["Copperhead Strike"], detail.Remote!.Rides);

        var outcome = await restartedB.Resolution.ResolveWithEditedErtRidesAsync(SyncEntityType.ErtDateOverride, overrideId,
            new ErtConflictEditedResolution(ErtResolutionBase.ThisDevice, detail.SuggestedMergedRides));
        Assert.Equal(ConflictResolutionOutcome.Resolved, outcome.Outcome);
        Assert.Empty(await restartedB.Conflicts.ListAsync());
        await a.Sync.RunAsync();
        Assert.Equal(["Fury 325", "Copperhead Strike"], (await a.Overrides.GetAsync(OverrideDate))!.Rides);
    }

    [Fact]
    public async Task EditedResolution_WhenTheSyncFolderIsUnavailable_ChangesNothing_AndKeepsTheConflict()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await SaveNovemberAsync(a, "Copperhead Strike");
        await SaveNovemberAsync(b, "Fury 325");
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        // Obstruct the export target (same technique as the Phase 16D resolution tests).
        var obstructedPath = SyncPathService.GetMonthlyErtFilePath(_sharedRoot, 2026, 11);
        File.Delete(obstructedPath);
        Directory.CreateDirectory(obstructedPath);

        var outcome = await b.Resolution.ResolveWithEditedErtRidesAsync(SyncEntityType.MonthlyErt, NovemberId,
            new ErtConflictEditedResolution(ErtResolutionBase.ThisDevice, ["Copperhead Strike", "Fury 325"]));

        Assert.Equal(ConflictResolutionOutcome.ExportUnavailable, outcome.Outcome);
        Assert.Single(await b.Conflicts.ListAsync());
        Assert.Equal(["Fury 325"], await NovemberRidesAsync(b));

        Directory.Delete(obstructedPath);
        var retry = await b.Resolution.ResolveWithEditedErtRidesAsync(SyncEntityType.MonthlyErt, NovemberId,
            new ErtConflictEditedResolution(ErtResolutionBase.ThisDevice, ["Copperhead Strike", "Fury 325"]));
        Assert.Equal(ConflictResolutionOutcome.Resolved, retry.Outcome);
        Assert.Equal(["Copperhead Strike", "Fury 325"], await NovemberRidesAsync(b));
    }

    [Fact]
    public async Task EditedResolution_RejectsAddingRidesToANoErtBase_WithoutChangingAnything()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await a.Ert.SaveMonthlyAsync(new MonthlyErtDraft(2026, 11, ErtConfigurationState.NoErt, null, [], null));
        await SaveNovemberAsync(b, "Fury 325");
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var outcome = await b.Resolution.ResolveWithEditedErtRidesAsync(SyncEntityType.MonthlyErt, NovemberId,
            new ErtConflictEditedResolution(ErtResolutionBase.OtherDevice, ["Fury 325"]));

        Assert.Equal(ConflictResolutionOutcome.WrongActionForConflict, outcome.Outcome);
        Assert.Single(await b.Conflicts.ListAsync());
    }

    [Fact]
    public async Task ResolvingAnErtConflict_NeverTouchesTheSameDaysDraft()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        var draftJson = JsonSerializer.Serialize(new { DailyFeatureTitle = "Keep me" });
        await b.Drafts.SaveAsync(new AssociateDraft(OverrideDate, AssociateDraftPayload.CurrentSchemaVersion, draftJson, BaseNow));
        await SaveOverrideAsync(a, "Copperhead Strike");
        await SaveOverrideAsync(b, "Fury 325");
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        await b.Resolution.ResolveWithEditedErtRidesAsync(SyncEntityType.ErtDateOverride, SyncEntityIdentity.ForErtDateOverride(OverrideDate),
            new ErtConflictEditedResolution(ErtResolutionBase.ThisDevice, ["Fury 325", "Copperhead Strike"]));

        var draft = await b.Drafts.GetAsync(OverrideDate);
        Assert.Equal(draftJson, draft!.PayloadJson);
    }

    [Fact]
    public async Task AnOrderOnlyConflictRecordedBeforePhase31B_IsClearedOnTheNextPass()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await SaveNovemberAsync(a, "Copperhead Strike", "Mountain Gliders");
        await SaveNovemberAsync(b, "Mountain Gliders", "Copperhead Strike");
        await a.Sync.RunAsync();

        // Simulate the pre-31B byte-hash detection: the order-only difference stored as a conflict on B.
        var remote = MonthlyErtSyncPayload.FromDomain((await a.MonthlyErts.GetAsync(2026, 11))!);
        await b.Conflicts.UpsertAsync(new SyncConflictRecord(
            SyncEntityType.MonthlyErt, NovemberId, DeviceAId, 1, BaseNow, false, MonthlyErtSyncPayload.CurrentSchemaVersion,
            MonthlyErtSyncPayloadSerializer.Serialize(remote), SyncContentHash.ComputeMonthlyErtHash(remote), BaseNow, null, null, null));
        await b.States.UpsertAsync(SyncEntityState.CreateNew(SyncEntityType.MonthlyErt, NovemberId, BaseNow) with
        {
            ConflictState = SyncConflictState.Conflicting,
            ConflictRemoteDeviceId = DeviceAId,
            ConflictRemoteRevision = 1,
        });

        var result = await b.Sync.RunAsync();

        Assert.Equal(0, result.ConflictCount);
        Assert.Empty(await b.Conflicts.ListAsync());
        Assert.Equal(["Mountain Gliders", "Copperhead Strike"], await NovemberRidesAsync(b));
    }

    [Fact]
    public async Task AConflictWithAnotherComputersDifferentRides_IsPreserved_WhenTheSharedFileNowAgrees()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await SaveNovemberAsync(a, "Copperhead Strike", "Mountain Gliders");
        await SaveNovemberAsync(b, "Mountain Gliders", "Copperhead Strike");
        await a.Sync.RunAsync();

        // An unresolved conflict from computer C with genuinely different rides.
        var fromC = new MonthlyErtSyncPayload(2026, 11, ErtConfigurationState.Configured, new TimeOnly(9, 0), ["Fury 325"], null, BaseNow);
        await b.Conflicts.UpsertAsync(new SyncConflictRecord(
            SyncEntityType.MonthlyErt, NovemberId, DeviceCId, 4, BaseNow, false, MonthlyErtSyncPayload.CurrentSchemaVersion,
            MonthlyErtSyncPayloadSerializer.Serialize(fromC), SyncContentHash.ComputeMonthlyErtHash(fromC), BaseNow, null, null, null));

        await b.Sync.RunAsync();

        var kept = Assert.Single(await b.Conflicts.ListAsync());
        Assert.Equal(DeviceCId, kept.RemoteDeviceId);
    }

    [Fact]
    public async Task ReorderingRidesLater_OnOneComputer_DoesNotReorderTheOther()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(5)));
        await SaveNovemberAsync(a, "Copperhead Strike", "Mountain Gliders");
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        await SaveNovemberAsync(b, "Mountain Gliders", "Copperhead Strike"); // an offline reorder on B
        await b.Sync.RunAsync();
        var resultA = await a.Sync.RunAsync();

        Assert.Equal(0, resultA.ConflictCount);
        Assert.Equal(["Copperhead Strike", "Mountain Gliders"], await NovemberRidesAsync(a));
        Assert.Equal(["Mountain Gliders", "Copperhead Strike"], await NovemberRidesAsync(b));
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
}
