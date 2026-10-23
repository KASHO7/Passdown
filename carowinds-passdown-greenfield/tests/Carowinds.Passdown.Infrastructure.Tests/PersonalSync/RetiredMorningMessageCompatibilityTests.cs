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
/// Morning Message rollback (docs/morning-message-rollback.md): a profile or shared folder that a Phase 29/30
/// test build used must keep working. The retired <c>morning-message/</c> sync folder and the local
/// Morning Message JSON files are left exactly as they are — never read, written or deleted — while drafts,
/// notes and official price history keep synchronizing, and a <c>sync-config.json</c> carrying the retired
/// <c>MorningMessageSyncEnabled</c> field still loads.
/// </summary>
public sealed class RetiredMorningMessageCompatibilityTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly OperatingDate = new(2026, 10, 23);

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"carowinds-mm-rollback-{Guid.NewGuid():N}");

    public RetiredMorningMessageCompatibilityTests() => Directory.CreateDirectory(Root);

    private string Root => Path.Combine(_workspace, "shared");

    private string RetiredSyncFolder => Path.Combine(SyncPathService.GetSyncFolderPath(Root), "morning-message");

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
        string Directory,
        PersonalSyncService Sync,
        IOperationalNotesService Notes,
        IOperationalNoteRepository NoteRepository,
        SqliteSeasonPriceHistoryRepository Local,
        SqliteSyncedPriceObservationRepository Synced);

    private Device CreateDevice(string name, ISyncConfigurationStore? configuration = null)
    {
        var deviceId = Guid.NewGuid();
        var directory = Path.Combine(_workspace, name);
        System.IO.Directory.CreateDirectory(directory);
        var clock = new FakeClock(Now);
        var factory = new SqliteConnectionFactory(Path.Combine(directory, "carowinds-passdown.db"));
        new MigrationRunner(factory, Phase26OfficialPriceHistorySyncTests.AllMigrations(), NullLogger<MigrationRunner>.Instance).Run();

        var noteRepository = new SqliteOperationalNoteRepository(factory);
        var states = new SqliteSyncEntityStateRepository(factory);
        var local = new SqliteSeasonPriceHistoryRepository(factory);
        var synced = new SqliteSyncedPriceObservationRepository(factory);
        var deviceIdProvider = new FakeDeviceIdProvider(deviceId);
        configuration ??= new FakeSyncConfigurationStore { Current = new SyncConfiguration(true, Root) };
        var settings = new PersonalSyncSettingsService(configuration, new SyncFolderValidator(), new FakeOneDriveLocationSuggestionProvider(), deviceIdProvider, clock);

        var sync = new PersonalSyncService(
            settings, configuration, deviceIdProvider, new SqliteAssociateDraftRepository(factory), noteRepository, new SqliteFinalizedPassdownRepository(factory), states,
            new SqliteSyncConflictRepository(factory), clock, new SqliteMonthlyErtRepository(factory), new SqliteErtDateOverrideRepository(factory),
            officialPriceHistorySync: new OfficialPriceHistorySync(local, synced));

        return new Device(deviceId, directory, sync, new OperationalNotesService(noteRepository, clock, new SyncChangeTracker(states, clock)), noteRepository, local, synced);
    }

    private static Task RecordPriceAsync(Device device) =>
        device.Local.InsertAsync(new SeasonPriceHistoryInterval(
            Phase26OfficialPriceHistorySyncTests.GoldPass, 1, 99m, PriceKind.StandardCatalog, "$99.00", null, "Gold Season Pass — 2027",
            "Carowinds Memberships & Season Passes", new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 21, 14, 0, 0, TimeSpan.Zero), SeasonPriceHistoryOrigin.Observed));

    /// <summary>Byte contents and write times of every file under <paramref name="directory"/>, recursively.</summary>
    private static Dictionary<string, (byte[] Bytes, DateTime Written)> Snapshot(string directory) =>
        System.IO.Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)));

    private static void AssertUnchanged(Dictionary<string, (byte[] Bytes, DateTime Written)> before, string directory)
    {
        var after = Snapshot(directory);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (file, state) in before)
        {
            Assert.Equal(state.Bytes, after[file].Bytes);
            Assert.Equal(state.Written, after[file].Written);
        }
    }

    private void SeedRetiredSyncFolder()
    {
        System.IO.Directory.CreateDirectory(RetiredSyncFolder);
        var stale = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var (file, text) in new[]
        {
            ($"{Guid.NewGuid():D}.json", "{\"FormatVersion\":1,\"Fields\":{}}"),
            ($"{Guid.NewGuid():D} (conflicted copy).json", "{\"FormatVersion\":1}"),
            ($"{Guid.NewGuid():D}.json.tmp", "partial"),
        })
        {
            var path = Path.Combine(RetiredSyncFolder, file);
            File.WriteAllText(path, text);
            File.SetLastWriteTimeUtc(path, stale);
        }
    }

    [Fact]
    public async Task SharedFolderWithRetiredMorningMessageFiles_StillSyncsNotesAndPriceHistory_AndLeavesThoseFilesUntouched()
    {
        SeedRetiredSyncFolder();
        var before = Snapshot(RetiredSyncFolder);
        var a = CreateDevice("a");
        var b = CreateDevice("b");
        await RecordPriceAsync(a);
        var note = await a.Notes.CreateAsync(new OperationalNoteDraft("Gate 3 scanner down", "Body.", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));

        var exported = await a.Sync.RunAsync();
        var imported = await b.Sync.RunAsync();

        Assert.Equal(PersonalSyncOutcome.Completed, exported.Outcome);
        Assert.Equal(PersonalSyncOutcome.Completed, imported.Outcome);
        Assert.Equal(1, imported.ImportedCount);
        Assert.Equal(1, imported.PriceObservationsMergedCount);
        Assert.Equal(0, imported.MalformedCount + imported.IoFailureCount + imported.ConflictCount);
        Assert.Equal("Gate 3 scanner down", (await b.NoteRepository.GetByIdAsync(note.Id))!.Title);
        AssertUnchanged(before, RetiredSyncFolder);
    }

    [Fact]
    public async Task SyncConfigWrittenByAPhase30Build_StillLoads_AndSyncRuns()
    {
        var device = Path.Combine(_workspace, "phase30-profile");
        Directory.CreateDirectory(device);
        var configPath = Path.Combine(device, "sync-config.json");
        var escapedRoot = Root.Replace("\\", "\\\\", StringComparison.Ordinal);
        await File.WriteAllTextAsync(configPath, $$"""
            {
              "PersonalSyncEnabled": true,
              "SyncRootPath": "{{escapedRoot}}",
              "LastKnownSyncSpaceId": null,
              "MorningMessageSyncEnabled": true
            }
            """);
        var store = new FileSyncConfigurationStore(configPath);

        var loaded = await store.LoadAsync();
        var result = await CreateDevice("c", store).Sync.RunAsync();

        Assert.Equal(new SyncConfiguration(true, Root), loaded);
        Assert.Equal(PersonalSyncOutcome.Completed, result.Outcome);
        Assert.False(Directory.Exists(RetiredSyncFolder)); // nothing re-creates the retired folder
    }

    [Fact]
    public async Task LocalMorningMessageFiles_AreLeftOnDisk_Unchanged_ThroughSyncPasses()
    {
        var a = CreateDevice("a");
        string[] legacy = ["morning-message.json", "morning-message-content.json", "morning-message-sync-state.json"];
        foreach (var file in legacy)
        {
            await File.WriteAllTextAsync(Path.Combine(a.Directory, file), "{\"legacy\":true}");
        }

        var before = Snapshot(a.Directory);
        await a.Sync.RunAsync();
        await a.Sync.RunAsync();

        foreach (var file in legacy)
        {
            Assert.Equal(before[file].Bytes, await File.ReadAllBytesAsync(Path.Combine(a.Directory, file)));
            Assert.Equal(before[file].Written, File.GetLastWriteTimeUtc(Path.Combine(a.Directory, file)));
        }
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
