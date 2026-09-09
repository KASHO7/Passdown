using System.Text.Json;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>Which local entity kind a <see cref="SyncEnvelope"/> wraps (Phase 16A §D).</summary>
public enum SyncEntityType
{
    AssociateDraft,
    OperationalNote,
    FinalizedPassdown,
}

/// <summary>
/// The versioned wrapper every synced entity file carries (Phase 16A §D). 16B implements this
/// contract and its serialization only — no entity is actually wrapped, written, or read through it
/// yet (that begins in 16C). Three schema-version fields are deliberately independent of one
/// another: <see cref="SyncSchemaVersion"/> versions this envelope shape; <see cref="PayloadSchemaVersion"/>
/// is a copy of the wrapped entity's own existing schema version (e.g.
/// <c>AssociateDraftPayload.SchemaVersion</c>); neither has any relationship to the SQLite database
/// schema version. <see cref="Revision"/> is a per-<c>(EntityId, DeviceId)</c> monotonically
/// increasing counter, not a timestamp — <see cref="ModifiedUtc"/> exists for display/ordering only
/// and must never be the sole arbiter of which write wins a conflict (Phase 16A §G).
/// </summary>
public sealed record SyncEnvelope
{
    public required int SyncSchemaVersion { get; init; }

    public required SyncEntityType EntityType { get; init; }

    /// <summary>The entity's own existing identity (Phase 16A §B) — a <c>DateOnly</c> ISO string for a draft, a <c>Guid</c> string for a note or finalized passdown.</summary>
    public required string EntityId { get; init; }

    public required Guid DeviceId { get; init; }

    public required int Revision { get; init; }

    public required DateTimeOffset ModifiedUtc { get; init; }

    /// <summary>Tombstone flag, set only by an explicit local action — never inferred from a file's absence (Phase 16A §G).</summary>
    public required bool IsDeleted { get; init; }

    public required int PayloadSchemaVersion { get; init; }

    /// <summary>The wrapped entity's existing JSON shape, byte-for-byte — the envelope never re-shapes it.</summary>
    public required JsonElement Payload { get; init; }

    /// <summary>Present only for <see cref="SyncEntityType.FinalizedPassdown"/> — reuses <c>FinalizedPassdownSnapshotSerializer.ComputeContentHash</c>, not a new hash scheme.</summary>
    public string? ContentHash { get; init; }
}
