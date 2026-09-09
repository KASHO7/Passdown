using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>Outcome of <see cref="SyncManifestService.EnsureManifestAsync"/> (Phase 16B §12).</summary>
public enum SyncManifestEnsureOutcome
{
    /// <summary>A valid manifest now exists — either it already did, or one was just written.</summary>
    Ready,

    /// <summary>manifest.json exists but is not valid JSON for <see cref="SyncManifest"/> — never auto-repaired (that recovery UX is deferred to a later phase).</summary>
    Malformed,

    /// <summary>manifest.json declares a <see cref="SyncManifest.SyncSchemaVersion"/> newer than this app understands.</summary>
    UnsupportedVersion,

    /// <summary>The folder could not be read or written right now (a transient I/O condition, not a corrupt file).</summary>
    Unavailable,
}

public sealed record SyncManifestEnsureResult(SyncManifestEnsureOutcome Outcome, string? Message)
{
    public static SyncManifestEnsureResult Ready() => new(SyncManifestEnsureOutcome.Ready, null);

    public static SyncManifestEnsureResult Malformed(string message) => new(SyncManifestEnsureOutcome.Malformed, message);

    public static SyncManifestEnsureResult UnsupportedVersion(string message) => new(SyncManifestEnsureOutcome.UnsupportedVersion, message);

    public static SyncManifestEnsureResult Unavailable(string message) => new(SyncManifestEnsureOutcome.Unavailable, message);
}

/// <summary>
/// Creates or validates <c>manifest.json</c> — the minimum file identifying a folder as a Carowinds
/// Passdown sync folder (Phase 16B §12). Only ever called once Personal Sync is explicitly enabled
/// with a folder that already passed <see cref="ISyncFolderValidator"/> — never during normal
/// startup with sync disabled (spec §12).
/// </summary>
public static class SyncManifestService
{
    public static async Task<SyncManifestEnsureResult> EnsureManifestAsync(
        string selectedRootPath, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var manifestPath = SyncPathService.GetManifestFilePath(selectedRootPath);

        var readResult = await SyncFileReader.ReadJsonAsync<SyncManifest>(
            manifestPath,
            SyncManifestSerializer.Options,
            manifest => manifest.SyncSchemaVersion <= SyncManifestSerializer.CurrentSyncSchemaVersion,
            cancellationToken).ConfigureAwait(false);

        switch (readResult.Status)
        {
            case SyncFileReadStatus.Success:
                return SyncManifestEnsureResult.Ready();

            case SyncFileReadStatus.Missing:
                return await WriteNewManifestAsync(manifestPath, nowUtc, cancellationToken).ConfigureAwait(false);

            case SyncFileReadStatus.Malformed:
                return SyncManifestEnsureResult.Malformed(readResult.ErrorMessage ?? "manifest.json is corrupted.");

            case SyncFileReadStatus.UnsupportedVersion:
                return SyncManifestEnsureResult.UnsupportedVersion(
                    readResult.ErrorMessage ?? "manifest.json was created by a newer version of this app.");

            case SyncFileReadStatus.IoError:
                return SyncManifestEnsureResult.Unavailable(readResult.ErrorMessage ?? "The sync folder is not available right now.");

            default:
                throw new InvalidOperationException($"Unhandled {nameof(SyncFileReadStatus)}: {readResult.Status}");
        }
    }

    private static async Task<SyncManifestEnsureResult> WriteNewManifestAsync(
        string manifestPath, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var manifest = new SyncManifest(SyncManifestSerializer.CurrentSyncSchemaVersion, SyncManifest.ProductName, nowUtc);
        var json = SyncManifestSerializer.Serialize(manifest);

        try
        {
            await AtomicFileWriter.WriteTextAsync(manifestPath, json, cancellationToken).ConfigureAwait(false);
            return SyncManifestEnsureResult.Ready();
        }
        catch (IOException ex)
        {
            return SyncManifestEnsureResult.Unavailable(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return SyncManifestEnsureResult.Unavailable(ex.Message);
        }
    }
}
