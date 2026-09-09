using System.Text.Json;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The minimum file that identifies a folder as a Carowinds Passdown sync folder (Phase 16B §12).
/// Deliberately not a record index or catalog — the per-record files under <c>drafts/</c>,
/// <c>notes/</c>, and <c>history/</c> remain authoritative; this only lets a reader recognize the
/// folder format and its version before touching anything else in it.
/// </summary>
public sealed record SyncManifest(int SyncSchemaVersion, string Product, DateTimeOffset CreatedUtc)
{
    public const string ProductName = "Carowinds Passdown Personal Sync";
}

/// <summary>Canonical JSON for <see cref="SyncManifest"/> — same option shape as <see cref="SyncEnvelopeSerializer"/>.</summary>
public static class SyncManifestSerializer
{
    public const int CurrentSyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion;

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static string Serialize(SyncManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, Options);
    }
}
