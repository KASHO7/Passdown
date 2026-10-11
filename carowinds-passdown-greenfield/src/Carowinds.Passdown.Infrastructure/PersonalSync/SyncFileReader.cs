using System.Text.Json;
using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// Reusable safe reading for sync-folder JSON files (Phase 16A/16B §11): distinguishes success,
/// missing, malformed, unsupported version, and I/O-unavailable, and never lets a raw
/// filesystem/JSON exception escape to a caller. Does not quarantine or delete a corrupt file — that
/// recovery UX is explicitly deferred to a later phase.
/// </summary>
public static class SyncFileReader
{
    /// <summary>
    /// Phase 18A SEC-003: upper bound for any sync-folder JSON file (envelope, manifest). Real files are
    /// a few KB (a finalized History envelope, the largest, embeds one snapshot — typically well under
    /// 100 KB), so 16 MiB is generous headroom while still refusing to buffer a multi-gigabyte
    /// accidental or hostile file into memory. An oversized file is reported as malformed and left
    /// untouched, never deleted.
    /// </summary>
    public const long MaxJsonFileBytes = 16L * 1024 * 1024;

    /// <summary>Phase 18A SEC-003: upper bound for a synced frozen History PDF (real ones are typically under 1 MB).</summary>
    public const long MaxPdfFileBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Reads and deserializes <paramref name="path"/> as <typeparamref name="T"/>.
    /// <paramref name="isSupportedVersion"/> lets each caller decide what "version" means for its own
    /// type (e.g. <see cref="SyncManifest.SyncSchemaVersion"/>) — this type stays generic over any
    /// versioned sync-folder file, not just one shape.
    /// </summary>
    public static async Task<SyncFileReadResult<T>> ReadJsonAsync<T>(
        string path,
        JsonSerializerOptions options,
        Func<T, bool> isSupportedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(isSupportedVersion);

        if (!File.Exists(path))
        {
            return SyncFileReadResult<T>.Missing();
        }

        string json;
        try
        {
            if (new FileInfo(path).Length > MaxJsonFileBytes)
            {
                return SyncFileReadResult<T>.Malformed($"{Path.GetFileName(path)} exceeds the {MaxJsonFileBytes}-byte sync file size limit.");
            }

            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return SyncFileReadResult<T>.IoError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return SyncFileReadResult<T>.IoError(ex.Message);
        }

        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException ex)
        {
            return SyncFileReadResult<T>.Malformed(ex.Message);
        }

        if (value is null)
        {
            return SyncFileReadResult<T>.Malformed($"{path} deserialized to null.");
        }

        return isSupportedVersion(value)
            ? SyncFileReadResult<T>.Success(value)
            : SyncFileReadResult<T>.UnsupportedVersion($"{path} was written by an unsupported schema version.");
    }
}
