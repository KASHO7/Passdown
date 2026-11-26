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

    /// <summary>Phase 17B — delegates to <see cref="MonthlyErtSyncPayloadSerializer"/>, mirroring <see cref="ComputeNoteHash"/>.</summary>
    public static string ComputeMonthlyErtHash(MonthlyErtSyncPayload payload) =>
        Compute(MonthlyErtSyncPayloadSerializer.Serialize(payload));

    /// <summary>Phase 17B — delegates to <see cref="ErtDateOverrideSyncPayloadSerializer"/>, mirroring <see cref="ComputeNoteHash"/>.</summary>
    public static string ComputeErtDateOverrideHash(ErtDateOverrideSyncPayload payload) =>
        Compute(ErtDateOverrideSyncPayloadSerializer.Serialize(payload));

    /// <summary>
    /// Phase 31B: the <em>meaning</em> of a monthly ERT configuration for conflict detection — state,
    /// start time, the order-independent ride set (<see cref="Domain.Ert.ErtRideSelection.CanonicalSet"/>)
    /// and the trimmed note; never the edit time or the rides' display order. Used only to recognize two
    /// devices that agree; <see cref="ComputeMonthlyErtHash"/> stays the stored dirty-tracking hash.
    /// </summary>
    public static string ComputeMonthlyErtSemanticHash(MonthlyErtSyncPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Compute(JsonSerializer.Serialize(new object?[]
        {
            "monthly-ert-semantic/v1",
            payload.Year,
            payload.Month,
            payload.State.ToString(),
            payload.StartTime?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            Domain.Ert.ErtRideSelection.CanonicalSet(payload.Rides),
            NormalizeText(payload.Note),
        }));
    }

    /// <summary>Phase 31B: <see cref="ComputeMonthlyErtSemanticHash"/>'s date-override counterpart.</summary>
    public static string ComputeErtDateOverrideSemanticHash(ErtDateOverrideSyncPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Compute(JsonSerializer.Serialize(new object?[]
        {
            "ert-date-override-semantic/v1",
            payload.OperatingDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            payload.State.ToString(),
            payload.StartTime?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            Domain.Ert.ErtRideSelection.CanonicalSet(payload.Rides),
            NormalizeText(payload.Reason),
        }));
    }

    private static string? NormalizeText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Compute(string canonicalJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
}
