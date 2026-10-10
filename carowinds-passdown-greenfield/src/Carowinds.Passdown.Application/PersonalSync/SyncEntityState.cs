using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Whether a syncable entity currently has an unresolved conflict (Phase 16C §13/§14, Phase 16D §3).
/// Conceptually this is the "Detected" state in Phase 16D's conflict lifecycle (None -&gt; Detected -&gt;
/// resolved back to None); the enum member keeps its original 16C name (<see cref="Conflicting"/>)
/// rather than being renamed, to avoid an unnecessary breaking rename of already-shipped, already-tested
/// 16C code for a purely cosmetic difference. There is no separate persisted "Resolving" state: a
/// resolution command is one synchronous attempt against this same "Detected" precondition — a failed
/// attempt (e.g. the sync folder is unavailable) simply leaves the entity Conflicting, safely retryable
/// by calling the same resolution command again (Phase 16D §7/§21).
/// </summary>
public enum SyncConflictState
{
    None,
    Conflicting,
}

/// <summary>
/// Local revision/conflict metadata for one syncable entity, keyed by <see cref="EntityType"/> +
/// <see cref="EntityId"/> (Phase 16C §4/§5) — the one generic sync metadata store, instead of adding
/// sync columns to every domain table. Never duplicates the entity's own payload; only enough to
/// answer "what do I currently have," "what did I last confirm was shared," and "did I diverge since
/// then."
///
/// Semantics:
/// <list type="bullet">
/// <item><see cref="LocalRevision"/>: a local, monotonically increasing counter bumped once per local
/// save of this entity (via <see cref="ISyncChangeTracker"/>) or once per applied remote update. This
/// is the value written into an exported <see cref="SyncEnvelope.Revision"/> — it is never a
/// timestamp and never resets.</item>
/// <item><see cref="SyncedRevision"/>/<see cref="SyncedContentHash"/>: the <see cref="LocalRevision"/>
/// and canonical content hash as of the last point this device confirmed its local content matched
/// what the sync folder holds — either because it just exported that exact content, or because it
/// just imported and applied a remote update. <see cref="SyncedRevision"/> of 0 with a null
/// <see cref="SyncedContentHash"/> means this entity has never been synced (a fresh bootstrap
/// candidate).</item>
/// <item><see cref="LastRemoteDeviceId"/>/<see cref="LastRemoteRevision"/>: the envelope identity of
/// the last remote change this device successfully incorporated (applied or found identical) — lets a
/// later scan of the same unchanged file recognize it as already-seen without re-comparing content.</item>
/// <item>Conflict fields: set only when an incoming remote envelope's content differs both from the
/// current local content and from <see cref="SyncedContentHash"/> (a genuine concurrent edit) — see
/// <see cref="SyncReconciliation"/>. Left untouched (not advanced) while conflicting, so the same
/// unresolved conflict is consistently re-detected rather than silently dropped. Deliberately does not
/// carry the conflicting payload itself — only enough identity/fingerprint to describe the conflict.</item>
/// </list>
/// </summary>
public sealed record SyncEntityState(
    SyncEntityType EntityType,
    string EntityId,
    int LocalRevision,
    int SyncedRevision,
    string? SyncedContentHash,
    Guid? LastRemoteDeviceId,
    int? LastRemoteRevision,
    SyncConflictState ConflictState,
    Guid? ConflictRemoteDeviceId,
    int? ConflictRemoteRevision,
    string? ConflictRemoteContentHash,
    DateTimeOffset? ConflictDetectedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>A brand-new entity that has never been tracked for sync before — <see cref="LocalRevision"/> starts at 1, matching "this device's first known state of it."</summary>
    public static SyncEntityState CreateNew(SyncEntityType entityType, string entityId, DateTimeOffset nowUtc) => new(
        entityType,
        entityId,
        LocalRevision: 1,
        SyncedRevision: 0,
        SyncedContentHash: null,
        LastRemoteDeviceId: null,
        LastRemoteRevision: null,
        ConflictState: SyncConflictState.None,
        ConflictRemoteDeviceId: null,
        ConflictRemoteRevision: null,
        ConflictRemoteContentHash: null,
        ConflictDetectedAtUtc: null,
        UpdatedAtUtc: nowUtc);

    public bool IsDirty(string currentLocalContentHash) =>
        !string.Equals(SyncedContentHash, currentLocalContentHash, StringComparison.Ordinal);
}

/// <summary>Persistence for <see cref="SyncEntityState"/> (Phase 16C §5/§32) — one generic store keyed by (EntityType, EntityId), never a per-domain-table column addition.</summary>
public interface ISyncEntityStateRepository
{
    Task<SyncEntityState?> GetAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncEntityState>> ListByTypeAsync(SyncEntityType entityType, CancellationToken cancellationToken = default);

    /// <summary>Inserts or fully replaces the state row for (<paramref name="state"/>'s EntityType, EntityId).</summary>
    Task UpsertAsync(SyncEntityState state, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every row, across every entity type (Phase 16F §19/§20) — used only when a sync pass
    /// discovers the configured folder's <see cref="SyncManifest.SyncSpaceId"/> no longer matches the
    /// one this device last reconciled against (a folder switch to a different sync universe), so no
    /// stale <see cref="SyncEntityState.SyncedContentHash"/> from the old folder can cause a record to
    /// be silently skipped as "already synced" against the new one. Never called for an ordinary sync
    /// pass against the same, unchanged folder.
    /// </summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
