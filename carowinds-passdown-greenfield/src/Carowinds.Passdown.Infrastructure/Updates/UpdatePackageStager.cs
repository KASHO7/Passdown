using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Infrastructure.Updates.Installation;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Updates;

/// <summary>
/// Phase 23D: <see cref="IUpdatePackageStager"/>. Downloads to <c>downloads\&lt;version&gt;\…zip.partial</c>,
/// verifies the exact size and SHA-256 from the signed manifest, renames to <c>.zip</c>, then extracts into a
/// brand-new <c>staging\&lt;version&gt;-&lt;id&gt;</c> folder through <see cref="SafeZipExtractor"/>. The downloaded
/// ZIP is deleted once extracted. A failed or cancelled attempt deletes its partial files, so a stale or
/// tampered package can never be picked up later: every attempt starts from a fresh download.
/// </summary>
public sealed class UpdatePackageStager(
    IUpdateReleaseSource source,
    UpdatePaths paths,
    Func<string, long?>? availableFreeSpace = null,
    ILogger<UpdatePackageStager>? logger = null) : IUpdatePackageStager
{
    /// <summary>Extra headroom beyond the package and its extracted files.</summary>
    public const long FreeSpaceMarginBytes = 64L * 1024 * 1024;

    /// <summary>A staging folder this young may belong to an install that is still running; cleanup leaves it.</summary>
    public static readonly TimeSpan CleanupGracePeriod = TimeSpan.FromHours(1);

    private readonly Func<string, long?> _availableFreeSpace = availableFreeSpace ?? DiskSpace.AvailableBytes;

    public async Task<StagedUpdate> StageAsync(UpdateOffer offer, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken = default)
    {
        var manifest = offer.Manifest;
        var required = manifest.Package.Size + manifest.TotalFileBytes + FreeSpaceMarginBytes;

        string downloadDirectory;
        try
        {
            Directory.CreateDirectory(paths.Downloads);
            Directory.CreateDirectory(paths.Staging);
            downloadDirectory = Path.Combine(paths.Downloads, manifest.Version.Normalized);
            SafeZipExtractor.TryDeleteDirectory(downloadDirectory);
            Directory.CreateDirectory(downloadDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateStagingException(StagingFailureKind.StorageError, "The update folder could not be prepared.", ex);
        }

        if (_availableFreeSpace(paths.Root) is { } free && free < required)
        {
            throw new UpdateStagingException(StagingFailureKind.InsufficientDiskSpace, "There is not enough free disk space to download the update.");
        }

        var partialPath = Path.Combine(downloadDirectory, manifest.Package.FileName + ".partial");
        var zipPath = Path.Combine(downloadDirectory, manifest.Package.FileName);
        try
        {
            await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var byteProgress = progress is null ? null : new Progress<long>(bytes => progress.Report(new UpdateDownloadProgress(bytes, manifest.Package.Size)));
                await source.DownloadPackageAsync(offer.Release, manifest.Package.FileName, output, manifest.Package.Size, byteProgress, cancellationToken).ConfigureAwait(false);
            }

            var length = new FileInfo(partialPath).Length;
            if (length != manifest.Package.Size)
            {
                throw new UpdateStagingException(StagingFailureKind.SizeMismatch, "The downloaded update is not the expected size.");
            }

            if (!string.Equals(SafeZipExtractor.HashFile(partialPath), manifest.Package.Sha256, StringComparison.Ordinal))
            {
                throw new UpdateStagingException(StagingFailureKind.HashMismatch, "The downloaded update does not match the signed release.");
            }

            File.Move(partialPath, zipPath);
            var stagedDirectory = Path.Combine(paths.Staging, $"{manifest.Version.Normalized}-{Guid.NewGuid():N}");
            SafeZipExtractor.ExtractVerified(zipPath, manifest, stagedDirectory);
            logger?.LogInformation("Update {Version} downloaded, verified and staged.", manifest.Version.Normalized);
            return new StagedUpdate(offer, stagedDirectory);
        }
        catch (UpdateSourceException ex)
        {
            throw new UpdateStagingException(StagingFailureKind.Download, ex.Message, ex);
        }
        catch (IOException ex) when (ex.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027))
        {
            throw new UpdateStagingException(StagingFailureKind.InsufficientDiskSpace, "There is not enough free disk space.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateStagingException(StagingFailureKind.StorageError, "The update could not be saved.", ex);
        }
        finally
        {
            SafeZipExtractor.TryDeleteDirectory(downloadDirectory);
        }
    }

    public void CleanObsolete()
    {
        var active = new UpdateInstallRecordStore(paths).ActiveStagedDirectories();
        foreach (var root in new[] { paths.Downloads, paths.Staging })
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var directory in Directory.EnumerateDirectories(root))
                {
                    var info = new DirectoryInfo(directory);
                    if (active.Any(path => UpdatePaths.IsWithin(path, directory)) || DateTime.UtcNow - info.LastWriteTimeUtc < CleanupGracePeriod)
                    {
                        continue;
                    }

                    SafeZipExtractor.TryDeleteDirectory(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogDebug("Update cleanup skipped a folder ({ExceptionType}).", ex.GetType().Name);
            }
        }
    }
}

/// <summary>Phase 23D: free space on the volume holding a path; null when it cannot be determined.</summary>
public static class DiskSpace
{
    public static long? AvailableBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
