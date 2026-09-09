using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IPersonalSyncConflictResolutionService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.PersonalSyncSettingsViewModel"/> tests — never touches the filesystem or a real database.</summary>
internal sealed class FakePersonalSyncConflictResolutionService : IPersonalSyncConflictResolutionService
{
    public IReadOnlyList<SyncConflictSummary> NextConflicts { get; set; } = [];

    public DraftConflictDetail? NextDraftDetail { get; set; }

    public NoteConflictDetail? NextNoteDetail { get; set; }

    public ConflictResolutionResult NextResolution { get; set; } = ConflictResolutionResult.Resolved();

    public int GetConflictsCallCount { get; private set; }

    public (SyncEntityType EntityType, string EntityId)? LastResolvedEntity { get; private set; }

    public string? LastResolutionKind { get; private set; }

    public Task<IReadOnlyList<SyncConflictSummary>> GetConflictsAsync(CancellationToken cancellationToken = default)
    {
        GetConflictsCallCount++;
        return Task.FromResult(NextConflicts);
    }

    public Task<DraftConflictDetail?> GetDraftConflictDetailAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        Task.FromResult(NextDraftDetail);

    public Task<NoteConflictDetail?> GetNoteConflictDetailAsync(Guid noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(NextNoteDetail);

    public Task<ConflictResolutionResult> ResolveKeepLocalAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
    {
        LastResolvedEntity = (entityType, entityId);
        LastResolutionKind = "KeepLocal";
        return Task.FromResult(NextResolution);
    }

    public Task<ConflictResolutionResult> ResolveUseRemoteAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
    {
        LastResolvedEntity = (entityType, entityId);
        LastResolutionKind = "UseRemote";
        return Task.FromResult(NextResolution);
    }

    public Task<ConflictResolutionResult> ResolveAcceptDeletionAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
    {
        LastResolvedEntity = (entityType, entityId);
        LastResolutionKind = "AcceptDeletion";
        return Task.FromResult(NextResolution);
    }
}
