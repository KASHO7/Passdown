using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// Phase 18B REL-007: removes stale temp files that an interrupted <see cref="AtomicFileWriter"/>
/// write (process crash, power loss, a failed best-effort delete) left inside the shared sync folder —
/// otherwise OneDrive would carry them to every device forever. Runs once at the start of each sync
/// pass and is strictly best-effort: it never throws and never blocks the pass.
/// <para>
/// A file is deleted only when <b>all</b> of these hold:
/// <list type="number">
/// <item>It is directly inside one of the app-owned directories this type knows (the
/// <see cref="SyncPathService.SyncFolderName"/> folder and its <c>drafts</c>, <c>notes</c>,
/// <c>history</c>, <c>monthly-ert</c>, <c>ert-date-overrides</c> subfolders) — enumerated
/// non-recursively, never the user's selected root itself, never anything else in OneDrive.</item>
/// <item>Its canonical full path's parent is exactly that directory (no <c>..</c>, rooted, UNC, or
/// alternate-separator escape), and neither the file nor the directory is a symbolic link/junction.</item>
/// <item>Its name is exactly <c>&lt;canonical sync file name&gt;.tmp-&lt;32 hex&gt;</c> for that
/// directory (the final name must round-trip through <see cref="SyncPathService"/>), or the folder
/// validator's own <c>.sync-probe-&lt;32 hex&gt;.tmp</c> probe in the sync folder.</item>
/// <item>It was last written at least <see cref="StaleAfter"/> ago, so a write in progress on this or
/// any other device (a normal write takes milliseconds) can never be deleted underneath its writer.</item>
/// </list>
/// Legitimate sync files (<c>2026-09-19.json</c>, history PDFs, the manifest, OneDrive conflict copies,
/// anything a user put there) never match and are never touched.
/// </para>
/// </summary>
public static partial class SyncTempFileCleaner
{
    /// <summary>24 hours — orders of magnitude longer than any real write, and tolerant of modest clock skew between devices.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    private enum Folder
    {
        SyncRoot,
        Drafts,
        Notes,
        History,
        MonthlyErt,
        ErtDateOverrides,
    }

    /// <returns>The number of stale temp files deleted.</returns>
    public static int CleanStaleTempFiles(string selectedRootPath, TimeProvider? timeProvider = null, TimeSpan? staleAfter = null)
    {
        var nowUtc = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var threshold = staleAfter ?? StaleAfter;
        var deleted = 0;

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(selectedRootPath);

            var syncFolder = SyncPathService.GetSyncFolderPath(selectedRootPath);
            if (!Directory.Exists(syncFolder) || IsLink(new DirectoryInfo(syncFolder)))
            {
                return 0;
            }

            foreach (var (folder, directory) in new (Folder, string)[]
            {
                (Folder.SyncRoot, syncFolder),
                (Folder.Drafts, SyncPathService.GetDraftsDirectoryPath(selectedRootPath)),
                (Folder.Notes, SyncPathService.GetNotesDirectoryPath(selectedRootPath)),
                (Folder.History, SyncPathService.GetHistoryDirectoryPath(selectedRootPath)),
                (Folder.MonthlyErt, SyncPathService.GetMonthlyErtsDirectoryPath(selectedRootPath)),
                (Folder.ErtDateOverrides, SyncPathService.GetErtDateOverridesDirectoryPath(selectedRootPath)),
            })
            {
                deleted += CleanDirectory(folder, selectedRootPath, directory, nowUtc, threshold);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // Best-effort hygiene only; the sync pass itself reports any real folder problem.
        }

        return deleted;
    }

    private static int CleanDirectory(Folder folder, string selectedRootPath, string directory, DateTime nowUtc, TimeSpan threshold)
    {
        var directoryInfo = new DirectoryInfo(directory);
        if (!directoryInfo.Exists || IsLink(directoryInfo))
        {
            return 0;
        }

        var expectedDirectory = Path.GetFullPath(directory);
        var deleted = 0;

        foreach (var candidate in Directory.EnumerateFiles(expectedDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (!IsContainedDirectlyIn(expectedDirectory, candidate)
                    || !IsRecognizedTempFileName(folder, selectedRootPath, Path.GetFileName(candidate)))
                {
                    continue;
                }

                var info = new FileInfo(candidate);
                if (!info.Exists || IsLink(info) || nowUtc - info.LastWriteTimeUtc < threshold)
                {
                    continue;
                }

                info.Delete();
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked, vanished (OneDrive moved/deleted it first), or not ours to delete — leave it.
            }
        }

        return deleted;
    }

    /// <summary>
    /// True only when <paramref name="candidatePath"/>, canonicalized, sits directly inside
    /// <paramref name="expectedDirectory"/> — a <c>..</c> segment, a rooted/UNC path, an alternate
    /// separator, or a nested subfolder all fail.
    /// </summary>
    internal static bool IsContainedDirectlyIn(string expectedDirectory, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(expectedDirectory))
        {
            return false;
        }

        string fullCandidate;
        string fullExpected;
        try
        {
            fullExpected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedDirectory));
            fullCandidate = Path.GetFullPath(candidatePath, fullExpected);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var parent = Path.GetDirectoryName(fullCandidate);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return parent is not null && string.Equals(Path.TrimEndingDirectorySeparator(parent), fullExpected, comparison);
    }

    /// <summary>The exact temp names this app can have created in <paramref name="folder"/> — nothing else.</summary>
    private static bool IsRecognizedTempFileName(Folder folder, string root, string fileName)
    {
        if (folder == Folder.SyncRoot && SyncProbeFileName().IsMatch(fileName))
        {
            return true;
        }

        if (!AtomicFileWriter.TryParseTempFileName(fileName, out var finalName))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(finalName);
        string? canonical = folder switch
        {
            Folder.SyncRoot => Path.GetFileName(SyncPathService.GetManifestFilePath(root)),
            Folder.Drafts when SyncEntityIdentity.TryParseDraftId(stem, out var date) => Path.GetFileName(SyncPathService.GetDraftFilePath(root, date)),
            Folder.Notes when SyncEntityIdentity.TryParseNoteId(stem, out var id) => Path.GetFileName(SyncPathService.GetOperationalNoteFilePath(root, id)),
            Folder.History when SyncPathService.TryParseHistoryFileName(stem, out var date, out var id) => Path.GetExtension(finalName) == ".pdf"
                ? Path.GetFileName(SyncPathService.GetHistoryPdfFilePath(root, date, id))
                : Path.GetFileName(SyncPathService.GetHistoryJsonFilePath(root, date, id)),
            Folder.MonthlyErt when SyncEntityIdentity.TryParseMonthlyErtId(stem, out var year, out var month) => Path.GetFileName(SyncPathService.GetMonthlyErtFilePath(root, year, month)),
            Folder.ErtDateOverrides when SyncEntityIdentity.TryParseErtDateOverrideId(stem, out var date) => Path.GetFileName(SyncPathService.GetErtDateOverrideFilePath(root, date)),
            _ => null,
        };

        return canonical is not null && string.Equals(canonical, finalName, StringComparison.Ordinal);
    }

    private static bool IsLink(FileSystemInfo info)
    {
        // LinkTarget is non-null only for symbolic links and junctions (name-surrogate reparse points) —
        // not for OneDrive Files-On-Demand cloud placeholders, which are ordinary files to us.
        return info.LinkTarget is not null;
    }

    [GeneratedRegex(@"^\.sync-probe-[0-9a-f]{32}\.tmp$", RegexOptions.CultureInvariant)]
    private static partial Regex SyncProbeFileName();
}
