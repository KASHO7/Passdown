using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// <see cref="ISyncFolderValidator"/> that checks a usable filesystem location — never an
/// authentication or "is this really OneDrive" check (Phase 16B §6). Rejects a selection that
/// resolves inside <see cref="AppPaths.LocalApplicationDataDirectory"/> (the live SQLite database's
/// directory) as structurally <see cref="SyncFolderValidationOutcome.Invalid"/>; every other failure
/// (missing, unwritable, transient I/O) is <see cref="SyncFolderValidationOutcome.Unavailable"/>,
/// since it may resolve itself without the user picking a different folder.
/// </summary>
public sealed class SyncFolderValidator : ISyncFolderValidator
{
    public Task<SyncFolderValidationResult> ValidateAsync(string selectedRootPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedRootPath))
        {
            return Task.FromResult(SyncFolderValidationResult.Invalid("Choose a folder to enable Personal Sync."));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(selectedRootPath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return Task.FromResult(SyncFolderValidationResult.Invalid("That folder path is not valid."));
        }

        if (ConflictsWithLocalAppData(fullPath))
        {
            return Task.FromResult(SyncFolderValidationResult.Invalid(
                "This folder can't be inside the app's local data folder — that folder holds the live database and must never be synced. Choose a different folder."));
        }

        try
        {
            var syncFolder = SyncPathService.GetSyncFolderPath(fullPath);
            Directory.CreateDirectory(syncFolder);

            var probePath = Path.Combine(syncFolder, $".sync-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);

            return Task.FromResult(SyncFolderValidationResult.Valid());
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(SyncFolderValidationResult.Unavailable("This folder is not writable right now."));
        }
        catch (IOException)
        {
            return Task.FromResult(SyncFolderValidationResult.Unavailable("This folder is not available right now."));
        }
    }

    private static bool ConflictsWithLocalAppData(string fullPath)
    {
        var normalizedCandidate = NormalizeForComparison(fullPath);
        var normalizedAppData = NormalizeForComparison(Path.GetFullPath(AppPaths.LocalApplicationDataDirectory));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return string.Equals(normalizedCandidate, normalizedAppData, comparison)
            || normalizedCandidate.StartsWith(normalizedAppData + Path.DirectorySeparatorChar, comparison)
            || normalizedAppData.StartsWith(normalizedCandidate + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizeForComparison(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
