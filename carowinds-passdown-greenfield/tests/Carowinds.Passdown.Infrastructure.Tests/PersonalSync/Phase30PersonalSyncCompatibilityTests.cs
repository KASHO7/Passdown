using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Application.MorningMessage.Sync;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Carowinds.Passdown.Infrastructure.Preferences;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>
/// Phase 30 through the full <see cref="PersonalSyncService"/> pass: Morning Message sync rides the existing
/// folder only when turned on, never disturbs drafts, notes or official price history, and an RC.9-equivalent
/// peer (the same pass with no Morning Message component — RC.9's code for every existing folder is
/// unchanged) neither reads nor writes <c>morning-message/</c>.
/// </summary>
public sealed class Phase30PersonalSyncCompatibilityTests : IDisposable
{
    private const string GoldPass = Phase26OfficialPriceHistorySyncTests.GoldPass;
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly OperatingDate = new(2026, 10, 22);

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"carowinds-phase30-compat-{Guid.NewGuid():N}");

    public Phase30PersonalSyncCompatibilityTests() => Directory.CreateDirectory(Root);

    private string Root => Path.Combine(_workspace, "shared");

    private string MessageFolder => SyncPathService.GetMorningMessageDirectoryPath(Root);

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
        PersonalSyncService Sync,
        IOperationalNotesService Notes,
        IOperationalNoteRepository NoteRepository,
        SqliteSeasonPriceHistoryRepository Local,
        SqliteSyncedPriceObservationRepository Synced,
        FileSyncConfigurationStore Configuration,
        FileMorningMessageContentStore Messages);

    /// <param name="phase30">False builds an RC.9-equivalent computer: the same pass with no Morning Message channel.</param>
    private async Task<Device> CreateDeviceAsync(string name, bool phase30, bool morningMessagesOn, IMorningMessageSync? channelOverride = null)
    {
        var deviceId = Guid.NewGuid();
        var directory = Path.Combine(_workspace, name);
        Directory.CreateDirectory(directory);
        var clock = new FakeClock(Now);
        var factory = new SqliteConnectionFactory(Path.Combine(directory, "carowinds-passdown.db"));
        new MigrationRunner(factory, Phase26OfficialPriceHistorySyncTests.AllMigrations(), NullLogger<MigrationRunner>.Instance).Run();

        var drafts = new SqliteAssociateDraftRepository(factory);
        var noteRepository = new SqliteOperationalNoteRepository(factory);
        var states = new SqliteSyncEntityStateRepository(factory);
        var tracker = new SyncChangeTracker(states, clock);
        var local = new SqliteSeasonPriceHistoryRepository(factory);
        var synced = new SqliteSyncedPriceObservationRepository(factory);
        var deviceIdProvider = new FixedDeviceId(deviceId);
        var configuration = new FileSyncConfigurationStore(Path.Combine(directory, "sync-config.json"));
        await configuration.SaveAsync(new SyncConfiguration(true, Root, MorningMessageSyncEnabled: morningMessagesOn));
        var settings = new PersonalSyncSettingsService(configuration, new SyncFolderValidator(), new NoSuggestions(), deviceIdProvider, clock);

        var messages = new FileMorningMessageContentStore(Path.Combine(directory, "morning-message-content.json"));
        var channel = channelOverride ?? (phase30
            ? new MorningMessageSync(
                configuration, messages, new FileMorningMessagePreferenceStore(Path.Combine(directory, "morning-message.json")),
                new MorningMessageSyncStateStore(Path.Combine(directory, "morning-message-sync-state.json")), new MorningMessageEditorSession(), deviceIdProvider)
            : null);

        var sync = new PersonalSyncService(
            settings, configuration, deviceIdProvider, drafts, noteRepository, new SqliteFinalizedPassdownRepository(factory), states,
            new SqliteSyncConflictRepository(factory), clock, new SqliteMonthlyErtRepository(factory), new SqliteErtDateOverrideRepository(factory),
            officialPriceHistorySync: new OfficialPriceHistorySync(local, synced),
            morningMessageSync: channel);

        return new Device(deviceId, sync, new OperationalNotesService(noteRepository, clock, tracker), noteRepository, local, synced, configuration, messages);
    }

    private static Task RecordPriceAsync(Device device) =>
        device.Local.InsertAsync(new SeasonPriceHistoryInterval(
            GoldPass, 1, 99m, PriceKind.StandardCatalog, "$99.00", null, "Gold Season Pass — 2027",
            "Carowinds Memberships & Season Passes", new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 21, 14, 0, 0, TimeSpan.Zero), SeasonPriceHistoryOrigin.Observed));

    private Task<OperationalNote> CreateNoteAsync(Device device, string title) =>
        device.Notes.CreateAsync(new OperationalNoteDraft(title, "Body.", OperationalNoteCategory.General, OperationalNotePriority.Normal, OperatingDate, null));

    private Dictionary<string, byte[]> Snapshot(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes) : [];

    [Fact]
    public async Task SyncNow_WithTheOptionOn_CarriesMorningMessages_AlongsideNotesAndPriceHistory()
    {
        var a = await CreateDeviceAsync("a", phase30: true, morningMessagesOn: true);
        var b = await CreateDeviceAsync("b", phase30: true, morningMessagesOn: true);
        await RecordPriceAsync(a);
        var note = await CreateNoteAsync(a, "Gate 3 scanner down");
        await a.Messages.SaveAsync(OperatingDate, MorningMessageContent.Empty with { Reminders = "Hydrate", OdNotes = "Ride 4" });

        var exported = await a.Sync.RunAsync();
        var imported = await b.Sync.RunAsync();

        Assert.NotNull(exported.MorningMessages);
        Assert.True(exported.MorningMessages!.Exported);
        Assert.Equal(PersonalSyncOutcome.Completed, imported.Outcome);
        Assert.Equal(1, imported.MorningMessages!.DatesUpdated);
        Assert.Equal(1, imported.ImportedCount);             // the note — morning messages are never counted as drafts/notes
        Assert.Equal(1, imported.PriceObservationsMergedCount);
        Assert.Equal(0, imported.MalformedCount + imported.IoFailureCount);
        Assert.Equal("Gate 3 scanner down", (await b.NoteRepository.GetByIdAsync(note.Id))!.Title);
        Assert.Equal(MorningMessageContent.Empty with { Reminders = "Hydrate", OdNotes = "Ride 4" }, await b.Messages.LoadAsync(OperatingDate));
    }

    [Fact]
    public async Task SyncNow_WithTheOptionOff_LeavesMorningMessagesAlone_AndEverythingElseStillSyncs()
    {
        var a = await CreateDeviceAsync("a", phase30: true, morningMessagesOn: true);
        var b = await CreateDeviceAsync("b", phase30: true, morningMessagesOn: false);
        await CreateNoteAsync(a, "Shared note");
        await a.Messages.SaveAsync(OperatingDate, MorningMessageContent.Empty with { Reminders = "From A" });
        await b.Messages.SaveAsync(OperatingDate, MorningMessageContent.Empty with { Reminders = "B's own" });
        await a.Sync.RunAsync();
        var before = Snapshot(MessageFolder);

        var result = await b.Sync.RunAsync();

        Assert.Null(result.MorningMessages);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal("B's own", (await b.Messages.LoadAsync(OperatingDate)).Reminders);
        Assert.Equal(before.Keys, Snapshot(MessageFolder).Keys);
        Assert.Single(before);
    }

    [Fact]
    public async Task Rc9Peer_IgnoresTheMorningMessageFolder_AndNeverChangesIt()
    {
        var phase30 = await CreateDeviceAsync("new", phase30: true, morningMessagesOn: true);
        var rc9 = await CreateDeviceAsync("rc9", phase30: false, morningMessagesOn: true); // even with the flag in its config file
        await phase30.Messages.SaveAsync(OperatingDate, MorningMessageContent.Empty with { BreakPlan = "Plan" });
        await CreateNoteAsync(phase30, "Shared note");
        await phase30.Sync.RunAsync();
        var before = Snapshot(MessageFolder);
        var manifest = await File.ReadAllBytesAsync(SyncPathService.GetManifestFilePath(Root));

        var result = await rc9.Sync.RunAsync();

        Assert.Equal(PersonalSyncOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.ImportedCount);      // existing entities still sync with an older peer
        Assert.Equal(0, result.MalformedCount);     // the unknown folder is not even looked at
        Assert.Null(result.MorningMessages);
        Assert.Equal(before.Keys, Snapshot(MessageFolder).Keys);
        Assert.All(before, entry => Assert.Equal(entry.Value, Snapshot(MessageFolder)[entry.Key]));
        Assert.Equal(manifest, await File.ReadAllBytesAsync(SyncPathService.GetManifestFilePath(Root)));
        Assert.Equal(1, SyncEnvelopeSerializer.CurrentSyncSchemaVersion); // the envelope contract is unchanged

        var again = await phase30.Sync.RunAsync();
        Assert.Equal(0, again.MalformedCount + again.IoFailureCount);
        Assert.Equal(0, again.MorningMessages!.ConflictCount);
    }

    [Fact]
    public async Task Phase30Peer_InAnRc9OnlyFolder_AddsOnlyItsOwnMorningMessageFile()
    {
        var rc9 = await CreateDeviceAsync("rc9", phase30: false, morningMessagesOn: false);
        await CreateNoteAsync(rc9, "Old peer note");
        await rc9.Sync.RunAsync();
        var phase30 = await CreateDeviceAsync("new", phase30: true, morningMessagesOn: true);
        await phase30.Messages.SaveAsync(OperatingDate, MorningMessageContent.Empty with { Reminders = "R" });

        var result = await phase30.Sync.RunAsync();

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.MalformedCount + result.IoFailureCount);
        var file = Assert.Single(Directory.GetFiles(MessageFolder));
        Assert.Equal($"{phase30.DeviceId:D}.json", Path.GetFileName(file));
    }

    [Fact]
    public async Task OfflineSyncFolder_ReportsUnavailable_AndLeavesLocalMessagesIntact()
    {
        var a = await CreateDeviceAsync("a", phase30: true, morningMessagesOn: true);
        await a.Messages.SaveAsync(OperatingDate, MorningMessageContent.Empty with { Reminders = "Local" });
        var before = await File.ReadAllBytesAsync(a.Messages.FilePath);
        var unavailableRoot = Path.Combine(_workspace, "unplugged-drive");
        File.WriteAllText(unavailableRoot, string.Empty);
        await a.Configuration.SaveAsync(new SyncConfiguration(true, unavailableRoot, MorningMessageSyncEnabled: true));

        var result = await a.Sync.RunAsync();

        Assert.Equal(PersonalSyncOutcome.FolderUnavailable, result.Outcome);
        Assert.Null(result.MorningMessages);
        Assert.Equal(before, await File.ReadAllBytesAsync(a.Messages.FilePath));
    }

    [Fact]
    public async Task AFailingMorningMessageChannel_IsCountedForRetry_AndNeverBlocksNotesOrPriceHistory()
    {
        var a = await CreateDeviceAsync("a", phase30: true, morningMessagesOn: true);
        var b = await CreateDeviceAsync("b", phase30: true, morningMessagesOn: true, channelOverride: new ThrowingChannel());
        await RecordPriceAsync(a);
        await CreateNoteAsync(a, "Still syncs");
        await a.Sync.RunAsync();

        var result = await b.Sync.RunAsync();

        Assert.Equal(PersonalSyncOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.PriceObservationsMergedCount);
        Assert.Equal(1, result.IoFailureCount);
        Assert.NotNull(result.MorningMessages);
    }

    private sealed class ThrowingChannel : IMorningMessageSync
    {
        public Task<MorningMessageSyncResult> RunAsync(string selectedRootPath, Guid deviceId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated locked file.");
    }

    private sealed class FixedDeviceId(Guid fixedId) : IDeviceIdProvider
    {
        public Guid GetOrCreateDeviceId() => fixedId;
    }

    private sealed class NoSuggestions : IOneDriveLocationSuggestionProvider
    {
        public IReadOnlyList<string> GetSuggestedRoots() => [];
    }
}
