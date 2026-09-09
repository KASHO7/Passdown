using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>
/// Phase 16C §30/§31: the two-device simulation harness — <c>LocalStoreA</c>/<c>LocalStoreB</c> are
/// two real, independent SQLite databases; <c>SharedSyncFolder</c> is one real temp directory both
/// "devices" are pointed at, standing in for a synced OneDrive folder without a real OneDrive client.
/// Every scenario the phase spec calls out by letter (§31 A-G) and by name (§30) is covered here —
/// these are more important than any UI test in this phase (§31).
/// </summary>
public class PersonalSyncServiceTests : IDisposable
{
    private readonly string _sharedRoot = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-shared-{Guid.NewGuid():N}");
    private readonly string _tempDataRoot = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-devices-{Guid.NewGuid():N}");
    private static readonly Guid DeviceAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DeviceBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateOnly OperatingDate = new(2026, 9, 9);
    private static readonly DateTimeOffset BaseNow = new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);

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
        ISyncEntityStateRepository States,
        ISyncConflictRepository Conflicts,
        ISyncChangeTracker Tracker,
        IPersonalSyncService Sync,
        IPersonalSyncConflictResolutionService Resolution,
        IUnitOfWorkFactory UnitOfWorkFactory,
        FakeClock Clock,
        Guid DeviceId);

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
        ];
        new MigrationRunner(connectionFactory, migrations, NullLogger<MigrationRunner>.Instance).Run();

        var drafts = new SqliteAssociateDraftRepository(connectionFactory);
        var noteRepository = new SqliteOperationalNoteRepository(connectionFactory);
        var states = new SqliteSyncEntityStateRepository(connectionFactory);
        var conflicts = new SqliteSyncConflictRepository(connectionFactory);
        var unitOfWorkFactory = new SqliteUnitOfWorkFactory(connectionFactory);
        var tracker = new SyncChangeTracker(states, clock);
        var notes = new OperationalNotesService(noteRepository, clock, tracker);

        var deviceIdProvider = new FakeDeviceIdProvider(deviceId);
        var settingsService = new PersonalSyncSettingsService(
            new FakeSyncConfigurationStore { Current = new SyncConfiguration(true, _sharedRoot) },
            new SyncFolderValidator(),
            new FakeOneDriveLocationSuggestionProvider(),
            deviceIdProvider,
            clock);

        var sync = new PersonalSyncService(settingsService, deviceIdProvider, drafts, noteRepository, states, conflicts, clock);
        var resolution = new PersonalSyncConflictResolutionService(settingsService, deviceIdProvider, drafts, noteRepository, states, conflicts, unitOfWorkFactory, clock);

        return new TestDevice(drafts, notes, noteRepository, states, conflicts, tracker, sync, resolution, unitOfWorkFactory, clock, deviceId);
    }

    private static Task SaveDraftAsync(TestDevice device, DateOnly date, AssociateDraftPayload payload)
    {
        var record = new AssociateDraft(date, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(payload), device.Clock.UtcNow);
        return SaveAndMarkAsync(device, date, record);
    }

    private static async Task SaveAndMarkAsync(TestDevice device, DateOnly date, AssociateDraft record)
    {
        await device.Drafts.SaveAsync(record);
        await device.Tracker.MarkLocalChangeAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(date));
    }

    /// <summary>
    /// Phase 16D §13: the two-step "genuine local delete" pattern — repository delete, then the same
    /// <see cref="ISyncChangeTracker.MarkLocalChangeAsync"/> hook every other local write already goes
    /// through (spec §7's "save SQLite first, sync metadata second"). No product UI calls this (§12) —
    /// exercised directly here per §23's "test tombstone/reconciliation primitives directly without
    /// inventing product UI."
    /// </summary>
    private static async Task DeleteDraftAsync(TestDevice device, DateOnly date)
    {
        await device.Drafts.DeleteAsync(date);
        await device.Tracker.MarkLocalChangeAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(date));
    }

    private static async Task DeleteNoteAsync(TestDevice device, Guid noteId)
    {
        await device.NoteRepository.DeleteAsync(noteId);
        await device.Tracker.MarkLocalChangeAsync(SyncEntityType.OperationalNote, SyncEntityIdentity.ForNote(noteId));
    }

    // ------------------------------------------------------------------
    // §31.A: A creates a draft -> B imports it.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScenarioA_DeviceACreatesADraft_DeviceBImportsIt()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 12000 });
        var exportResult = await a.Sync.RunAsync();
        Assert.Equal(1, exportResult.ExportedCount);

        var importResult = await b.Sync.RunAsync();

        Assert.Equal(1, importResult.ImportedCount);
        Assert.Equal(0, importResult.ConflictCount);
        var imported = await b.Drafts.GetAsync(OperatingDate);
        Assert.NotNull(imported);
        Assert.Equal(12000, AssociateDraftPayloadSerializer.Deserialize(imported!.PayloadJson).ProjectedAttendance);
    }

    // ------------------------------------------------------------------
    // §31.B: A creates a note -> B imports it, preserving the exact Guid.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScenarioB_DeviceACreatesANote_DeviceBImportsIt_PreservingItsGuid()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Gate 3 scanner down", "Use gate 2.", OperationalNoteCategory.Equipment, OperationalNotePriority.Important, OperatingDate, null));
        await a.Sync.RunAsync();

        var importResult = await b.Sync.RunAsync();

        Assert.Equal(1, importResult.ImportedCount);
        var importedNote = await b.NoteRepository.GetByIdAsync(created.Id);
        Assert.NotNull(importedNote);
        Assert.Equal(created.Id, importedNote!.Id);
        Assert.Equal("Gate 3 scanner down", importedNote.Title);
    }

    // ------------------------------------------------------------------
    // §31.C: A and B create different, non-overlapping records -> both converge without conflict.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScenarioC_NonOverlappingChangesOnTwoDevices_Converge()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));
        var otherDate = OperatingDate.AddDays(1);

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await SaveDraftAsync(b, otherDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 200 });

        await a.Sync.RunAsync();
        await b.Sync.RunAsync(); // imports A's date, exports B's own date
        await a.Sync.RunAsync(); // imports B's date

        Assert.NotNull(await a.Drafts.GetAsync(OperatingDate));
        Assert.NotNull(await a.Drafts.GetAsync(otherDate));
        Assert.NotNull(await b.Drafts.GetAsync(OperatingDate));
        Assert.NotNull(await b.Drafts.GetAsync(otherDate));
    }

    // ------------------------------------------------------------------
    // §31.D: A and B edit the same draft concurrently -> conflict, neither side overwritten.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScenarioD_ConcurrentEditsToTheSameDraft_DetectedAsConflict_NeitherSideOverwritten()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        // Establish a shared baseline.
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        // Both devices edit the same date before either re-syncs.
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 500 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 });

        await a.Sync.RunAsync(); // A exports its revision 2
        var bResult = await b.Sync.RunAsync(); // B sees A's newer file but has also diverged -> conflict

        Assert.Equal(1, bResult.ConflictCount);
        Assert.Equal(0, bResult.ExportedCount); // B must not export over the conflict

        var bLocal = AssociateDraftPayloadSerializer.Deserialize((await b.Drafts.GetAsync(OperatingDate))!.PayloadJson);
        Assert.Equal(999, bLocal.ProjectedAttendance); // B's own copy preserved, never silently overwritten

        var bState = await b.States.GetAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(OperatingDate));
        Assert.Equal(SyncConflictState.Conflicting, bState!.ConflictState);
        Assert.Equal(DeviceAId, bState.ConflictRemoteDeviceId);

        // The remote file (A's) must also survive untouched — B never exported over it.
        var remoteEnvelope = SyncEnvelopeSerializer.Deserialize(await File.ReadAllTextAsync(SyncPathService.GetDraftFilePath(_sharedRoot, OperatingDate)));
        var remotePayload = AssociateDraftPayloadSerializer.Deserialize(remoteEnvelope.Payload.GetRawText());
        Assert.Equal(500, remotePayload.ProjectedAttendance);
    }

    // ------------------------------------------------------------------
    // §31.E: A and B edit the same note concurrently -> conflict, neither side overwritten.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScenarioE_ConcurrentEditsToTheSameNote_DetectedAsConflict_NeitherSideOverwritten()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Original", "Body", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        await a.Notes.UpdateAsync(created.Id, new OperationalNoteDraft("Edited by A", "Body", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await b.Notes.UpdateAsync(created.Id, new OperationalNoteDraft("Edited by B", "Body", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));

        await a.Sync.RunAsync();
        var bResult = await b.Sync.RunAsync();

        Assert.Equal(1, bResult.ConflictCount);
        var bLocal = await b.NoteRepository.GetByIdAsync(created.Id);
        Assert.Equal("Edited by B", bLocal!.Title); // B's own edit preserved
    }

    // ------------------------------------------------------------------
    // §31.F / §15: repeated sync passes with no changes must be stable/idempotent.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScenarioF_RepeatedSyncPassesWithNoChanges_AreIdempotent()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        // Repeated passes on both sides with nothing new must import/export nothing further.
        var aSecond = await a.Sync.RunAsync();
        var bSecond = await b.Sync.RunAsync();
        var bThird = await b.Sync.RunAsync();

        Assert.Equal(0, aSecond.ImportedCount);
        Assert.Equal(0, aSecond.ExportedCount);
        Assert.Equal(0, bSecond.ImportedCount);
        Assert.Equal(0, bSecond.ExportedCount);
        Assert.Equal(0, bThird.ImportedCount);
        Assert.Equal(0, bThird.ExportedCount);

        var noteCountAfterRepeats = (await a.Drafts.ListSavedDraftDatesAsync()).Count;
        Assert.Equal(1, noteCountAfterRepeats); // No duplicate rows ever created.
    }

    // ------------------------------------------------------------------
    // §31.G: B is offline while A changes twice; B reconnects later and picks up the final state directly.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScenarioG_OfflineDeviceReconnectsLater_PicksUpTheLatestStateDirectly()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        // B stays offline (never syncs) while A edits again.
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 250 });
        await a.Sync.RunAsync();

        var bResult = await b.Sync.RunAsync();

        Assert.Equal(1, bResult.ImportedCount);
        Assert.Equal(0, bResult.ConflictCount);
        var bLocal = AssociateDraftPayloadSerializer.Deserialize((await b.Drafts.GetAsync(OperatingDate))!.PayloadJson);
        Assert.Equal(250, bLocal.ProjectedAttendance);
    }

    // ------------------------------------------------------------------
    // Additional §30 failure/recovery coverage.
    // ------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_SyncDisabled_TouchesNoFilesAndReportsDisabled()
    {
        var device = CreateDevice("solo", DeviceAId, new FakeClock(BaseNow));
        var disabledSettings = new PersonalSyncSettingsService(
            new FakeSyncConfigurationStore { Current = SyncConfiguration.Disabled },
            new SyncFolderValidator(),
            new FakeOneDriveLocationSuggestionProvider(),
            new FakeDeviceIdProvider(DeviceAId),
            new FakeClock(BaseNow));
        var sync = new PersonalSyncService(disabledSettings, new FakeDeviceIdProvider(DeviceAId), device.Drafts, device.NoteRepository, device.States, device.Conflicts, new FakeClock(BaseNow));

        var result = await sync.RunAsync();

        Assert.Equal(PersonalSyncOutcome.Disabled, result.Outcome);
        Assert.False(Directory.Exists(_sharedRoot));
    }

    [Fact]
    public async Task RunAsync_FolderUnavailable_ReportsFolderUnavailable_AndLocalDraftStillSaves()
    {
        var device = CreateDevice("solo", DeviceAId, new FakeClock(BaseNow));
        var unavailableSettings = new PersonalSyncSettingsService(
            new FakeSyncConfigurationStore { Current = new SyncConfiguration(true, Path.Combine(_sharedRoot, "does", "not", "exist")) },
            new FailingSyncFolderValidator(),
            new FakeOneDriveLocationSuggestionProvider(),
            new FakeDeviceIdProvider(DeviceAId),
            new FakeClock(BaseNow));
        var sync = new PersonalSyncService(unavailableSettings, new FakeDeviceIdProvider(DeviceAId), device.Drafts, device.NoteRepository, device.States, device.Conflicts, new FakeClock(BaseNow));

        // Local save must succeed regardless of sync folder health (§1).
        await SaveDraftAsync(device, OperatingDate, AssociateDraftPayload.CreateDefault());
        Assert.NotNull(await device.Drafts.GetAsync(OperatingDate));

        var result = await sync.RunAsync();

        Assert.Equal(PersonalSyncOutcome.FolderUnavailable, result.Outcome);
    }

    [Fact]
    public async Task RunAsync_MalformedOneRecordAmongValidOnes_ProcessesTheOthersAndReportsMalformedCount()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 111 });
        await SaveDraftAsync(a, OperatingDate.AddDays(1), AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 222 });
        await a.Sync.RunAsync();

        // Corrupt one of the two exported files.
        await File.WriteAllTextAsync(SyncPathService.GetDraftFilePath(_sharedRoot, OperatingDate), "{ not valid json");

        var result = await b.Sync.RunAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.MalformedCount);
        Assert.NotNull(await b.Drafts.GetAsync(OperatingDate.AddDays(1)));
        Assert.Null(await b.Drafts.GetAsync(OperatingDate)); // The malformed file's entity was never touched.
    }

    [Fact]
    public async Task RunAsync_IoFailureDuringOneExport_LeavesTheEntityDirtyForRetry_AndOtherExportsStillSucceed()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 1 });
        await SaveDraftAsync(a, OperatingDate.AddDays(1), AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 2 });

        // Obstruct the first date's export target by pre-creating a directory at its exact file path —
        // AtomicFileWriter's rename-into-place will fail with an IOException.
        var obstructedPath = SyncPathService.GetDraftFilePath(_sharedRoot, OperatingDate);
        Directory.CreateDirectory(obstructedPath);

        var firstAttempt = await a.Sync.RunAsync();

        Assert.Equal(1, firstAttempt.ExportedCount); // The unobstructed date still exports.
        Assert.Equal(1, firstAttempt.IoFailureCount);
        Assert.False(File.Exists(obstructedPath)); // Still a directory — no partial/corrupt file was left.

        // Clear the obstruction and retry.
        Directory.Delete(obstructedPath);
        var secondAttempt = await a.Sync.RunAsync();

        Assert.Equal(1, secondAttempt.ExportedCount); // The previously-failed date is retried and now succeeds.
        Assert.True(File.Exists(obstructedPath));
    }

    [Fact]
    public async Task RunAsync_ImportingTheSameEnvelopeTwice_NeverDuplicatesOrInflatesRevision()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 42 });
        await a.Sync.RunAsync();

        await b.Sync.RunAsync();
        var secondImport = await b.Sync.RunAsync();
        var thirdImport = await b.Sync.RunAsync();

        Assert.Equal(0, secondImport.ImportedCount);
        Assert.Equal(0, thirdImport.ImportedCount);
        Assert.Single(await b.Drafts.ListSavedDraftDatesAsync());
        var state = await b.States.GetAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(OperatingDate));
        Assert.Equal(1, state!.LocalRevision); // Never inflated by repeated no-op imports.
    }

    // ------------------------------------------------------------------
    // §16/§17: bootstrap — first device with pre-existing local data, and a second, non-empty device.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Bootstrap_FirstDeviceWithExistingLocalData_ExportsEverythingOnFirstSyncPass()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));

        // All of this was saved before sync was ever enabled — no sync metadata exists for any of it yet.
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 1 });
        await SaveDraftAsync(a, OperatingDate.AddDays(1), AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 2 });
        await a.Notes.CreateAsync(new OperationalNoteDraft("Pre-existing note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));

        var result = await a.Sync.RunAsync();

        Assert.Equal(3, result.ExportedCount);
        Assert.True(File.Exists(SyncPathService.GetDraftFilePath(_sharedRoot, OperatingDate)));
        Assert.True(File.Exists(SyncPathService.GetDraftFilePath(_sharedRoot, OperatingDate.AddDays(1))));
        Assert.Single(Directory.GetFiles(SyncPathService.GetNotesDirectoryPath(_sharedRoot)));
    }

    [Fact]
    public async Task Bootstrap_SecondDeviceWithNonOverlappingExistingData_MergesBothWaysWithoutConflict()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));
        var bOnlyDate = OperatingDate.AddDays(5);

        // Both devices already have independent, non-overlapping local data before sync is ever enabled.
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await SaveDraftAsync(b, bOnlyDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 200 });

        await a.Sync.RunAsync(); // A bootstraps: exports its own date.
        var bFirstPass = await b.Sync.RunAsync(); // B imports A's date and exports its own.
        var aSecondPass = await a.Sync.RunAsync(); // A imports B's date.

        Assert.Equal(1, bFirstPass.ImportedCount);
        Assert.Equal(1, bFirstPass.ExportedCount);
        Assert.Equal(0, bFirstPass.ConflictCount);
        Assert.Equal(1, aSecondPass.ImportedCount);

        Assert.NotNull(await a.Drafts.GetAsync(bOnlyDate));
        Assert.NotNull(await b.Drafts.GetAsync(OperatingDate));
    }

    [Fact]
    public async Task Bootstrap_SecondDeviceWithOverlappingDivergentData_DetectsConflict_NeverAutoPicksAWinner()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        // Both devices independently already had a draft for the SAME date before sync ever existed.
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 111 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 });

        await a.Sync.RunAsync();
        var bResult = await b.Sync.RunAsync();

        Assert.Equal(1, bResult.ConflictCount);
        Assert.Equal(0, bResult.ImportedCount);
        var bLocal = AssociateDraftPayloadSerializer.Deserialize((await b.Drafts.GetAsync(OperatingDate))!.PayloadJson);
        Assert.Equal(999, bLocal.ProjectedAttendance); // B's own pre-existing data was never overwritten.
    }

    [Fact]
    public async Task Bootstrap_SecondDeviceWithIdenticalOverlappingData_ConvergesSilently_NoConflict()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        // Both devices happen to already have the exact same content for the same date.
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 111 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 111 });

        await a.Sync.RunAsync();
        var bResult = await b.Sync.RunAsync();

        Assert.Equal(0, bResult.ConflictCount);
        Assert.Equal(0, bResult.ImportedCount); // Already identical — nothing to apply.
    }

    // ------------------------------------------------------------------
    // §34: privacy — the actual exported JSON must never leak device/machine identity or paths.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExportedSyncFiles_NeverContainMachineNameUsernameOrLocalPaths()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Notes.CreateAsync(new OperationalNoteDraft("Note", "Body", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));

        await a.Sync.RunAsync();

        var allText = string.Join('\n', Directory.GetFiles(_sharedRoot, "*.json", SearchOption.AllDirectories).Select(File.ReadAllText));

        Assert.DoesNotContain(Environment.UserName, allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_tempDataRoot, allText, StringComparison.OrdinalIgnoreCase); // The local AppData-equivalent path never appears.
        Assert.DoesNotContain("Token", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential", allText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_UnexpectedOneDriveConflictCopyFilename_IsIgnored_NeverImportedOrDeleted()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault());
        await a.Sync.RunAsync();

        var conflictCopyPath = Path.Combine(SyncPathService.GetDraftsDirectoryPath(_sharedRoot), "2026-09-09-DESKTOP1.json");
        await File.WriteAllTextAsync(conflictCopyPath, await File.ReadAllTextAsync(SyncPathService.GetDraftFilePath(_sharedRoot, OperatingDate)));

        var result = await b.Sync.RunAsync();

        Assert.Equal(1, result.ImportedCount); // Only the canonical file counted.
        Assert.True(File.Exists(conflictCopyPath)); // Left untouched — never deleted.
        Assert.Single(await b.Drafts.ListSavedDraftDatesAsync()); // Never imported as a second/duplicate entity.
    }

    [Fact]
    public async Task RunAsync_PreviouslySeenRemoteFileLaterDisappears_NeverDeletesOrTombstonesTheLocalNote()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Persisted note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        Assert.NotNull(await b.NoteRepository.GetByIdAsync(created.Id));

        File.Delete(SyncPathService.GetOperationalNoteFilePath(_sharedRoot, created.Id));

        var result = await b.Sync.RunAsync();

        Assert.Equal(0, result.ImportedCount);
        Assert.NotNull(await b.NoteRepository.GetByIdAsync(created.Id)); // Missing file never implied deletion (§29).
    }

    [Fact]
    public async Task RunAsync_LocalEditAfterACleanImport_ThenRemoteAdvances_IsDetectedAsConflictNotSilentlyOverwritten()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync(); // Clean import on B.

        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 777 }); // B edits locally after import.
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 888 }); // A also advances.
        await a.Sync.RunAsync();

        var result = await b.Sync.RunAsync();

        Assert.Equal(1, result.ConflictCount);
        var bLocal = AssociateDraftPayloadSerializer.Deserialize((await b.Drafts.GetAsync(OperatingDate))!.PayloadJson);
        Assert.Equal(777, bLocal.ProjectedAttendance);
    }

    // ====================================================================
    // Phase 16D §22: explicit conflict resolution — two-device scenarios A-F.
    // ====================================================================

    [Fact]
    public async Task ConflictResolution_A_ConcurrentEdit_DetectsConflict_AndPersistsTheFullRemotePayload()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 500 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var conflicts = await b.Resolution.GetConflictsAsync();
        Assert.Single(conflicts);
        Assert.False(conflicts[0].IsRemoteDeletion);

        var detail = await b.Resolution.GetDraftConflictDetailAsync(OperatingDate);
        Assert.NotNull(detail);
        Assert.Equal(999, detail!.Local!.ProjectedAttendance);
        Assert.Equal(500, detail.Remote!.ProjectedAttendance); // The full conflicting remote payload survived — not just a hash.
    }

    [Fact]
    public async Task ConflictResolution_B_KeepLocal_ExportsNewAuthoritativeRevision_AndOtherDeviceConverges()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 500 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var resolution = await b.Resolution.ResolveKeepLocalAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(OperatingDate));
        Assert.Equal(ConflictResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Empty(await b.Resolution.GetConflictsAsync());

        // B's own copy is unchanged locally...
        var bLocal = AssociateDraftPayloadSerializer.Deserialize((await b.Drafts.GetAsync(OperatingDate))!.PayloadJson);
        Assert.Equal(999, bLocal.ProjectedAttendance);

        // ...and A converges to it on the next sync (rebased, no further conflict).
        var aResult = await a.Sync.RunAsync();
        Assert.Equal(0, aResult.ConflictCount);
        var aLocal = AssociateDraftPayloadSerializer.Deserialize((await a.Drafts.GetAsync(OperatingDate))!.PayloadJson);
        Assert.Equal(999, aLocal.ProjectedAttendance);

        // Convergence is stable — a further pass on either side changes nothing further.
        var bAgain = await b.Sync.RunAsync();
        var aAgain = await a.Sync.RunAsync();
        Assert.Equal(0, bAgain.ImportedCount + bAgain.ExportedCount + bAgain.ConflictCount);
        Assert.Equal(0, aAgain.ImportedCount + aAgain.ExportedCount + aAgain.ConflictCount);
    }

    [Fact]
    public async Task ConflictResolution_C_UseOtherDevice_AdoptsRemoteContent_AndConverges()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 500 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var resolution = await b.Resolution.ResolveUseRemoteAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(OperatingDate));
        Assert.Equal(ConflictResolutionOutcome.Resolved, resolution.Outcome);

        var bLocal = AssociateDraftPayloadSerializer.Deserialize((await b.Drafts.GetAsync(OperatingDate))!.PayloadJson);
        Assert.Equal(500, bLocal.ProjectedAttendance); // Adopted A's content, B's own conflicting edit discarded by explicit choice.

        // A further sync pass must not immediately re-export B's old (discarded) version (§8.6).
        var bAgain = await b.Sync.RunAsync();
        Assert.Equal(0, bAgain.ExportedCount);
    }

    [Fact]
    public async Task ConflictResolution_D_RestartBeforeResolving_ConflictAndFullPayloadSurvive()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 500 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        // Simulate an app restart on B: brand-new service instances over the same on-disk database.
        var bRestarted = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddHours(2)));

        var conflicts = await bRestarted.Resolution.GetConflictsAsync();
        Assert.Single(conflicts);

        var detail = await bRestarted.Resolution.GetDraftConflictDetailAsync(OperatingDate);
        Assert.NotNull(detail);
        Assert.Equal(500, detail!.Remote!.ProjectedAttendance); // The conflicting remote payload survived the restart.

        var resolution = await bRestarted.Resolution.ResolveKeepLocalAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(OperatingDate));
        Assert.Equal(ConflictResolutionOutcome.Resolved, resolution.Outcome);
    }

    [Fact]
    public async Task ConflictResolution_E_SyncFolderUnavailableDuringResolution_ConflictPreserved_RetrySucceeds()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 500 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        // Obstruct B's export target so Keep Local's write fails.
        var obstructedPath = SyncPathService.GetDraftFilePath(_sharedRoot, OperatingDate);
        File.Delete(obstructedPath);
        Directory.CreateDirectory(obstructedPath);

        var failedAttempt = await b.Resolution.ResolveKeepLocalAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(OperatingDate));
        Assert.Equal(ConflictResolutionOutcome.ExportUnavailable, failedAttempt.Outcome);
        Assert.Single(await b.Resolution.GetConflictsAsync()); // Not lost, not falsely marked resolved.

        Directory.Delete(obstructedPath);
        var retry = await b.Resolution.ResolveKeepLocalAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(OperatingDate));
        Assert.Equal(ConflictResolutionOutcome.Resolved, retry.Outcome);
        Assert.Empty(await b.Resolution.GetConflictsAsync());
    }

    [Fact]
    public async Task ConflictResolution_F_RemoteChangesAgainWhilePending_OriginalConflictStaysStable_NewerActivityFlagged()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 500 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync(); // Conflict detected, original remote payload (500) persisted.

        // A advances again before B resolves anything.
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 700 });
        await a.Sync.RunAsync();
        var bResult = await b.Sync.RunAsync();

        Assert.Equal(1, bResult.ConflictCount); // Still (only) one conflict — not a second, unrelated one.
        var detail = await b.Resolution.GetDraftConflictDetailAsync(OperatingDate);
        Assert.NotNull(detail);
        Assert.Equal(500, detail!.Remote!.ProjectedAttendance); // Original conflict payload never silently replaced.
        Assert.True(detail.HasNewerRemoteActivity); // ...but newer remote activity is surfaced.

        // Resolving still works normally afterward.
        var resolution = await b.Resolution.ResolveKeepLocalAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(OperatingDate));
        Assert.Equal(ConflictResolutionOutcome.Resolved, resolution.Outcome);
    }

    [Fact]
    public async Task ConflictResolution_Note_KeepLocal_ExportsNewRevision_AndOtherDeviceConverges()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Original", "Body", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        await a.Notes.UpdateAsync(created.Id, new OperationalNoteDraft("Edited by A", "Body", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await b.Notes.UpdateAsync(created.Id, new OperationalNoteDraft("Edited by B", "Body", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var entityId = SyncEntityIdentity.ForNote(created.Id);
        Assert.Single(await b.Resolution.GetConflictsAsync());
        var noteDetail = await b.Resolution.GetNoteConflictDetailAsync(created.Id);
        Assert.Equal("Edited by A", noteDetail!.Remote!.Title);

        var resolution = await b.Resolution.ResolveKeepLocalAsync(SyncEntityType.OperationalNote, entityId);
        Assert.Equal(ConflictResolutionOutcome.Resolved, resolution.Outcome);

        var aResult = await a.Sync.RunAsync();
        Assert.Equal(0, aResult.ConflictCount);
        Assert.Equal("Edited by B", (await a.NoteRepository.GetByIdAsync(created.Id))!.Title);
    }

    // ====================================================================
    // Phase 16D §23: deletion/tombstone scenarios A-J.
    // ====================================================================

    [Fact]
    public async Task Deletion_A_ADeletesNote_BImportsDeletion()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Gate 3 scanner down", null, OperationalNoteCategory.Equipment, OperationalNotePriority.Important, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        Assert.NotNull(await b.NoteRepository.GetByIdAsync(created.Id));

        await DeleteNoteAsync(a, created.Id);
        var aExport = await a.Sync.RunAsync();
        Assert.Equal(1, aExport.ExportedCount);

        var bImport = await b.Sync.RunAsync();
        Assert.Equal(1, bImport.ImportedCount);
        Assert.Null(await b.NoteRepository.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task Deletion_B_RepeatedTombstoneImport_IsIdempotent()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await DeleteNoteAsync(a, created.Id);
        await a.Sync.RunAsync();

        var firstImport = await b.Sync.RunAsync();
        var secondImport = await b.Sync.RunAsync();
        var thirdImport = await b.Sync.RunAsync();

        Assert.Equal(1, firstImport.ImportedCount);
        Assert.Equal(0, secondImport.ImportedCount);
        Assert.Equal(0, thirdImport.ImportedCount);
        Assert.Null(await b.NoteRepository.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task Deletion_C_StaleOfflineDeviceReconnectsAfterDeletion_SafelyAppliesDeletion_NeverResurrects()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var c = CreateDevice("c", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await c.Sync.RunAsync(); // C sees the note once, then goes offline for a long time.

        await DeleteNoteAsync(a, created.Id);
        await a.Sync.RunAsync();

        // C reconnects much later, having never edited its own copy in the meantime.
        var cResult = await c.Sync.RunAsync();
        Assert.Equal(1, cResult.ImportedCount);
        Assert.Equal(0, cResult.ConflictCount);
        Assert.Null(await c.NoteRepository.GetByIdAsync(created.Id));

        // C must never re-export the pre-delete content it used to have.
        var cAgain = await c.Sync.RunAsync();
        Assert.Equal(0, cAgain.ExportedCount);
    }

    [Fact]
    public async Task Deletion_D_StaleDeviceThatDidNotEditDoesNotResurrectAfterOwnSyncPass()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await DeleteNoteAsync(a, created.Id);
        await a.Sync.RunAsync();
        await b.Sync.RunAsync(); // B applies the deletion safely (never edited its own copy).

        // The canonical remote file must still be A's tombstone — B never overwrote it with its old copy.
        var envelope = SyncEnvelopeSerializer.Deserialize(await File.ReadAllTextAsync(SyncPathService.GetOperationalNoteFilePath(_sharedRoot, created.Id)));
        Assert.True(envelope.IsDeleted);
    }

    [Fact]
    public async Task Deletion_E_LocalEditBeforeReceivingTombstone_IsConflict_NeverSilentDeletion()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Original", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        await b.Notes.UpdateAsync(created.Id, new OperationalNoteDraft("Edited by B before delete arrives", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await DeleteNoteAsync(a, created.Id);
        await a.Sync.RunAsync();

        var bResult = await b.Sync.RunAsync();
        Assert.Equal(1, bResult.ConflictCount);
        Assert.NotNull(await b.NoteRepository.GetByIdAsync(created.Id)); // B's edit preserved, never silently deleted.

        var summary = Assert.Single(await b.Resolution.GetConflictsAsync());
        Assert.True(summary.IsRemoteDeletion);
    }

    [Fact]
    public async Task Deletion_F_KeepLocalAfterDeleteConflict_ResurrectsIntentionallyWithNewAuthoritativeRevision()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Original", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await b.Notes.UpdateAsync(created.Id, new OperationalNoteDraft("Edited by B before delete arrives", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await DeleteNoteAsync(a, created.Id);
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var entityId = SyncEntityIdentity.ForNote(created.Id);
        var resolution = await b.Resolution.ResolveKeepLocalAsync(SyncEntityType.OperationalNote, entityId);
        Assert.Equal(ConflictResolutionOutcome.Resolved, resolution.Outcome);
        Assert.NotNull(await b.NoteRepository.GetByIdAsync(created.Id));

        // A considers this entity deleted (it authored the tombstone); B's resurrection is itself a new,
        // independent piece of remote activity from A's point of view — resurrection is never silent on
        // EITHER side, so A must also make its own explicit choice, not auto-adopt B's undelete (§17).
        var aResult = await a.Sync.RunAsync();
        Assert.Equal(1, aResult.ConflictCount);
        Assert.Null(await a.NoteRepository.GetByIdAsync(created.Id)); // A's deletion is not silently overridden either.

        var aConflict = Assert.Single(await a.Resolution.GetConflictsAsync());
        Assert.False(aConflict.IsRemoteDeletion); // B's incoming envelope is live content, not a deletion.

        // A explicitly accepts B's resurrection ("Use Other Device") — now both sides converge.
        var aResolution = await a.Resolution.ResolveUseRemoteAsync(SyncEntityType.OperationalNote, entityId);
        Assert.Equal(ConflictResolutionOutcome.Resolved, aResolution.Outcome);
        var aNote = await a.NoteRepository.GetByIdAsync(created.Id);
        Assert.NotNull(aNote);
        Assert.Equal("Edited by B before delete arrives", aNote!.Title);
        Assert.Equal(created.Id, aNote.Id); // Identity preserved, never a duplicate Guid.
    }

    [Fact]
    public async Task Deletion_G_AcceptDeletionRemovesLocalRecord()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Original", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await b.Notes.UpdateAsync(created.Id, new OperationalNoteDraft("Edited by B before delete arrives", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await DeleteNoteAsync(a, created.Id);
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var entityId = SyncEntityIdentity.ForNote(created.Id);
        var resolution = await b.Resolution.ResolveAcceptDeletionAsync(SyncEntityType.OperationalNote, entityId);
        Assert.Equal(ConflictResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Null(await b.NoteRepository.GetByIdAsync(created.Id));
        Assert.Empty(await b.Resolution.GetConflictsAsync());
    }

    [Fact]
    public async Task Deletion_H_TombstoneAndDeleteConflictSurviveRestart()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Original", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await b.Notes.UpdateAsync(created.Id, new OperationalNoteDraft("Edited by B before delete arrives", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await DeleteNoteAsync(a, created.Id);
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var bRestarted = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddHours(2)));
        var conflicts = await bRestarted.Resolution.GetConflictsAsync();
        Assert.Single(conflicts);
        Assert.True(conflicts[0].IsRemoteDeletion);

        var resolution = await bRestarted.Resolution.ResolveAcceptDeletionAsync(SyncEntityType.OperationalNote, SyncEntityIdentity.ForNote(created.Id));
        Assert.Equal(ConflictResolutionOutcome.Resolved, resolution.Outcome);
    }

    [Fact]
    public async Task Deletion_I_MissingCanonicalFile_IsNeverTreatedAsDeletion()
    {
        // Already covered end-to-end by RunAsync_PreviouslySeenRemoteFileLaterDisappears_NeverDeletesOrTombstonesTheLocalNote
        // above (§18/§29) — a missing file is indistinguishable from "not yet synced," never a tombstone.
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        File.Delete(SyncPathService.GetOperationalNoteFilePath(_sharedRoot, created.Id));
        var result = await b.Sync.RunAsync();

        Assert.Equal(0, result.ImportedCount);
        Assert.NotNull(await b.NoteRepository.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task Deletion_J_MalformedTombstoneFile_DoesNothingDestructive()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Note", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        await File.WriteAllTextAsync(SyncPathService.GetOperationalNoteFilePath(_sharedRoot, created.Id), """{ "IsDeleted": true, "EntityType":""");

        var result = await b.Sync.RunAsync();

        Assert.Equal(1, result.MalformedCount);
        Assert.NotNull(await b.NoteRepository.GetByIdAsync(created.Id)); // Never destroyed by a malformed tombstone.
    }

    // ====================================================================
    // Phase 16D §19/§21: resolution service guard rails and idempotence.
    // ====================================================================

    [Fact]
    public async Task Resolution_NoConflictExists_ResolveKeepLocal_ReturnsNoConflictFound_NotAnError()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));

        var result = await a.Resolution.ResolveKeepLocalAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(OperatingDate));

        Assert.Equal(ConflictResolutionOutcome.NoConflictFound, result.Outcome);
    }

    [Fact]
    public async Task Resolution_ResolvingTheSameConflictTwice_SecondCallIsIdempotent_NoConflictFound()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 500 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var entityId = SyncEntityIdentity.ForDraft(OperatingDate);
        var first = await b.Resolution.ResolveKeepLocalAsync(SyncEntityType.AssociateDraft, entityId);
        var second = await b.Resolution.ResolveKeepLocalAsync(SyncEntityType.AssociateDraft, entityId);

        Assert.Equal(ConflictResolutionOutcome.Resolved, first.Outcome);
        Assert.Equal(ConflictResolutionOutcome.NoConflictFound, second.Outcome); // Accidental UI retry never duplicates a revision.

        var state = await b.States.GetAsync(SyncEntityType.AssociateDraft, entityId);
        Assert.Equal(3, state!.LocalRevision); // 1 (baseline) -> 2 (B's own edit) -> 3 (Keep Local). Not bumped twice.
    }

    [Fact]
    public async Task Resolution_UseOtherDeviceOnARemoteDeletionConflict_IsRefused_UseAcceptDeletionInstead()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        var created = await a.Notes.CreateAsync(new OperationalNoteDraft("Original", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await b.Notes.UpdateAsync(created.Id, new OperationalNoteDraft("Edited by B", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await DeleteNoteAsync(a, created.Id);
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var entityId = SyncEntityIdentity.ForNote(created.Id);
        var result = await b.Resolution.ResolveUseRemoteAsync(SyncEntityType.OperationalNote, entityId);

        Assert.Equal(ConflictResolutionOutcome.WrongActionForConflict, result.Outcome);
        Assert.NotNull(await b.NoteRepository.GetByIdAsync(created.Id)); // Nothing was changed by the refused call.
        Assert.Single(await b.Resolution.GetConflictsAsync()); // Conflict remains, ready for the correct action.
    }

    [Fact]
    public async Task Resolution_AcceptDeletionOnANonDeletionConflict_IsRefused_UseOtherDeviceInstead()
    {
        var a = CreateDevice("a", DeviceAId, new FakeClock(BaseNow));
        var b = CreateDevice("b", DeviceBId, new FakeClock(BaseNow.AddMinutes(1)));

        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 100 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();
        await SaveDraftAsync(a, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 500 });
        await SaveDraftAsync(b, OperatingDate, AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 999 });
        await a.Sync.RunAsync();
        await b.Sync.RunAsync();

        var entityId = SyncEntityIdentity.ForDraft(OperatingDate);
        var result = await b.Resolution.ResolveAcceptDeletionAsync(SyncEntityType.AssociateDraft, entityId);

        Assert.Equal(ConflictResolutionOutcome.WrongActionForConflict, result.Outcome);
        var bLocal = AssociateDraftPayloadSerializer.Deserialize((await b.Drafts.GetAsync(OperatingDate))!.PayloadJson);
        Assert.Equal(999, bLocal.ProjectedAttendance); // Nothing was changed by the refused call.
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

    private sealed class FailingSyncFolderValidator : ISyncFolderValidator
    {
        public Task<SyncFolderValidationResult> ValidateAsync(string selectedRootPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(SyncFolderValidationResult.Unavailable("Simulated unavailable folder."));
    }
}
