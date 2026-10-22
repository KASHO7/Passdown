using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>
/// Phase 26 through the full <see cref="PersonalSyncService"/> pass: the price-history channel rides
/// the existing folder, never disturbs drafts/notes, and an RC.8-equivalent peer (the same pass with no
/// price-history component — RC.8's code for every existing folder is unchanged) neither reads nor
/// writes it.
/// </summary>
public sealed class Phase26PersonalSyncCompatibilityTests : IDisposable
{
    private const string GoldPass = Phase26OfficialPriceHistorySyncTests.GoldPass;
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly OperatingDate = new(2026, 10, 22);

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"carowinds-phase26-compat-{Guid.NewGuid():N}");

    private string Root => Path.Combine(_workspace, "shared");

    public Phase26PersonalSyncCompatibilityTests() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private sealed record Device(
        Guid DeviceId,
        SqliteConnectionFactory Factory,
        PersonalSyncService Sync,
        IOperationalNotesService Notes,
        IOperationalNoteRepository NoteRepository,
        SqliteSeasonPriceHistoryRepository Local,
        SqliteSyncedPriceObservationRepository Synced,
        FakeSyncConfigurationStore Configuration);

    private Device CreateDevice(string name, bool phase26)
    {
        var deviceId = Guid.NewGuid();
        var clock = new FakeClock(Now);
        var factory = new SqliteConnectionFactory(Path.Combine(_workspace, name, "carowinds-passdown.db"));
        new MigrationRunner(factory, Phase26OfficialPriceHistorySyncTests.AllMigrations(), NullLogger<MigrationRunner>.Instance).Run();

        var drafts = new SqliteAssociateDraftRepository(factory);
        var noteRepository = new SqliteOperationalNoteRepository(factory);
        var states = new SqliteSyncEntityStateRepository(factory);
        var tracker = new SyncChangeTracker(states, clock);
        var local = new SqliteSeasonPriceHistoryRepository(factory);
        var synced = new SqliteSyncedPriceObservationRepository(factory);
        var deviceIdProvider = new FakeDeviceIdProvider(deviceId);
        var configuration = new FakeSyncConfigurationStore { Current = new SyncConfiguration(true, Root) };
        var settings = new PersonalSyncSettingsService(configuration, new SyncFolderValidator(), new FakeOneDriveLocationSuggestionProvider(), deviceIdProvider, clock);

        var sync = new PersonalSyncService(
            settings, configuration, deviceIdProvider, drafts, noteRepository, new SqliteFinalizedPassdownRepository(factory), states,
            new SqliteSyncConflictRepository(factory), clock, new SqliteMonthlyErtRepository(factory), new SqliteErtDateOverrideRepository(factory),
            officialPriceHistorySync: phase26 ? new OfficialPriceHistorySync(local, synced) : null);

        return new Device(deviceId, factory, sync, new OperationalNotesService(noteRepository, clock, tracker), noteRepository, local, synced, configuration);
    }

    private static Task RecordAsync(Device device, int sequence, decimal amount, DateTimeOffset first) =>
        device.Local.InsertAsync(new SeasonPriceHistoryInterval(
            GoldPass, sequence, amount, PriceKind.StandardCatalog, $"${amount:0.00}", null, "Gold Season Pass — 2027",
            "Carowinds Memberships & Season Passes", first, first.AddDays(20), SeasonPriceHistoryOrigin.Observed));

    private static Dictionary<string, byte[]> Snapshot(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory).ToDictionary(Path.GetFileName, File.ReadAllBytes)!
            : [];

    [Fact]
    public async Task SyncNow_CarriesPriceHistory_AndReportsMergedObservations()
    {
        var a = CreateDevice("a", phase26: true);
        var b = CreateDevice("b", phase26: true);
        await RecordAsync(a, 1, 99m, new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero));
        await RecordAsync(a, 2, 104m, new DateTimeOffset(2026, 7, 1, 14, 0, 0, TimeSpan.Zero));

        var exported = await a.Sync.RunAsync();
        var imported = await b.Sync.RunAsync();

        Assert.True(exported.IncludesOfficialPriceHistory);
        Assert.Equal(PersonalSyncOutcome.Completed, imported.Outcome);
        Assert.Equal(2, imported.PriceObservationsMergedCount);
        Assert.Equal(0, imported.ImportedCount); // price observations are never counted as draft/note imports
        Assert.Equal(0, imported.MalformedCount);
        Assert.Equal(2, await b.Synced.CountAsync());
        Assert.True(File.Exists(SyncPathService.GetOfficialPriceHistoryFilePath(Root, b.DeviceId)));
    }

    [Fact]
    public async Task NotesStillSynchronize_AlongsidePriceHistory()
    {
        var a = CreateDevice("a", phase26: true);
        var b = CreateDevice("b", phase26: true);
        await RecordAsync(a, 1, 99m, new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero));
        var note = await a.Notes.CreateAsync(new OperationalNoteDraft(
            "Gate 3 scanner down", "Use gate 2.", OperationalNoteCategory.Equipment, OperationalNotePriority.Important, OperatingDate, null));

        await a.Sync.RunAsync();
        var result = await b.Sync.RunAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.PriceObservationsMergedCount);
        Assert.Equal("Gate 3 scanner down", (await b.NoteRepository.GetByIdAsync(note.Id))!.Title);
    }

    [Fact]
    public async Task Rc8Peer_IgnoresThePriceHistoryFolder_AndNeverChangesIt()
    {
        var phase26 = CreateDevice("new", phase26: true);
        var rc8 = CreateDevice("rc8", phase26: false);
        await RecordAsync(phase26, 1, 99m, new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero));
        await phase26.Notes.CreateAsync(new OperationalNoteDraft(
            "Shared note", "Body.", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await phase26.Sync.RunAsync();
        var folder = SyncPathService.GetOfficialPriceHistoryDirectoryPath(Root);
        var before = Snapshot(folder);
        Assert.Single(before);

        var result = await rc8.Sync.RunAsync();

        Assert.Equal(PersonalSyncOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.ImportedCount);          // existing entities still sync with an older peer
        Assert.Equal(0, result.MalformedCount);         // the unknown folder is not even looked at
        Assert.False(result.IncludesOfficialPriceHistory);
        Assert.Equal(0, await rc8.Synced.CountAsync());
        Assert.Equal(before.Keys, Snapshot(folder).Keys);
        Assert.All(before, entry => Assert.Equal(entry.Value, Snapshot(folder)[entry.Key]));

        // And a later Phase 26 pass still sees exactly its own history intact.
        var again = await phase26.Sync.RunAsync();
        Assert.Equal(0, again.PriceObservationsSkippedCount);
        Assert.Single(await phase26.Local.GetHistoryAsync(GoldPass));
    }

    [Fact]
    public async Task Phase26Peer_InAnRc8OnlyFolder_ImportsNothing_AndAddsOnlyItsOwnSidecar()
    {
        var rc8 = CreateDevice("rc8", phase26: false);
        await rc8.Notes.CreateAsync(new OperationalNoteDraft("Old peer note", "Body.", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));
        await rc8.Sync.RunAsync();
        var phase26 = CreateDevice("new", phase26: true);
        await RecordAsync(phase26, 1, 99m, new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero));

        var result = await phase26.Sync.RunAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.PriceObservationsMergedCount);
        Assert.Equal(0, result.MalformedCount + result.IoFailureCount);
        var sidecar = Assert.Single(Directory.GetFiles(SyncPathService.GetOfficialPriceHistoryDirectoryPath(Root)));
        Assert.Equal($"{phase26.DeviceId:D}.json", Path.GetFileName(sidecar));
        Assert.Equal(SyncEnvelopeSerializer.CurrentSyncSchemaVersion, SyncManifestSerializer.CurrentSyncSchemaVersion);
        Assert.Equal(1, SyncEnvelopeSerializer.CurrentSyncSchemaVersion); // the envelope contract is unchanged
    }

    [Fact]
    public async Task OfflineSyncFolder_ReportsUnavailable_AndLeavesLocalHistoryIntact()
    {
        var a = CreateDevice("a", phase26: true);
        await RecordAsync(a, 1, 99m, new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero));
        var before = await a.Local.GetHistoryAsync(GoldPass);
        // A root that cannot hold the sync folder right now (here: a file squats on the path) is "unavailable".
        var unavailableRoot = Path.Combine(_workspace, "unplugged-drive");
        File.WriteAllText(unavailableRoot, string.Empty);
        a.Configuration.Current = new SyncConfiguration(true, unavailableRoot);

        var result = await a.Sync.RunAsync();

        Assert.Equal(PersonalSyncOutcome.FolderUnavailable, result.Outcome);
        Assert.False(result.IncludesOfficialPriceHistory);
        Assert.Equal(before, await a.Local.GetHistoryAsync(GoldPass));
        Assert.Equal(0, await a.Synced.CountAsync());
    }

    [Fact]
    public async Task UnreadablePriceHistoryFile_IsCountedForRetry_WithoutFailingThePass()
    {
        var a = CreateDevice("a", phase26: true);
        var folder = SyncPathService.GetOfficialPriceHistoryDirectoryPath(Root);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, $"{Guid.NewGuid():D}.json"), "{ truncated");

        var result = await a.Sync.RunAsync();

        Assert.Equal(PersonalSyncOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.MalformedCount);
        Assert.Equal(0, result.IoFailureCount);
    }

    [Fact]
    public void StaleTempFileCleanup_CoversThePriceHistoryFolder_ButNeverItsRealFiles()
    {
        var folder = SyncPathService.GetOfficialPriceHistoryDirectoryPath(Root);
        Directory.CreateDirectory(folder);
        var device = Guid.NewGuid();
        var real = Path.Combine(folder, $"{device:D}.json");
        var conflictCopy = Path.Combine(folder, $"{device:D}-LAPTOP.json");
        var staleTemp = Path.Combine(folder, $"{device:D}.json{AtomicFileWriter.TempMarker}{Guid.NewGuid():N}");
        var foreignTemp = Path.Combine(folder, $"notes.json{AtomicFileWriter.TempMarker}{Guid.NewGuid():N}");
        foreach (var path in new[] { real, conflictCopy, staleTemp, foreignTemp })
        {
            File.WriteAllText(path, "{}");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
        }

        var deleted = SyncTempFileCleaner.CleanStaleTempFiles(Root);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(staleTemp));
        Assert.True(File.Exists(real));
        Assert.True(File.Exists(conflictCopy));
        Assert.True(File.Exists(foreignTemp));
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
