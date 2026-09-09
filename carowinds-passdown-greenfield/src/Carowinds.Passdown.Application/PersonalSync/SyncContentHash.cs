using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Deterministic payload fingerprinting (Phase 16C §6) so unchanged records are never repeatedly
/// rewritten and so a genuine concurrent edit can be told apart from a harmless re-save of identical
/// content. Always hashes a fresh, compact, fixed-option re-serialization of the strongly-typed
/// payload — never the raw text of whatever file/column the JSON happened to come from — so
/// indentation, property order, or the surrounding envelope's own formatting can never affect the
/// hash (sync metadata itself never affects the payload hash).
/// </summary>
public static class SyncContentHash
{
    /// <summary>
    /// Sentinel value for <see cref="SyncEntityState.SyncedContentHash"/> meaning "as of the last
    /// confirmed sync, this device understood the entity as deleted" (Phase 16D §11/§13) — never a real
    /// SHA-256 hex digest (those are always 64 hex characters), so it can never collide with genuine
    /// content. Reusing the existing <see cref="SyncEntityState.SyncedContentHash"/>/dirty-comparison
    /// mechanism for deletion state means no additional schema column is needed to track "is this
    /// entity's deletion already synced."
    /// </summary>
    public const string TombstoneMarker = "TOMBSTONE";

    /// <summary>Compact, stable-order options shared by every hash input — deliberately distinct from <see cref="AssociateDraftPayloadSerializer"/>'s storage format and from <see cref="Application.PersonalSync.SyncEnvelopeSerializer"/>'s indented file format.</summary>
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ComputeDraftHash(AssociateDraftPayload payload) =>
        Compute(JsonSerializer.Serialize(payload, CanonicalOptions));

    /// <summary>Delegates to <see cref="OperationalNoteSyncPayloadSerializer"/> rather than a second canonical-options definition — that serializer's compact, fixed-property-order, string-enum output is already a canonical form.</summary>
    public static string ComputeNoteHash(OperationalNoteSyncPayload payload) =>
        Compute(OperationalNoteSyncPayloadSerializer.Serialize(payload));

    private static string Compute(string canonicalJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
}
