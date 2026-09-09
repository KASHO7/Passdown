using System.Text.Json;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The minimum file that identifies a folder as a Carowinds Passdown sync folder (Phase 16B §12).
/// Deliberately not a record index or catalog — the per-record files under <c>drafts/</c>,
/// <c>notes/</c>, and <c>history/</c> remain authoritative; this only lets a reader recognize the
/// folder format and its version before touching anything else in it.
/// </summary>
/// <param name="SyncSpaceId">
/// Phase 16F §20: a stable identity for THIS sync folder/manifest, generated once and never
/// reassigned while the folder exists — lets a device tell "I'm still talking to the same sync
/// universe I was last time" apart from "the user pointed me at a different (possibly empty) folder,"
/// which a device's own <see cref="SyncEntityState.SyncedContentHash"/> bookkeeping cannot distinguish
/// on its own (see <c>PersonalSyncService</c>'s folder-switch reconciliation). Defaults to
/// <see cref="Guid.Empty"/> so a manifest written before 16F still deserializes; <c>SyncManifestService</c>
/// backfills a real value into any manifest it reads with the empty default, once, the first time a
/// 16F+ build reads it. Deliberately never <c>DeviceId</c> (Phase 16A §C) — one identifies a folder, the
/// other identifies a device; conflating them would make it impossible to tell "two devices sharing one
/// folder" apart from "one device that switched folders."
/// </param>
public sealed record SyncManifest(int SyncSchemaVersion, string Product, DateTimeOffset CreatedUtc, Guid SyncSpaceId = default)
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
