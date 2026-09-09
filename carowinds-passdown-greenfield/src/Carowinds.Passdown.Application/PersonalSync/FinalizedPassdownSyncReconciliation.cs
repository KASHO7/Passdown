namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// What a sync pass should do with one incoming remote finalized-passdown envelope (Phase 16E §12-§14).
/// Deliberately a separate, simpler decision set from <see cref="SyncImportDecision"/> — insert-only,
/// content-hash-dedup History has no "safe update"/"tombstone" concept at all (Phase 16D's own §Phase
/// 16E-readiness note: "History's own insert-only/content-hash dedup rule is a different — simpler —
/// conflict model than drafts/notes', so 16E will still need its own decision path, not Decide reused
/// verbatim").
/// </summary>
public enum FinalizedPassdownSyncDecision
{
    /// <summary>Self-authored (this device's own <see cref="SyncEnvelope.DeviceId"/>) — nothing to do.</summary>
    Ignored,

    /// <summary>No local record exists with this id, and no local record exists at all for this operating date — a fresh second device's normal case. Import it as-is, preserving its identity exactly.</summary>
    RemoteOnlyImport,

    /// <summary>A local record with this exact id already exists and its <see cref="Persistence.FinalizedPassdown.ContentHash"/> matches the remote's — the same record, seen again. Deduplicate; never rewrite the local PDF.</summary>
    Converged,

    /// <summary>
    /// A local record with this exact id exists but its content hash differs from the remote's (Phase
    /// 16E §13). Two devices cannot legitimately mint the same finalization <c>Guid</c> for different
    /// content — this is a serious integrity condition, never auto-resolved and never overwriting
    /// either side.
    /// </summary>
    ImmutableConflict,

    /// <summary>
    /// No local record exists with this exact id, but a local record already exists for the same
    /// operating date under a different id (Phase 16E §14) — two devices independently finalized the
    /// same date. Never silently kept both without surfacing, never silently chosen; both source
    /// artifacts are preserved as-is.
    /// </summary>
    DualFinalizationConflict,
}

/// <summary>
/// The single pure decision function behind every finalized-passdown import (Phase 16E §12-§14) — no
/// filesystem, no repository access, mirroring <see cref="SyncReconciliation"/>'s own design so every
/// scenario is directly unit-testable. <see cref="PersonalSync.PersonalSyncService"/> (Infrastructure)
/// supplies the already-looked-up local facts and acts on the returned decision.
/// </summary>
public static class FinalizedPassdownSyncReconciliation
{
    /// <param name="localDeviceId">This device's own <see cref="IDeviceIdProvider"/> id.</param>
    /// <param name="envelopeDeviceId">The incoming envelope's <see cref="SyncEnvelope.DeviceId"/>.</param>
    /// <param name="remoteContentHash">The incoming envelope's <see cref="SyncEnvelope.ContentHash"/> (already validated non-empty by the caller).</param>
    /// <param name="localContentHashForSameId">The local <see cref="Persistence.FinalizedPassdown.ContentHash"/> when a local record with the exact same id already exists; <see langword="null"/> otherwise.</param>
    /// <param name="localRecordExistsForSameDate">
    /// <see langword="true"/> when a local record (necessarily under a <em>different</em> id, since
    /// <paramref name="localContentHashForSameId"/> is <see langword="null"/> whenever this is
    /// consulted) already exists for the same operating date. Ignored when
    /// <paramref name="localContentHashForSameId"/> is not <see langword="null"/>.
    /// </param>
    public static FinalizedPassdownSyncDecision Decide(
        Guid localDeviceId,
        Guid envelopeDeviceId,
        string remoteContentHash,
        string? localContentHashForSameId,
        bool localRecordExistsForSameDate)
    {
        if (envelopeDeviceId == localDeviceId)
        {
            // Our own previously exported file, read back during this pass's scan — never a remote change.
            return FinalizedPassdownSyncDecision.Ignored;
        }

        if (localContentHashForSameId is not null)
        {
            return string.Equals(localContentHashForSameId, remoteContentHash, StringComparison.Ordinal)
                ? FinalizedPassdownSyncDecision.Converged
                : FinalizedPassdownSyncDecision.ImmutableConflict;
        }

        return localRecordExistsForSameDate
            ? FinalizedPassdownSyncDecision.DualFinalizationConflict
            : FinalizedPassdownSyncDecision.RemoteOnlyImport;
    }
}
