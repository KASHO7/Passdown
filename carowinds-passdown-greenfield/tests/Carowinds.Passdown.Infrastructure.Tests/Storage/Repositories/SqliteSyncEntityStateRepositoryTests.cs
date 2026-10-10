using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

/// <summary>Phase 16C §5/§32: the one generic sync metadata store over the <c>sync_entity_state</c> table (DB schema 12).</summary>
public class SqliteSyncEntityStateRepositoryTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);

    public void Dispose() => _fixture.Dispose();

    private SqliteSyncEntityStateRepository CreateRepository() => new(_fixture.ConnectionFactory);

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
        var deviceId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        var remoteDeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var state = new SyncEntityState(
            SyncEntityType.AssociateDraft, "2026-09-09",
            LocalRevision: 3, SyncedRevision: 2, SyncedContentHash: "abc123",
            LastRemoteDeviceId: deviceId, LastRemoteRevision: 2,
            ConflictState: SyncConflictState.Conflicting,
            ConflictRemoteDeviceId: remoteDeviceId, ConflictRemoteRevision: 4, ConflictRemoteContentHash: "def456",
            ConflictDetectedAtUtc: Now, UpdatedAtUtc: Now);

        await repository.UpsertAsync(state);
        var loaded = await repository.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        Assert.Equal(state, loaded);
    }

    [Fact]
    public async Task UpsertAsync_CalledTwiceForTheSameKey_ReplacesRatherThanDuplicating()
    {
        var repository = CreateRepository();
        var first = SyncEntityState.CreateNew(SyncEntityType.OperationalNote, "3fa85f64-5717-4562-b3fc-2c963f66afa6", Now);
        var second = first with { LocalRevision = 5, SyncedRevision = 5, SyncedContentHash = "newHash" };

        await repository.UpsertAsync(first);
        await repository.UpsertAsync(second);
        var loaded = await repository.GetAsync(SyncEntityType.OperationalNote, "3fa85f64-5717-4562-b3fc-2c963f66afa6");

        Assert.Equal(second, loaded);
    }

    [Fact]
    public async Task GetAsync_SameEntityIdDifferentEntityType_AreDistinctRows()
    {
        var repository = CreateRepository();
        const string sharedId = "3fa85f64-5717-4562-b3fc-2c963f66afa6";
        await repository.UpsertAsync(SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, sharedId, Now) with { LocalRevision = 1 });
        await repository.UpsertAsync(SyncEntityState.CreateNew(SyncEntityType.OperationalNote, sharedId, Now) with { LocalRevision = 9 });

        var draftState = await repository.GetAsync(SyncEntityType.AssociateDraft, sharedId);
        var noteState = await repository.GetAsync(SyncEntityType.OperationalNote, sharedId);

        Assert.Equal(1, draftState!.LocalRevision);
        Assert.Equal(9, noteState!.LocalRevision);
    }

    [Fact]
    public async Task ListByTypeAsync_OnlyReturnsRowsOfTheRequestedType()
    {
        var repository = CreateRepository();
        await repository.UpsertAsync(SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, "2026-09-09", Now));
        await repository.UpsertAsync(SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, "2026-09-10", Now));
        await repository.UpsertAsync(SyncEntityState.CreateNew(SyncEntityType.OperationalNote, "3fa85f64-5717-4562-b3fc-2c963f66afa6", Now));

        var drafts = await repository.ListByTypeAsync(SyncEntityType.AssociateDraft);

        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, s => Assert.Equal(SyncEntityType.AssociateDraft, s.EntityType));
    }

    [Fact]
    public async Task UpsertAsync_WithNullConflictAndRemoteFields_RoundTripsNulls()
    {
        var repository = CreateRepository();
        var state = SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, "2026-09-09", Now);

        await repository.UpsertAsync(state);
        var loaded = await repository.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        Assert.Null(loaded!.SyncedContentHash);
        Assert.Null(loaded.LastRemoteDeviceId);
        Assert.Null(loaded.LastRemoteRevision);
        Assert.Null(loaded.ConflictRemoteDeviceId);
        Assert.Null(loaded.ConflictRemoteRevision);
        Assert.Null(loaded.ConflictRemoteContentHash);
        Assert.Null(loaded.ConflictDetectedAtUtc);
    }

    [Fact]
    public async Task Data_PersistsAcrossAReopenedConnection()
    {
        var writer = CreateRepository();
        var state = SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, "2026-09-09", Now);
        await writer.UpsertAsync(state);

        var reader = new SqliteSyncEntityStateRepository(_fixture.ReopenConnectionFactory());
        var loaded = await reader.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        Assert.Equal(state, loaded);
    }
}
