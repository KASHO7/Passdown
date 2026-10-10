using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

/// <summary>Phase 16D §2/§31: the durable conflict store — including the full conflicting remote payload — over the <c>sync_conflicts</c> table (DB schema 13).</summary>
public class SqliteSyncConflictRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);
    private static readonly Guid RemoteDeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public void Dispose() => _fixture.Dispose();

    private SqliteSyncConflictRepository CreateRepository() => new(_fixture.ConnectionFactory);

    private static SyncConflictRecord SampleDraftConflict() => new(
        SyncEntityType.AssociateDraft, "2026-09-09", RemoteDeviceId, 2, Now, RemoteIsDeleted: false,
        RemotePayloadSchemaVersion: 3, RemotePayloadJson: """{"projectedAttendance":500}""", RemoteContentHash: "remote-hash",
        DetectedAtUtc: Now, NewerRemoteDeviceId: null, NewerRemoteRevision: null, NewerRemoteDetectedAtUtc: null);

    [Fact]
    public async Task GetAsync_MissingRow_ReturnsNull()
    {
        var repository = CreateRepository();

        var result = await repository.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertAsync_ThenGetAsync_RoundTripsEveryField()
    {
        var repository = CreateRepository();
        var record = SampleDraftConflict();

        await repository.UpsertAsync(record);
        var loaded = await repository.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        Assert.Equal(record, loaded);
    }

    [Fact]
    public async Task UpsertAsync_ForATombstone_RoundTripsNullPayloadFields()
    {
        var repository = CreateRepository();
        var record = new SyncConflictRecord(
            SyncEntityType.OperationalNote, "3fa85f64-5717-4562-b3fc-2c963f66afa6", RemoteDeviceId, 3, Now,
            RemoteIsDeleted: true, RemotePayloadSchemaVersion: null, RemotePayloadJson: null, RemoteContentHash: null,
            DetectedAtUtc: Now, NewerRemoteDeviceId: null, NewerRemoteRevision: null, NewerRemoteDetectedAtUtc: null);

        await repository.UpsertAsync(record);
        var loaded = await repository.GetAsync(SyncEntityType.OperationalNote, "3fa85f64-5717-4562-b3fc-2c963f66afa6");

        Assert.NotNull(loaded);
        Assert.True(loaded!.RemoteIsDeleted);
        Assert.Null(loaded.RemotePayloadJson);
        Assert.Null(loaded.RemotePayloadSchemaVersion);
        Assert.Null(loaded.RemoteContentHash);
    }

    [Fact]
    public async Task UpsertAsync_CalledTwiceForTheSameKey_ReplacesRatherThanDuplicating()
    {
        var repository = CreateRepository();
        var first = SampleDraftConflict();
        var second = first with { NewerRemoteDeviceId = Guid.NewGuid(), NewerRemoteRevision = 3, NewerRemoteDetectedAtUtc = Now.AddMinutes(5) };

        await repository.UpsertAsync(first);
        await repository.UpsertAsync(second);
        var loaded = await repository.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        Assert.Equal(second, loaded);
        Assert.Single(await repository.ListAsync());
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRow()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(SampleDraftConflict());

        await repository.DeleteAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        Assert.Null(await repository.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09"));
    }

    [Fact]
    public async Task DeleteAsync_WhenNoRowExists_IsANoOp()
    {
        var repository = CreateRepository();

        await repository.DeleteAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        Assert.Null(await repository.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09"));
    }

    [Fact]
    public async Task ListAsync_ReturnsConflictsAcrossAllEntityTypes()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(SampleDraftConflict());
        await repository.UpsertAsync(SampleDraftConflict() with { EntityType = SyncEntityType.OperationalNote, EntityId = "3fa85f64-5717-4562-b3fc-2c963f66afa6" });

        var all = await repository.ListAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        var record = SampleDraftConflict();
        await writer.UpsertAsync(record);

        var reader = new SqliteSyncConflictRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        Assert.Equal(record, loaded);
    }
}
