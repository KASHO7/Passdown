namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>What a sync pass should do with one incoming remote envelope, given local state (Phase 16C §10-13). Never a "silently pick a winner" outcome.</summary>
public enum SyncImportDecision
{
    /// <summary>Self-authored (this device's own <see cref="SyncEnvelope.DeviceId"/>) or an already-incorporated remote revision — nothing to do.</summary>
    Ignored,

    /// <summary>No local entity exists at all — a fresh second device's normal case. Import it as-is, preserving its identity exactly.</summary>
    RemoteOnlyImport,

    /// <summary>Local content already matches the remote content byte-for-byte after canonicalization — nothing to apply, just record that both sides agree.</summary>
    Converged,

    /// <summary>Local content has not diverged from the last confirmed-shared state — safe to replace it with the remote update.</summary>
    SafeRemoteUpdate,

    /// <summary>Local content differs from both the remote content and the last confirmed-shared state — a genuine concurrent edit. Never applied automatically.</summary>
    Conflict,
}

/// <summary>
/// The single pure decision function behind every import in Phase 16C (§10-§13) — no filesystem, no
/// repository access, so every conflict/idempotence scenario in the spec is directly unit-testable
/// without real files or a real database. <see cref="PersonalSync.PersonalSyncService"/> (Infrastructure)
/// is the only caller; it supplies the local facts (does the entity exist, what does it currently
/// hash to, what sync state is on record) and acts on the returned decision.
/// </summary>
public static class SyncReconciliation
{
    public static SyncImportDecision Decide(
        Guid localDeviceId,
        SyncEnvelope envelope,
        bool localEntityExists,
        string? localContentHash,
        SyncEntityState? state,
        string remoteContentHash)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.DeviceId == localDeviceId)
        {
            // Our own previously exported file, read back during this pass's scan — never a remote change.
            return SyncImportDecision.Ignored;
        }

        if (state is { LastRemoteDeviceId: { } lastDeviceId, LastRemoteRevision: { } lastRevision }
            && lastDeviceId == envelope.DeviceId
            && envelope.Revision <= lastRevision)
        {
            // Already incorporated (same or older revision from the same remote device) — idempotent re-scan.
            return SyncImportDecision.Ignored;
        }

        if (!localEntityExists)
        {
            return SyncImportDecision.RemoteOnlyImport;
        }

        if (string.Equals(localContentHash, remoteContentHash, StringComparison.Ordinal))
        {
            return SyncImportDecision.Converged;
        }

        var lastSyncedHash = state?.SyncedContentHash;
        if (lastSyncedHash is not null && string.Equals(localContentHash, lastSyncedHash, StringComparison.Ordinal))
        {
            // Local hasn't changed since the last confirmed-shared state — the remote side moved, we didn't.
            return SyncImportDecision.SafeRemoteUpdate;
        }

        // Either this is the very first contact between these two copies (no shared baseline) and they
        // differ, or local diverged from the last shared baseline while the remote also changed —
        // either way, a genuine concurrent edit. Never resolved automatically.
        return SyncImportDecision.Conflict;
    }
}
