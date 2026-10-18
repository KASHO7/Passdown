using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Infrastructure.Updates.Installation;

public enum UpdaterExitCode
{
    Succeeded = 0,
    NotInstalled = 1,
    RolledBack = 2,
    RecoveryRequired = 3,
    InvalidUsage = 4,
}

/// <summary>
/// Phase 23D: the external updater's engine, run by Carowinds.Passdown.Updater.exe from the staged copy of the
/// new version (so nothing in the installation folder is locked by the updater itself).
///
/// Sequence: validate the request → wait for the app to exit → re-verify every staged file's SHA-256 →
/// refuse if any installed file is in use (another instance, scheduled refresh, antivirus) or the folder is not
/// writable or short of space → copy the new files into <c>.carowinds-update\incoming-&lt;id&gt;</c> (nothing
/// installed is touched yet) → swap: move each old file into <c>.carowinds-update\backup-&lt;id&gt;</c> and the new
/// one into place, journaling every step → start the new version → wait for it to confirm it is running.
/// Success is recorded only after that confirmation; then the backup is deleted. If the swap fails, or the new
/// version does not confirm in time, every step is reversed from the journal, the previous version is started
/// again, and the record says RolledBack. If even that fails, the record says RecoveryRequired, the backup is
/// kept, and UPDATE-RECOVERY.txt in the installation folder explains how to restore it by hand.
/// Only files of the old and new versions are moved; user files, the data folder and anything else are never
/// touched (all user data lives under %LOCALAPPDATA%, not in the installation folder).
/// </summary>
public sealed class UpdateInstaller(UpdatePaths paths, IUpdaterEnvironment environment, string updaterBaseDirectory)
{
    public static readonly TimeSpan AppExitTimeout = TimeSpan.FromMinutes(3);

    public static readonly TimeSpan InUseRetryWindow = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan LaunchConfirmationTimeout = TimeSpan.FromMinutes(3);

    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public const long FreeSpaceMarginBytes = 32L * 1024 * 1024;

    public const string InstalledFilesListName = "installed-files.json";

    public const string RecoveryNoteName = "UPDATE-RECOVERY.txt";

    public async Task<UpdaterExitCode> RunAsync(string requestPath, CancellationToken cancellationToken = default)
    {
        var request = LoadAndValidate(requestPath, out var invalidReason);
        if (request is null)
        {
            return UpdaterExitCode.InvalidUsage;
        }

        using var updaterLock = TryAcquireLock();
        if (updaterLock is null)
        {
            Record(request, UpdateInstallStatus.NotInstalled, UpdateFailureCodes.AnotherUpdateRunning);
            return UpdaterExitCode.NotInstalled;
        }

        if (invalidReason is not null)
        {
            Record(request, UpdateInstallStatus.NotInstalled, UpdateFailureCodes.InvalidRequest);
            return UpdaterExitCode.NotInstalled;
        }

        Record(request, UpdateInstallStatus.InProgress, null);

        if (!await environment.WaitForExitAsync(request.AppProcessId, request.AppProcessStartUtc, AppExitTimeout, cancellationToken).ConfigureAwait(false))
        {
            return Fail(request, UpdateFailureCodes.AppDidNotClose);
        }

        if (!StagedFilesMatch(request))
        {
            return Fail(request, UpdateFailureCodes.StagedFilesInvalid);
        }

        var installDirectory = request.InstallDirectory;
        var workDirectory = Path.Combine(installDirectory, UpdatePaths.InstallWorkDirectoryName);
        var incoming = Path.Combine(workDirectory, $"incoming-{request.RequestId:N}");
        var backup = Path.Combine(workDirectory, $"backup-{request.RequestId:N}");

        var previousFiles = ReadInstalledFilesList(workDirectory);
        var targets = request.Files.Select(file => file.Path)
            .Concat(previousFiles.Where(path => !request.Files.Any(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        var inUse = await WaitUntilNotInUseAsync(installDirectory, targets, cancellationToken).ConfigureAwait(false);
        if (inUse is { } failureCode)
        {
            return Fail(request, failureCode);
        }

        if (!CanWrite(workDirectory))
        {
            return Fail(request, UpdateFailureCodes.NotWritable);
        }

        var needed = request.Files.Sum(file => file.Size) + FreeSpaceMarginBytes;
        if (environment.AvailableFreeSpace(installDirectory) is { } free && free < needed)
        {
            return Fail(request, UpdateFailureCodes.InsufficientDiskSpace);
        }

        if (CopyIncoming(request, incoming) is { } copyFailure)
        {
            SafeZipExtractor.TryDeleteDirectory(incoming);
            return Fail(request, copyFailure);
        }

        var journal = new List<UpdateSwapEntry>();
        try
        {
            Directory.CreateDirectory(backup);
            foreach (var relative in targets)
            {
                var installed = Combine(installDirectory, relative);
                var saved = Combine(backup, relative);
                var isNew = request.Files.Any(file => string.Equals(file.Path, relative, StringComparison.OrdinalIgnoreCase));
                var entry = new UpdateSwapEntry(relative, BackedUp: false, Placed: false);
                journal.Add(entry);

                if (File.Exists(installed))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Move(installed, saved);
                    entry = entry with { BackedUp = true };
                    journal[^1] = entry;
                    SaveJournal(request, journal, backup);
                }

                if (isNew)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
                    File.Move(Combine(incoming, relative), installed);
                    journal[^1] = entry with { Placed = true };
                    SaveJournal(request, journal, backup);
                }
            }
        }
        catch (Exception)
        {
            // Any failure mid-swap (not only I/O) is reversed from the journal before anything else happens.
            if (!Rollback(installDirectory, backup, journal))
            {
                return RecoveryRequired(request, journal, backup);
            }

            SafeZipExtractor.TryDeleteDirectory(incoming);
            SafeZipExtractor.TryDeleteDirectory(backup);
            return Fail(request, UpdateFailureCodes.ReplaceFailed);
        }

        SafeZipExtractor.TryDeleteDirectory(incoming);
        WriteInstalledFilesList(workDirectory, request.Files.Select(file => file.Path));

        // Files are replaced. That is not success yet: success is the new version confirming it started.
        Record(request, UpdateInstallStatus.AwaitingLaunchConfirmation, null, backup, journal);
        var executable = Combine(installDirectory, UpdateManifestJson.MainExecutable);
        using var started = environment.StartApp(executable, installDirectory);
        if (started is null)
        {
            return await RollBackAfterLaunchFailureAsync(request, journal, backup, UpdateFailureCodes.LaunchFailed, null, previousFiles, cancellationToken).ConfigureAwait(false);
        }

        if (!await WaitForConfirmationAsync(request, started, cancellationToken).ConfigureAwait(false))
        {
            return await RollBackAfterLaunchFailureAsync(request, journal, backup, UpdateFailureCodes.NewVersionDidNotStart, started, previousFiles, cancellationToken).ConfigureAwait(false);
        }

        Record(request, UpdateInstallStatus.Succeeded, null, null, journal);
        SafeZipExtractor.TryDeleteDirectory(backup);
        TryDelete(paths.RequestFile(request.RequestId));
        return UpdaterExitCode.Succeeded;
    }

    /// <summary>
    /// Reads the request and checks it against what this updater may do. Returns null (nothing recorded) when
    /// the file is unreadable or outside the requests folder; otherwise the request, with
    /// <paramref name="invalidReason"/> set when a field fails validation.
    /// </summary>
    public UpdateInstallRequest? LoadAndValidate(string requestPath, out string? invalidReason)
    {
        invalidReason = null;
        if (string.IsNullOrWhiteSpace(requestPath) || !UpdatePaths.IsWithin(requestPath, paths.Requests))
        {
            return null;
        }

        var request = UpdateJson.TryRead<UpdateInstallRequest>(requestPath);
        if (request is null || request.RequestId == Guid.Empty || Path.GetFullPath(requestPath) != Path.GetFullPath(paths.RequestFile(request.RequestId)))
        {
            return null;
        }

        invalidReason = Validate(request);
        return request;
    }

    private string? Validate(UpdateInstallRequest request)
    {
        if (request.FormatVersion != UpdateInstallRequest.CurrentFormatVersion || request.ProductId != UpdateManifest.CarowindsOperationsProductId)
        {
            return "format or product";
        }

        if (!SemanticVersion.TryParse(request.FromVersion, out var from) || !SemanticVersion.TryParse(request.ToVersion, out var to) || to <= from)
        {
            return "versions";
        }

        if (request.AppProcessId <= 0 || request.Files is not { Count: > 0 } || request.Files.Count > UpdateManifestJson.MaximumFiles)
        {
            return "process or files";
        }

        // The updater may only install the staged folder it is itself running from.
        if (!Path.IsPathFullyQualified(request.StagedDirectory)
            || !SameDirectory(request.StagedDirectory, updaterBaseDirectory)
            || !UpdatePaths.IsWithin(request.StagedDirectory, paths.Staging)
            || UpdatePaths.IsLink(request.StagedDirectory))
        {
            return "staged directory";
        }

        var install = request.InstallDirectory;
        if (!Path.IsPathFullyQualified(install)
            || !Directory.Exists(install)
            || UpdatePaths.IsLink(install)
            || string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(install)), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(Path.GetFullPath(install)) ?? string.Empty), StringComparison.OrdinalIgnoreCase)
            || UpdatePaths.IsWithin(install, paths.Root)
            || UpdatePaths.IsWithin(paths.Root, install)
            || !File.Exists(Combine(install, UpdateManifestJson.MainExecutable))
            || !File.Exists(Combine(install, "Carowinds.Passdown.Desktop.dll")))
        {
            return "install directory";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in request.Files)
        {
            if (!UpdateManifestJson.IsSafeRelativePath(file.Path) || !UpdateManifestJson.IsSha256(file.Sha256) || file.Size < 0 || !seen.Add(file.Path))
            {
                return "file list";
            }
        }

        return UpdateManifestJson.RequiredFiles.All(required => seen.Contains(required)) ? null : "required files";
    }

    private bool StagedFilesMatch(UpdateInstallRequest request)
    {
        try
        {
            var expected = new HashSet<string>(request.Files.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
            var actual = Directory.EnumerateFiles(request.StagedDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(request.StagedDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
                .ToList();
            if (actual.Count != expected.Count || actual.Any(path => !expected.Contains(path)))
            {
                return false;
            }

            return request.Files.All(file =>
            {
                var path = Combine(request.StagedDirectory, file.Path);
                var info = new FileInfo(path);
                return info.Exists && !UpdatePaths.IsLink(info) && info.Length == file.Size && SafeZipExtractor.HashFile(path) == file.Sha256;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<string?> WaitUntilNotInUseAsync(string installDirectory, IReadOnlyList<string> targets, CancellationToken cancellationToken)
    {
        var deadline = environment.UtcNow + InUseRetryWindow;
        while (true)
        {
            var result = ProbeInUse(installDirectory, targets);
            if (result is null || result == UpdateFailureCodes.NotWritable || environment.UtcNow >= deadline)
            {
                return result;
            }

            await environment.DelayAsync(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Another instance (or the 6 AM scheduled refresh) is detected two ways: by process path, and — because a
    /// running .exe/.dll can be renamed on Windows even while loaded — by opening every file that would be
    /// replaced for exclusive write. Either one blocks the install; no process is ever stopped.
    /// </summary>
    private string? ProbeInUse(string installDirectory, IReadOnlyList<string> targets)
    {
        if (environment.CountInstancesRunningFrom(installDirectory) > 0)
        {
            return UpdateFailureCodes.InstallationInUse;
        }

        foreach (var relative in targets)
        {
            var path = Combine(installDirectory, relative);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (UnauthorizedAccessException)
            {
                return UpdateFailureCodes.NotWritable;
            }
            catch (IOException)
            {
                return UpdateFailureCodes.InstallationInUse;
            }
        }

        return null;
    }

    private static bool CanWrite(string workDirectory)
    {
        try
        {
            Directory.CreateDirectory(workDirectory);
            if (UpdatePaths.IsLink(workDirectory))
            {
                return false;
            }

            var probe = Path.Combine(workDirectory, $"write-probe-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, [1]);
            var renamed = probe + ".renamed";
            File.Move(probe, renamed);
            File.Delete(renamed);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? CopyIncoming(UpdateInstallRequest request, string incoming)
    {
        try
        {
            SafeZipExtractor.TryDeleteDirectory(incoming);
            Directory.CreateDirectory(incoming);
            foreach (var file in request.Files)
            {
                var target = Combine(incoming, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Combine(request.StagedDirectory, file.Path), target, overwrite: false);
                if (SafeZipExtractor.HashFile(target) != file.Sha256)
                {
                    return UpdateFailureCodes.CopyFailed;
                }
            }

            return null;
        }
        catch (IOException ex) when (ex.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027))
        {
            return UpdateFailureCodes.InsufficientDiskSpace;
        }
        catch (UnauthorizedAccessException)
        {
            return UpdateFailureCodes.NotWritable;
        }
        catch (IOException)
        {
            return UpdateFailureCodes.CopyFailed;
        }
    }

    /// <summary>Reverses the journal newest-first: remove each placed new file, move each backed-up old file back.</summary>
    private static bool Rollback(string installDirectory, string backup, IReadOnlyList<UpdateSwapEntry> journal)
    {
        var ok = true;
        foreach (var entry in journal.Reverse())
        {
            var installed = Combine(installDirectory, entry.Path);
            try
            {
                if (entry.Placed && File.Exists(installed))
                {
                    File.Delete(installed);
                }

                if (entry.BackedUp)
                {
                    File.Move(Combine(backup, entry.Path), installed, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ok = false;
            }
        }

        return ok;
    }

    private async Task<bool> WaitForConfirmationAsync(UpdateInstallRequest request, IStartedApp started, CancellationToken cancellationToken)
    {
        var deadline = environment.UtcNow + LaunchConfirmationTimeout;
        while (environment.UtcNow < deadline)
        {
            if (UpdateJson.TryRead<LaunchConfirmation>(paths.ConfirmationFile(request.RequestId)) is { } confirmation
                && confirmation.RequestId == request.RequestId
                && confirmation.Version == request.ToVersion)
            {
                return true;
            }

            if (started.HasExited)
            {
                // One last look: the app may have confirmed and then been closed by the user straight away.
                return UpdateJson.TryRead<LaunchConfirmation>(paths.ConfirmationFile(request.RequestId)) is { } late && late.Version == request.ToVersion;
            }

            await environment.DelayAsync(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<UpdaterExitCode> RollBackAfterLaunchFailureAsync(UpdateInstallRequest request, List<UpdateSwapEntry> journal, string backup, string failureCode, IStartedApp? started, IReadOnlyList<string> previousFiles, CancellationToken cancellationToken)
    {
        if (started is { HasExited: false })
        {
            started.Kill();
            await started.WaitForExitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        }

        var workDirectory = Path.Combine(request.InstallDirectory, UpdatePaths.InstallWorkDirectoryName);
        await WaitUntilNotInUseAsync(request.InstallDirectory, journal.Select(entry => entry.Path).ToList(), cancellationToken).ConfigureAwait(false);
        if (!Rollback(request.InstallDirectory, backup, journal))
        {
            return RecoveryRequired(request, journal, backup);
        }

        WriteInstalledFilesList(workDirectory, previousFiles);
        SafeZipExtractor.TryDeleteDirectory(backup);
        Record(request, UpdateInstallStatus.RolledBack, failureCode, null, journal);

        // Bring the user back to the version that was working. Started once; never retried in a loop.
        using var previous = environment.StartApp(Combine(request.InstallDirectory, UpdateManifestJson.MainExecutable), request.InstallDirectory);
        return UpdaterExitCode.RolledBack;
    }

    private UpdaterExitCode RecoveryRequired(UpdateInstallRequest request, IReadOnlyList<UpdateSwapEntry> journal, string backup)
    {
        Record(request, UpdateInstallStatus.RecoveryRequired, UpdateFailureCodes.RollbackFailed, backup, journal);
        try
        {
            File.WriteAllText(Combine(request.InstallDirectory, RecoveryNoteName),
                $"""
                Carowinds Operations could not finish updating from {request.FromVersion} to {request.ToVersion}, and could not put the previous version back automatically.

                Your data is safe: drafts, notes, History, prices and settings are stored in %LOCALAPPDATA%\Carowinds Passdown and were not touched.

                To restore the previous version:
                1. Make sure Carowinds Operations is closed.
                2. Open this folder: {backup}
                3. Copy everything in it into this folder ({request.InstallDirectory}), replacing existing files.
                4. Start Carowinds.Passdown.Desktop.exe.

                If that does not work, extract the previous release ZIP into a new folder and run it from there.
                """);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The record still names the backup folder.
        }

        return UpdaterExitCode.RecoveryRequired;
    }

    private UpdaterExitCode Fail(UpdateInstallRequest request, string failureCode)
    {
        Record(request, UpdateInstallStatus.NotInstalled, failureCode);
        return UpdaterExitCode.NotInstalled;
    }

    private void SaveJournal(UpdateInstallRequest request, IReadOnlyList<UpdateSwapEntry> journal, string backup) =>
        Record(request, UpdateInstallStatus.InProgress, null, backup, journal);

    private void Record(UpdateInstallRequest request, UpdateInstallStatus status, string? failureCode, string? backupDirectory = null, IReadOnlyList<UpdateSwapEntry>? journal = null)
    {
        var record = new UpdateInstallRecord(
            1, request.RequestId, request.FromVersion, request.ToVersion, request.InstallDirectory, request.StagedDirectory,
            status, failureCode, DateTimeOffset.UtcNow, backupDirectory, journal?.ToList() ?? (IReadOnlyList<UpdateSwapEntry>)[]);
        try
        {
            UpdateJson.WriteAtomic(paths.RecordFile(request.RequestId), record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The install continues; the app shows "status unknown" rather than a false success.
        }
    }

    private FileStream? TryAcquireLock()
    {
        try
        {
            Directory.CreateDirectory(paths.Root);
            return new FileStream(Path.Combine(paths.Root, "updater.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadInstalledFilesList(string workDirectory)
    {
        var list = UpdateJson.TryRead<string[]>(Path.Combine(workDirectory, InstalledFilesListName));
        return list?.Where(UpdateManifestJson.IsSafeRelativePath).Take(UpdateManifestJson.MaximumFiles).ToList() ?? [];
    }

    private static void WriteInstalledFilesList(string workDirectory, IEnumerable<string> files)
    {
        try
        {
            UpdateJson.WriteAtomic(Path.Combine(workDirectory, InstalledFilesListName), files.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only used to remove obsolete files next time; harmless if missing.
        }
    }

    private static string Combine(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static bool SameDirectory(string left, string right) =>
        UpdatePaths.IsWithin(left, right) && UpdatePaths.IsWithin(right, left);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
