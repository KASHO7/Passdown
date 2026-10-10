using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The durable record of one unresolved conflict (Phase 16D §2/§3), keyed by (<see cref="EntityType"/>,
/// <see cref="EntityId"/>) — at most one per entity, matching <see cref="SyncEntityState.ConflictState"/>'s
/// own single-conflict-per-entity model. Persists the full conflicting remote envelope — including its
/// payload — so resolution can safely happen after an app restart or after the OneDrive folder
/// temporarily disappears, without relying on the canonical file still holding the same content by the
/// time the user opens the conflict UI (§2). Never duplicates <see cref="SyncEntityState"/>'s own
/// identity/hash bookkeeping; this is the one place the actual conflicting content lives.
/// </summary>
/// <param name="RemoteIsDeleted">
/// True when the conflicting remote envelope is a tombstone (a delete-vs-edit conflict, Phase 16D
/// §14/§15) — in which case <see cref="RemotePayloadJson"/>/<see cref="RemotePayloadSchemaVersion"/> are
/// null. False for an ordinary edit-vs-edit conflict, and also for the "local considered this entity
/// deleted, remote sent live content" mirror case (Phase 16D §17) — which direction applies is decided
/// at resolution time by checking whether the local entity currently exists, never stored here.
/// </param>
/// <param name="RemotePayloadJson">The remote envelope's payload, serialized exactly as it will be re-applied (opaque to this type) — null only when <see cref="RemoteIsDeleted"/>.</param>
/// <param name="NewerRemoteDeviceId">
/// Set when a later sync pass sees a further remote change for this same entity while this conflict is
/// still unresolved (Phase 16D §10) — the original conflict's own fields above are deliberately left
/// untouched (never silently replaced); these three fields only surface that newer activity exists.
/// </param>
public sealed record SyncConflictRecord(
    SyncEntityType EntityType,
    string EntityId,
    Guid RemoteDeviceId,
    int RemoteRevision,
    DateTimeOffset RemoteModifiedUtc,
    bool RemoteIsDeleted,
    int? RemotePayloadSchemaVersion,
    string? RemotePayloadJson,
    string? RemoteContentHash,
    DateTimeOffset DetectedAtUtc,
    Guid? NewerRemoteDeviceId,
    int? NewerRemoteRevision,
    DateTimeOffset? NewerRemoteDetectedAtUtc);

/// <summary>Persistence for <see cref="SyncConflictRecord"/> (Phase 16D §2/§31) — a dedicated table alongside <see cref="ISyncEntityStateRepository"/>, never an entity-specific conflict table per spec's explicit preference.</summary>
public interface ISyncConflictRepository
{
    Task<SyncConflictRecord?> GetAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default);

    /// <summary>Every currently-unresolved conflict, across all entity types, in no particular guaranteed order — callers apply their own presentation ordering.</summary>
    Task<IReadOnlyList<SyncConflictRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts or fully replaces the conflict row for (<paramref name="record"/>'s EntityType, EntityId).</summary>
    Task UpsertAsync(SyncConflictRecord record, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>Clears a resolved (or no-longer-applicable) conflict. A no-op if none exists — resolution must stay idempotent (Phase 16D §21).</summary>
    Task DeleteAsync(SyncEntityType entityType, string entityId, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every conflict row (Phase 16F §19/§20) — used only alongside <see cref="ISyncEntityStateRepository.ClearAllAsync"/>
    /// on a folder-switch rebase: a conflict recorded against the old sync folder's remote content no
    /// longer describes anything relevant once the device has moved to a different sync universe.
    /// </summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
