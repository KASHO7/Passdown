using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carowinds.Passdown.Application.Finalization;

/// <summary>
/// Canonical, deterministic serialization for <see cref="FinalizedPassdownSnapshot"/> (spec Phase 9
/// Parts E/H/S) — the one place that decides how a snapshot becomes
/// <see cref="Persistence.FinalizedPassdown.SnapshotJson"/> and back, and how
/// <see cref="Persistence.FinalizedPassdown.ContentHash"/> is computed from it.
/// </summary>
public static class FinalizedPassdownSnapshotSerializer
{
    /// <summary>
    /// Deliberately narrow and explicit (spec Part S) rather than <see cref="JsonSerializerOptions.Default"/>:
    /// enums serialize as their (stable, readable) names rather than an underlying integer that would
    /// silently renumber if an enum member were ever reordered; property names use the record's own
    /// declared (Pascal-case) names with no camel-casing rewrite; nothing here reflects over private
    /// members, resolves polymorphic type metadata, or otherwise varies with runtime type identity —
    /// every type in the snapshot graph is a purpose-built sealed record/enum, never a source DB
    /// entity serialized directly.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(FinalizedPassdownSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, Options);
    }

    public static FinalizedPassdownSnapshot Deserialize(string snapshotJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        return JsonSerializer.Deserialize<FinalizedPassdownSnapshot>(snapshotJson, Options)
            ?? throw new InvalidOperationException("Finalized passdown snapshot JSON deserialized to null.");
    }

    /// <summary>
    /// Lowercase-hex SHA-256 of <paramref name="snapshotJson"/> (spec Part H) — integrity metadata
    /// only, to detect accidental mutation/corruption of a historical row, never used for
    /// security/authentication. Hashes the exact JSON text that is/was stored, so re-serializing an
    /// already-deserialized snapshot and hashing that is equivalent only because <see cref="Options"/>
    /// is deterministic (spec Part S) — stable property order (record declaration order), no
    /// indentation, and a fixed enum representation.
    /// </summary>
    public static string ComputeContentHash(string snapshotJson)
    {
        ArgumentNullException.ThrowIfNull(snapshotJson);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson));
        return Convert.ToHexStringLower(bytes);
    }
}
