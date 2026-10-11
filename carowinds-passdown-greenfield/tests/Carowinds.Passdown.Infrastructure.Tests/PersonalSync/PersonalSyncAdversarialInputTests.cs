using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.OperationalNotes;
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
/// Phase 18A (docs/phase18-comprehensive-audit.md SEC-001/SEC-002/SEC-003): the sync folder is untrusted
/// input. Each test plants one syntactically-valid-but-hostile (or corrupt) file written by "another
/// device" and proves the pass (a) never throws — before 18A every one of the throwing cases aborted the
/// entire pass, so every later import and every export stopped until the file was removed by hand —
/// (b) counts the file as malformed, (c) never writes it into the local database, and (d) still processes
/// an unrelated valid file and still exports local changes.
/// </summary>
public class PersonalSyncAdversarialInputTests : IDisposable
{
    private readonly string _sharedRoot = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-adv-shared-{Guid.NewGuid():N}");
    private readonly string _tempDataRoot = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-adv-devices-{Guid.NewGuid():N}");
    private static readonly Guid LocalDeviceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RemoteDeviceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
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
        IOperationalNoteRepository Notes,
        IMonthlyErtRepository MonthlyErts,
        IErtDateOverrideRepository ErtOverrides,
        IFinalizedPassdownRepository FinalizedPassdowns,
        ISyncChangeTracker Tracker,
        IPersonalSyncService Sync,
        FakeClock Clock);

    private TestDevice CreateLocalDevice()
    {
        var clock = new FakeClock(BaseNow);
        var connectionFactory = new SqliteConnectionFactory(Path.Combine(_tempDataRoot, "local.db"));
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
        var notes = new SqliteOperationalNoteRepository(connectionFactory);
        var monthlyErts = new SqliteMonthlyErtRepository(connectionFactory);
        var ertOverrides = new SqliteErtDateOverrideRepository(connectionFactory);
        var finalized = new SqliteFinalizedPassdownRepository(connectionFactory);
        var states = new SqliteSyncEntityStateRepository(connectionFactory);
        var conflicts = new SqliteSyncConflictRepository(connectionFactory);
        var tracker = new SyncChangeTracker(states, clock);

        var deviceIdProvider = new FakeDeviceIdProvider(LocalDeviceId);
        var configStore = new FakeSyncConfigurationStore { Current = new SyncConfiguration(true, _sharedRoot) };
        var settings = new PersonalSyncSettingsService(configStore, new SyncFolderValidator(), new FakeOneDriveLocationSuggestionProvider(), deviceIdProvider, clock);
        var sync = new PersonalSyncService(settings, configStore, deviceIdProvider, drafts, notes, finalized, states, conflicts, clock, monthlyErts, ertOverrides);

        return new TestDevice(drafts, notes, monthlyErts, ertOverrides, finalized, tracker, sync, clock);
    }

    /// <summary>Writes an envelope exactly as a remote device would — every envelope-level field valid — with <paramref name="payloadJson"/> as its raw payload.</summary>
    private static async Task PlantEnvelopeAsync(string path, SyncEntityType entityType, string entityId, string payloadJson, string? contentHash = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var payload = JsonDocument.Parse(payloadJson);
        var envelope = new SyncEnvelope
        {
            SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
            EntityType = entityType,
            EntityId = entityId,
            DeviceId = RemoteDeviceId,
            Revision = 1,
            ModifiedUtc = BaseNow,
            IsDeleted = false,
            PayloadSchemaVersion = 1,
            Payload = payload.RootElement.Clone(),
            ContentHash = contentHash,
        };
        await File.WriteAllTextAsync(path, SyncEnvelopeSerializer.Serialize(envelope));
    }

    private Task PlantValidRemoteDraftAsync(DateOnly date) =>
        PlantEnvelopeAsync(
            SyncPathService.GetDraftFilePath(_sharedRoot, date),
            SyncEntityType.AssociateDraft,
            SyncEntityIdentity.ForDraft(date),
            JsonSerializer.Serialize(AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 4242 }));

    /// <summary>The shared tail of every scenario: the pass completed, the poison file was counted, an unrelated valid remote draft was still imported, and a pending local change was still exported.</summary>
    private async Task AssertPassSurvivedAsync(TestDevice device, PersonalSyncResult result, DateOnly validRemoteDraftDate, DateOnly localDraftDate)
    {
        Assert.Equal(1, result.MalformedCount);
        var imported = await device.Drafts.GetAsync(validRemoteDraftDate);
        Assert.NotNull(imported);
        Assert.Equal(4242, AssociateDraftPayloadSerializer.Deserialize(imported.PayloadJson).ProjectedAttendance);
        Assert.True(File.Exists(SyncPathService.GetDraftFilePath(_sharedRoot, localDraftDate)), "Local export must still run after a poison file.");
    }

    private static async Task SaveLocalDraftAsync(TestDevice device, DateOnly date)
    {
        await device.Drafts.SaveAsync(new AssociateDraft(date, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(AssociateDraftPayload.CreateDefault() with { PreSolds = 7 }), device.Clock.UtcNow));
        await device.Tracker.MarkLocalChangeAsync(SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(date));
    }

    [Fact]
    public async Task NullNotePayload_IsMalformed_AndDoesNotAbortThePass()
    {
        var device = CreateLocalDevice();
        var noteId = Guid.NewGuid();
        await PlantEnvelopeAsync(SyncPathService.GetOperationalNoteFilePath(_sharedRoot, noteId), SyncEntityType.OperationalNote, SyncEntityIdentity.ForNote(noteId), "null");
        await PlantValidRemoteDraftAsync(OperatingDate);
        await SaveLocalDraftAsync(device, OperatingDate.AddDays(5));

        var result = await device.Sync.RunAsync();

        Assert.Null(await device.Notes.GetByIdAsync(noteId));
        await AssertPassSurvivedAsync(device, result, OperatingDate, OperatingDate.AddDays(5));
    }

    [Fact]
    public async Task NullDraftPayload_IsMalformed_NeverImportedAsADefaultDraft()
    {
        var device = CreateLocalDevice();
        var poisonDate = OperatingDate.AddDays(1);
        await PlantEnvelopeAsync(SyncPathService.GetDraftFilePath(_sharedRoot, poisonDate), SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(poisonDate), "null");
        await PlantValidRemoteDraftAsync(OperatingDate);
        await SaveLocalDraftAsync(device, OperatingDate.AddDays(5));

        var result = await device.Sync.RunAsync();

        Assert.Null(await device.Drafts.GetAsync(poisonDate));
        await AssertPassSurvivedAsync(device, result, OperatingDate, OperatingDate.AddDays(5));
    }

    [Theory]
    [InlineData("""{"ManualHoursOverride":null}""")]
    [InlineData("""{"CustomProducts":[null]}""")]
    [InlineData("""{"OperationalNotes":null}""")]
    [InlineData("""{"DailyFeatureType":99,"DailyFeatureText":"x"}""")]
    public async Task DraftPayloadWithNullMembersOrUndefinedEnum_IsMalformed_NeverPersisted(string overrideJson)
    {
        var device = CreateLocalDevice();
        var poisonDate = OperatingDate.AddDays(1);

        // Start from a complete default payload, then overwrite the one member under test.
        var payload = JsonSerializer.SerializeToNode(AssociateDraftPayload.CreateDefault())!.AsObject();
        foreach (var (name, value) in JsonNodeObject(overrideJson))
        {
            payload[name] = value?.DeepClone();
        }

        await PlantEnvelopeAsync(SyncPathService.GetDraftFilePath(_sharedRoot, poisonDate), SyncEntityType.AssociateDraft, SyncEntityIdentity.ForDraft(poisonDate), payload.ToJsonString());
        await PlantValidRemoteDraftAsync(OperatingDate);
        await SaveLocalDraftAsync(device, OperatingDate.AddDays(5));

        var result = await device.Sync.RunAsync();

        Assert.Null(await device.Drafts.GetAsync(poisonDate));
        await AssertPassSurvivedAsync(device, result, OperatingDate, OperatingDate.AddDays(5));
    }

    private static System.Text.Json.Nodes.JsonObject JsonNodeObject(string json) => System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();

    [Fact]
    public async Task NotePayloadWhoseIdDiffersFromItsFile_IsMalformed_AndNeverRewritesTheOtherNote()
    {
        var device = CreateLocalDevice();

        // A real local note "victim" — the poison file is named for a different id but carries the victim's id.
        var victim = new OperationalNote(Guid.NewGuid(), "Original title", "Original body", OperationalNoteCategory.General, OperationalNotePriority.Normal,
            OperatingDate, null, OperationalNoteStatus.Active, BaseNow, BaseNow, null);
        await device.Notes.AddAsync(victim);

        var fileNoteId = Guid.NewGuid();
        var hostile = OperationalNoteSyncPayload.FromDomain(victim with { Title = "Overwritten by another file", UpdatedAtUtc = BaseNow.AddHours(1) });
        await PlantEnvelopeAsync(SyncPathService.GetOperationalNoteFilePath(_sharedRoot, fileNoteId), SyncEntityType.OperationalNote, SyncEntityIdentity.ForNote(fileNoteId), OperationalNoteSyncPayloadSerializer.Serialize(hostile));
        await PlantValidRemoteDraftAsync(OperatingDate);
        await SaveLocalDraftAsync(device, OperatingDate.AddDays(5));

        var result = await device.Sync.RunAsync();

        Assert.Equal("Original title", (await device.Notes.GetByIdAsync(victim.Id))!.Title);
        Assert.Null(await device.Notes.GetByIdAsync(fileNoteId));
        await AssertPassSurvivedAsync(device, result, OperatingDate, OperatingDate.AddDays(5));
    }

    [Theory]
    [InlineData("Title", "null")]
    [InlineData("Title", "\"   \"")]
    [InlineData("Category", "99")]
    [InlineData("Status", "\"Archived\"")]
    public async Task NotePayloadWithMissingTitleOrUndefinedEnum_IsMalformed(string member, string valueJson)
    {
        var device = CreateLocalDevice();
        var noteId = Guid.NewGuid();
        var valid = OperationalNoteSyncPayload.FromDomain(new OperationalNote(noteId, "T", null, OperationalNoteCategory.General, OperationalNotePriority.Normal,
            OperatingDate, null, OperationalNoteStatus.Active, BaseNow, BaseNow, null));
        var node = System.Text.Json.Nodes.JsonNode.Parse(OperationalNoteSyncPayloadSerializer.Serialize(valid))!.AsObject();
        node[member] = System.Text.Json.Nodes.JsonNode.Parse(valueJson);

        await PlantEnvelopeAsync(SyncPathService.GetOperationalNoteFilePath(_sharedRoot, noteId), SyncEntityType.OperationalNote, SyncEntityIdentity.ForNote(noteId), node.ToJsonString());
        await PlantValidRemoteDraftAsync(OperatingDate);
        await SaveLocalDraftAsync(device, OperatingDate.AddDays(5));

        var result = await device.Sync.RunAsync();

        Assert.Null(await device.Notes.GetByIdAsync(noteId));
        await AssertPassSurvivedAsync(device, result, OperatingDate, OperatingDate.AddDays(5));
    }

    [Fact]
    public async Task MonthlyErtPayloadForADifferentMonthThanItsFile_IsMalformed_AndNeverWritesEitherMonth()
    {
        var device = CreateLocalDevice();
        var hostile = new MonthlyErtSyncPayload(2027, 1, ErtConfigurationState.Configured, new TimeOnly(9, 0), ["Fury 325"], null, BaseNow);
        await PlantEnvelopeAsync(SyncPathService.GetMonthlyErtFilePath(_sharedRoot, 2026, 9), SyncEntityType.MonthlyErt, SyncEntityIdentity.ForMonthlyErt(2026, 9), MonthlyErtSyncPayloadSerializer.Serialize(hostile));
        await PlantValidRemoteDraftAsync(OperatingDate);
        await SaveLocalDraftAsync(device, OperatingDate.AddDays(5));

        var result = await device.Sync.RunAsync();

        Assert.Null(await device.MonthlyErts.GetAsync(2027, 1));
        Assert.Null(await device.MonthlyErts.GetAsync(2026, 9));
        await AssertPassSurvivedAsync(device, result, OperatingDate, OperatingDate.AddDays(5));
    }

    [Fact]
    public async Task MonthlyErtPayloadWithNullRides_IsMalformed()
    {
        var device = CreateLocalDevice();
        var node = System.Text.Json.Nodes.JsonNode.Parse(MonthlyErtSyncPayloadSerializer.Serialize(
            new MonthlyErtSyncPayload(2026, 9, ErtConfigurationState.Configured, new TimeOnly(9, 0), ["Fury 325"], null, BaseNow)))!.AsObject();
        node["Rides"] = null;
        await PlantEnvelopeAsync(SyncPathService.GetMonthlyErtFilePath(_sharedRoot, 2026, 9), SyncEntityType.MonthlyErt, SyncEntityIdentity.ForMonthlyErt(2026, 9), node.ToJsonString());
        await PlantValidRemoteDraftAsync(OperatingDate);
        await SaveLocalDraftAsync(device, OperatingDate.AddDays(5));

        var result = await device.Sync.RunAsync();

        Assert.Null(await device.MonthlyErts.GetAsync(2026, 9));
        await AssertPassSurvivedAsync(device, result, OperatingDate, OperatingDate.AddDays(5));
    }

    [Fact]
    public async Task ErtDateOverridePayloadForADifferentDateThanItsFile_IsMalformed_AndNeverWritesEitherDate()
    {
        var device = CreateLocalDevice();
        var fileDate = OperatingDate.AddDays(2);
        var otherDate = OperatingDate.AddDays(30);
        var hostile = new ErtDateOverrideSyncPayload(otherDate, ErtConfigurationState.NoErt, null, [], "x", BaseNow);
        await PlantEnvelopeAsync(SyncPathService.GetErtDateOverrideFilePath(_sharedRoot, fileDate), SyncEntityType.ErtDateOverride, SyncEntityIdentity.ForErtDateOverride(fileDate), ErtDateOverrideSyncPayloadSerializer.Serialize(hostile));
        await PlantValidRemoteDraftAsync(OperatingDate);
        await SaveLocalDraftAsync(device, OperatingDate.AddDays(5));

        var result = await device.Sync.RunAsync();

        Assert.Null(await device.ErtOverrides.GetAsync(otherDate));
        Assert.Null(await device.ErtOverrides.GetAsync(fileDate));
        await AssertPassSurvivedAsync(device, result, OperatingDate, OperatingDate.AddDays(5));
    }

    [Fact]
    public async Task HistoryPayloadWithNullSnapshotJson_IsMalformed_AndDoesNotAbortThePass()
    {
        var device = CreateLocalDevice();
        var id = Guid.NewGuid();
        var historyDate = OperatingDate.AddDays(-3);
        var payloadJson = $$"""
            {"Id":"{{id}}","PassdownDate":"{{historyDate:yyyy-MM-dd}}","FinalizedAtUtc":"2026-09-06T10:00:00+00:00","SnapshotSchemaVersion":2,
             "SnapshotJson":null,"ContentHash":"abc","Origin":"Scheduled","PdfSha256":"00","PdfSizeBytes":4}
            """;
        await PlantEnvelopeAsync(SyncPathService.GetHistoryJsonFilePath(_sharedRoot, historyDate, id), SyncEntityType.FinalizedPassdown, SyncEntityIdentity.ForFinalizedPassdown(id), payloadJson, contentHash: "abc");
        await File.WriteAllBytesAsync(SyncPathService.GetHistoryPdfFilePath(_sharedRoot, historyDate, id), [0x25, 0x50, 0x44, 0x46]);
        await PlantValidRemoteDraftAsync(OperatingDate);
        await SaveLocalDraftAsync(device, OperatingDate.AddDays(5));

        var result = await device.Sync.RunAsync();

        Assert.Null(await device.FinalizedPassdowns.GetByIdAsync(id));
        await AssertPassSurvivedAsync(device, result, OperatingDate, OperatingDate.AddDays(5));
    }

    [Fact]
    public async Task OversizedSyncFile_IsReportedMalformed_WithoutBeingBuffered()
    {
        var path = Path.Combine(_sharedRoot, "oversized.json");
        Directory.CreateDirectory(_sharedRoot);

        // Valid JSON ({"V":1} padded with whitespace) — before 18A this read and parsed successfully.
        await using (var stream = File.Create(path))
        {
            await stream.WriteAsync("{\"V\":1"u8.ToArray());
            var padding = new byte[1024 * 1024];
            Array.Fill(padding, (byte)' ');
            for (var written = 0L; written <= SyncFileReader.MaxJsonFileBytes; written += padding.Length)
            {
                await stream.WriteAsync(padding);
            }
            await stream.WriteAsync("}"u8.ToArray());
        }

        var result = await SyncFileReader.ReadJsonAsync<VersionedProbe>(path, new JsonSerializerOptions(), _ => true);

        Assert.Equal(SyncFileReadStatus.Malformed, result.Status);
    }

    [Fact]
    public async Task NormalSizedSyncFile_StillReadsSuccessfully()
    {
        var path = Path.Combine(_sharedRoot, "normal.json");
        Directory.CreateDirectory(_sharedRoot);
        await File.WriteAllTextAsync(path, """{"V":1}""");

        var result = await SyncFileReader.ReadJsonAsync<VersionedProbe>(path, new JsonSerializerOptions(), _ => true);

        Assert.Equal(SyncFileReadStatus.Success, result.Status);
        Assert.Equal(1, result.Value!.V);
    }

    private sealed record VersionedProbe(int V);

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
