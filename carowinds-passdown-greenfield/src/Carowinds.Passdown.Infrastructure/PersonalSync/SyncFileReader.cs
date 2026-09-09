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
