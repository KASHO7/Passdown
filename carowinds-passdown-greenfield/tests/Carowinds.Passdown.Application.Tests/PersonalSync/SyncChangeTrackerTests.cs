using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Tests.TestSupport;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>Phase 16C §7: the local-save "mark dirty" hook — creates sync metadata for a never-before-tracked entity, and bumps <see cref="SyncEntityState.LocalRevision"/> on every subsequent call.</summary>
public class SyncChangeTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MarkLocalChangeAsync_FirstCallForAnEntity_CreatesStateAtRevisionOne()
    {
        var repository = new InMemorySyncEntityStateRepository();
        var tracker = new SyncChangeTracker(repository, new FakeClock(Now));

        await tracker.MarkLocalChangeAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        var state = await repository.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09");
        Assert.NotNull(state);
        Assert.Equal(1, state!.LocalRevision);
        Assert.Equal(0, state.SyncedRevision);
        Assert.Null(state.SyncedContentHash);
        Assert.Equal(SyncConflictState.None, state.ConflictState);
    }

    [Fact]
    public async Task MarkLocalChangeAsync_SecondCall_IncrementsLocalRevision_AndPreservesSyncedState()
    {
        var repository = new InMemorySyncEntityStateRepository();
        var tracker = new SyncChangeTracker(repository, new FakeClock(Now));

        await repository.UpsertAsync(SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, "2026-09-09", Now) with
        {
            LocalRevision = 3,
            SyncedRevision = 3,
            SyncedContentHash = "abc123",
        });

        await tracker.MarkLocalChangeAsync(SyncEntityType.AssociateDraft, "2026-09-09");

        var state = await repository.GetAsync(SyncEntityType.AssociateDraft, "2026-09-09");
        Assert.Equal(4, state!.LocalRevision);
        Assert.Equal(3, state.SyncedRevision); // Unchanged — dirtiness is decided by content hash comparison at sync time, not here.
        Assert.Equal("abc123", state.SyncedContentHash);
    }

    [Fact]
    public async Task MarkLocalChangeAsync_DifferentEntityTypesWithTheSameId_AreTrackedIndependently()
    {
        var repository = new InMemorySyncEntityStateRepository();
        var tracker = new SyncChangeTracker(repository, new FakeClock(Now));
        const string sameId = "3fa85f64-5717-4562-b3fc-2c963f66afa6";

        await tracker.MarkLocalChangeAsync(SyncEntityType.OperationalNote, sameId);
        await tracker.MarkLocalChangeAsync(SyncEntityType.OperationalNote, sameId);

        var noteState = await repository.GetAsync(SyncEntityType.OperationalNote, sameId);
        var draftState = await repository.GetAsync(SyncEntityType.AssociateDraft, sameId);

        Assert.Equal(2, noteState!.LocalRevision);
        Assert.Null(draftState);
    }

    private sealed class InMemorySyncEntityStateRepository : ISyncEntityStateRepository
    {
        private readonly Dictionary<(SyncEntityType, string), SyncEntityState> _states = new();

        public Task<SyncEntityState?> GetAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_states.GetValueOrDefault((entityType, entityId)));

        public Task<IReadOnlyList<SyncEntityState>> ListByTypeAsync(SyncEntityType entityType, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SyncEntityState> results = _states.Values.Where(s => s.EntityType == entityType).ToList();
            return Task.FromResult(results);
        }

        public Task UpsertAsync(SyncEntityState state, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
        {
            _states[(state.EntityType, state.EntityId)] = state;
            return Task.CompletedTask;
        }

        public Task ClearAllAsync(CancellationToken cancellationToken = default)
        {
            _states.Clear();
            return Task.CompletedTask;
        }
    }
}
