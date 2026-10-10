using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IPersonalSyncConflictResolutionService"/> for <see cref="PersonalSyncCoordinator"/> tests — only <see cref="GetConflictsAsync"/> is exercised by the coordinator; the rest are unused stubs.</summary>
internal sealed class FakePersonalSyncConflictResolutionService : IPersonalSyncConflictResolutionService
{
    public IReadOnlyList<SyncConflictSummary> NextConflicts { get; set; } = [];

    public int GetConflictsCallCount { get; private set; }

    public Task<IReadOnlyList<SyncConflictSummary>> GetConflictsAsync(CancellationToken cancellationToken = default)
    {
        GetConflictsCallCount++;
        return Task.FromResult(NextConflicts);
    }

    public Task<DraftConflictDetail?> GetDraftConflictDetailAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        Task.FromResult<DraftConflictDetail?>(null);

    public Task<NoteConflictDetail?> GetNoteConflictDetailAsync(Guid noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult<NoteConflictDetail?>(null);

    public Task<ConflictResolutionResult> ResolveKeepLocalAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConflictResolutionResult.Resolved());

    public Task<ConflictResolutionResult> ResolveUseRemoteAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConflictResolutionResult.Resolved());

    public Task<ConflictResolutionResult> ResolveAcceptDeletionAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConflictResolutionResult.Resolved());
}
