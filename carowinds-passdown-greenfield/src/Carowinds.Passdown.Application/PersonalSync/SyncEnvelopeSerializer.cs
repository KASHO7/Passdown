using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Canonical JSON for <see cref="SyncEnvelope"/> (Phase 16B §9). Mirrors
/// <c>FinalizedPassdownSnapshotSerializer</c>'s options exactly (stable Pascal-case property names,
/// enums as names not integers, no indentation instability) so the two envelope-style contracts in
/// this codebase serialize the same way. <see cref="Deserialize"/> throws on malformed JSON — turning
/// that into a structured failure instead of a crash is the safe file reader's job (see
/// <c>Infrastructure.PersonalSync.SyncFileReader</c>), not this type's.
/// </summary>
public static class SyncEnvelopeSerializer
{
    /// <summary>Current envelope shape version — bumped only when the envelope's own fields change, independent of any payload's schema version or the SQLite database schema version.</summary>
    public const int CurrentSyncSchemaVersion = 1;

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, Options);
    }

    public static SyncEnvelope Deserialize(string envelopeJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopeJson);
        return JsonSerializer.Deserialize<SyncEnvelope>(envelopeJson, Options)
            ?? throw new InvalidOperationException("Sync envelope JSON deserialized to null.");
    }
}
