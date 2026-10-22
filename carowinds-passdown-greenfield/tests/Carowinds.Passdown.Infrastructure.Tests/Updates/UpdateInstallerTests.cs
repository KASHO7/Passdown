using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Updates;
using Carowinds.Passdown.Infrastructure.Updates.Installation;

namespace Carowinds.Passdown.Infrastructure.Tests.Updates;

/// <summary>
/// Phase 23D: the external updater's install/rollback engine, run against isolated temporary folders with a fake
/// process environment. No real process is started and the real installation is never touched.
/// </summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private const string OldVersion = "0.1.0-rc.7";
    private const string NewVersion = "0.1.0-rc.8";

    private readonly UpdateTestKit _kit = new();
    private readonly UpdatePaths _paths;
    private readonly string _install;
    private readonly string _dataFolder;
    private readonly FakeEnvironment _environment;

    // These scenarios are about program files; the database stays in one format throughout. The real SQLite guard,
    // and versions that upgrade the database, are covered by UpdateInstallerDatabaseSafetyTests (Phase 27).
    private readonly UnchangedDatabase _database = new();

    public UpdateInstallerTests()
    {
        _paths = new UpdatePaths(Path.Combine(_kit.Root, "Carowinds Operations Updates"));
        _install = Path.Combine(_kit.Root, "Carowinds-Operations-portable");
        _dataFolder = Path.Combine(_kit.Root, "Carowinds Passdown");
        Directory.CreateDirectory(_install);
        foreach (var (path, bytes) in UpdateTestKit.AppFiles(OldVersion))
        {
            var target = Path.Combine(_install, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }

        // Something the user put in the app folder, and the (separate) data folder the app really uses.
        File.WriteAllText(Path.Combine(_install, "my-shortcuts.txt"), "user file");
        Directory.CreateDirectory(_dataFolder);
        File.WriteAllText(Path.Combine(_dataFolder, "carowinds-passdown.db"), "database");
        File.WriteAllText(Path.Combine(_dataFolder, "appearance.json"), "{ \"Appearance\": \"Dark\" }");
        File.WriteAllText(Path.Combine(_dataFolder, "sync-config.json"), "{ \"enabled\": true }");

        _environment = new FakeEnvironment(_paths);
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows() && Directory.Exists(_install))
        {
            File.SetUnixFileMode(_install, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        _kit.Dispose();
    }

    private sealed class FakeEnvironment(UpdatePaths paths) : IUpdaterEnvironment
    {
        public bool AppExits { get; set; } = true;

        public int Instances { get; set; }

        public long? FreeSpace { get; set; }

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        /// <summary>What a started app does: by default the new version confirms and the old one just runs.</summary>
        public Func<string, int, FakeApp?>? OnStart { get; set; }

        public List<string> StartedExecutableContents { get; } = [];

        public Task<bool> WaitForExitAsync(int processId, DateTimeOffset? startUtc, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(AppExits);

        public int CountInstancesRunningFrom(string installDirectory) => Instances;

        public IStartedApp? StartApp(string executablePath, string workingDirectory)
        {
            var content = File.ReadAllText(executablePath);
            StartedExecutableContents.Add(content);
            return OnStart is not null ? OnStart(content, StartedExecutableContents.Count) : DefaultStart(content);
        }

        public FakeApp DefaultStart(string content)
        {
            if (content.EndsWith(NewVersion, StringComparison.Ordinal))
            {
                Confirm();
            }

            return new FakeApp();
        }

        public void Confirm()
        {
            var record = Directory.EnumerateFiles(paths.Records, "install-*.json").Single();
            var id = Guid.Parse(Path.GetFileNameWithoutExtension(record)["install-".Length..]);
            File.WriteAllText(paths.ConfirmationFile(id), JsonSerializer.Serialize(new { requestId = id, version = NewVersion, processId = 1 }));
        }

        public long? AvailableFreeSpace(string path) => FreeSpace;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            UtcNow += delay;
            return Task.CompletedTask;
        }
    }

    private sealed class UnchangedDatabase : IUpdateDatabaseGuard
    {
        public string DatabasePath => "carowinds-passdown.db";

        public DatabaseBackupResult BackUpBeforeInstall(string fromVersion, string toVersion) => new(DatabaseBackupStatus.Created, "backup.db", 16);

        public bool TryReadSchemaVersion(out int? schemaVersion)
        {
            schemaVersion = 16;
            return true;
        }
    }

    private sealed class FakeApp(bool exited = false) : IStartedApp
    {
        public bool HasExited { get; private set; } = exited;

        public bool Killed { get; private set; }

        public void Kill()
        {
            Killed = true;
            HasExited = true;
        }

        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);

        public void Dispose()
        {
        }
    }

    /// <summary>Stages the new release and writes the request exactly as the app does (through the real launcher).</summary>
    private (UpdateInstaller Installer, string RequestPath, string Staged, UpdateManifest Manifest) Prepare(IReadOnlyDictionary<string, byte[]>? files = null, string version = NewVersion)
    {
        var (zip, manifest) = _kit.BuildVerifiedRelease(version, files);
        var staged = Path.Combine(_paths.Staging, $"{version}-{Guid.NewGuid():N}");
        SafeZipExtractor.ExtractVerified(zip, manifest, staged);

        ProcessStartInfo? started = null;
        var launcher = new UpdaterProcessLauncher(_paths, _install, info => { started = info; return true; });
        var offer = new UpdateOffer(new ReleaseCandidate($"v{version}", manifest.Version, null, null, false, []), manifest, manifest.Package.Size);
        var launch = launcher.Launch(new StagedUpdate(offer, staged), SemanticVersion.Parse(OldVersion));
        Assert.True(launch.Started);
        Assert.NotNull(started);
        Assert.False(started.UseShellExecute);
        Assert.Equal([UpdaterProcessLauncher.RequestArgument, _paths.RequestFile(launch.RequestId)], started.ArgumentList);
        Assert.Equal(Path.Combine(staged, "Carowinds.Passdown.Updater.exe"), started.FileName);

        return (new UpdateInstaller(_paths, _environment, staged, _database), _paths.RequestFile(launch.RequestId), staged, manifest);
    }

    private UpdateInstallRecord Record() => new UpdateInstallRecordStore(_paths, _install).LoadLatestRecord()!;

    private string Installed(string path) => File.ReadAllText(Path.Combine(_install, path.Replace('/', Path.DirectorySeparatorChar)));

    private void AssertOldVersionIntact()
    {
        foreach (var (path, bytes) in UpdateTestKit.AppFiles(OldVersion))
        {
            Assert.Equal(Encoding.UTF8.GetString(bytes), Installed(path));
        }

        AssertUserDataUntouched();
    }

    private void AssertUserDataUntouched()
    {
        Assert.Equal("user file", Installed("my-shortcuts.txt"));
        Assert.Equal("database", File.ReadAllText(Path.Combine(_dataFolder, "carowinds-passdown.db")));
        Assert.Equal("{ \"Appearance\": \"Dark\" }", File.ReadAllText(Path.Combine(_dataFolder, "appearance.json")));
        Assert.Equal("{ \"enabled\": true }", File.ReadAllText(Path.Combine(_dataFolder, "sync-config.json")));
    }

    [Fact]
    public async Task SuccessfulUpdate_ReplacesAppFiles_KeepsUserData_RecordsSuccessOnlyAfterConfirmation()
    {
        var (installer, request, _, _) = Prepare();

        var exit = await installer.RunAsync(request);

        Assert.Equal(UpdaterExitCode.Succeeded, exit);
        foreach (var (path, bytes) in UpdateTestKit.AppFiles(NewVersion))
        {
            Assert.Equal(Encoding.UTF8.GetString(bytes), Installed(path));
        }

        AssertUserDataUntouched();
        Assert.Equal(UpdateInstallStatus.Succeeded, Record().Status);
        Assert.Equal([$"desktop-exe {NewVersion}"], _environment.StartedExecutableContents);
        Assert.False(File.Exists(request));
        // The rollback copy is removed only after the confirmed launch.
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_install, UpdatePaths.InstallWorkDirectoryName)));
    }

    [Fact]
    public async Task NextUpdate_RemovesFilesTheNewVersionNoLongerShips_ButNeverUserFiles()
    {
        var (installer, request, _, _) = Prepare();
        Assert.Equal(UpdaterExitCode.Succeeded, await installer.RunAsync(request));
        foreach (var file in Directory.EnumerateFiles(_paths.Records))
        {
            File.Delete(file);
        }

        var files = UpdateTestKit.AppFiles("0.1.0-rc.9");
        files.Remove("LatoFont/OFL.txt");
        var (installer2, request2, _, _) = Prepare(files, "0.1.0-rc.9");
        _environment.OnStart = (content, _) =>
        {
            if (content.EndsWith("0.1.0-rc.9", StringComparison.Ordinal))
            {
                var record = Directory.EnumerateFiles(_paths.Records, "install-*.json").Single();
                var id = Guid.Parse(Path.GetFileNameWithoutExtension(record)["install-".Length..]);
                File.WriteAllText(_paths.ConfirmationFile(id), JsonSerializer.Serialize(new { requestId = id, version = "0.1.0-rc.9", processId = 1 }));
            }

            return new FakeApp();
        };

        // rc.9 is installed over rc.8, so the request says from rc.7; rewrite it to rc.8 the way the app would.
        var json = File.ReadAllText(request2).Replace($"\"fromVersion\": \"{OldVersion}\"", $"\"fromVersion\": \"{NewVersion}\"", StringComparison.Ordinal);
        File.WriteAllText(request2, json);

        Assert.Equal(UpdaterExitCode.Succeeded, await installer2.RunAsync(request2));
        Assert.False(File.Exists(Path.Combine(_install, "LatoFont", "OFL.txt")));
        Assert.Equal("user file", Installed("my-shortcuts.txt"));
    }

    [Fact]
    public async Task NewVersionNeverConfirms_IsKilled_RolledBack_AndThePreviousVersionIsStartedOnce()
    {
        var (installer, request, _, _) = Prepare();
        FakeApp? newApp = null;
        _environment.OnStart = (content, call) => call == 1 ? newApp = new FakeApp() : new FakeApp();

        var exit = await installer.RunAsync(request);

        Assert.Equal(UpdaterExitCode.RolledBack, exit);
        Assert.True(newApp!.Killed);
        AssertOldVersionIntact();
        var record = Record();
        Assert.Equal(UpdateInstallStatus.RolledBack, record.Status);
        Assert.Equal(UpdateFailureCodes.NewVersionDidNotStart, record.FailureCode);
        Assert.Equal([$"desktop-exe {NewVersion}", $"desktop-exe {OldVersion}"], _environment.StartedExecutableContents);
    }

    [Fact]
    public async Task NewVersionCrashesOnStartup_IsRolledBack()
    {
        var (installer, request, _, _) = Prepare();
        _environment.OnStart = (_, call) => new FakeApp(exited: call == 1);

        Assert.Equal(UpdaterExitCode.RolledBack, await installer.RunAsync(request));
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task NewVersionCannotBeStarted_IsRolledBack()
    {
        var (installer, request, _, _) = Prepare();
        _environment.OnStart = (_, call) => call == 1 ? null : new FakeApp();

        Assert.Equal(UpdaterExitCode.RolledBack, await installer.RunAsync(request));
        Assert.Equal(UpdateFailureCodes.LaunchFailed, Record().FailureCode);
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task ReplacementFailureMidSwap_RestoresEveryFile_AndChangesNothing()
    {
        var (installer, request, _, _) = Prepare();
        // A folder where the last file must go makes the final move fail after every other file was swapped.
        File.Delete(Path.Combine(_install, "coreclr.dll"));
        Directory.CreateDirectory(Path.Combine(_install, "coreclr.dll"));

        var exit = await installer.RunAsync(request);

        Assert.Equal(UpdaterExitCode.NotInstalled, exit);
        Assert.Equal(UpdateFailureCodes.ReplaceFailed, Record().FailureCode);
        foreach (var (path, bytes) in UpdateTestKit.AppFiles(OldVersion).Where(f => f.Key != "coreclr.dll"))
        {
            Assert.Equal(Encoding.UTF8.GetString(bytes), Installed(path));
        }

        AssertUserDataUntouched();
        Assert.Empty(_environment.StartedExecutableContents);
    }

    [Fact]
    public async Task RollbackThatCannotComplete_IsRecoveryRequired_KeepsTheBackup_AndWritesInstructions()
    {
        var (installer, request, _, _) = Prepare();
        _environment.OnStart = (_, call) =>
        {
            if (call == 1)
            {
                // Simulate the backup becoming unusable before rollback (e.g. removed by another tool).
                var backup = Directory.EnumerateDirectories(Path.Combine(_install, UpdatePaths.InstallWorkDirectoryName), "backup-*").Single();
                File.Delete(Path.Combine(backup, "Carowinds.Passdown.Desktop.dll"));
            }

            return null;
        };

        var exit = await installer.RunAsync(request);

        Assert.Equal(UpdaterExitCode.RecoveryRequired, exit);
        var record = Record();
        Assert.Equal(UpdateInstallStatus.RecoveryRequired, record.Status);
        Assert.NotNull(record.BackupDirectory);
        Assert.True(Directory.Exists(record.BackupDirectory));
        Assert.NotEmpty(record.Journal);
        Assert.Contains("restore the previous version", File.ReadAllText(Path.Combine(_install, UpdateInstaller.RecoveryNoteName)));
        AssertUserDataUntouched();
    }

    [Fact]
    public async Task LockedExecutable_BlocksTheInstall_WithoutChangingAnything()
    {
        var (installer, request, _, _) = Prepare();
        using (new FileStream(Path.Combine(_install, "Carowinds.Passdown.Desktop.exe"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        }

        Assert.Equal(UpdateFailureCodes.InstallationInUse, Record().FailureCode);
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task AnotherInstanceRunningFromTheFolder_BlocksTheInstall_AndNoProcessIsStopped()
    {
        var (installer, request, _, _) = Prepare();
        _environment.Instances = 1;

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        Assert.Equal(UpdateFailureCodes.InstallationInUse, Record().FailureCode);
        Assert.Empty(_environment.StartedExecutableContents);
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task AppThatDoesNotClose_CancelsTheInstall()
    {
        var (installer, request, _, _) = Prepare();
        _environment.AppExits = false;

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        Assert.Equal(UpdateFailureCodes.AppDidNotClose, Record().FailureCode);
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task InsufficientDiskSpace_ChangesNothing()
    {
        var (installer, request, _, _) = Prepare();
        _environment.FreeSpace = 10;

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        Assert.Equal(UpdateFailureCodes.InsufficientDiskSpace, Record().FailureCode);
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task PermissionDenied_ChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX permissions simulate a protected folder on the build host; Windows ACLs are covered by the manual checklist.
        }

        var (installer, request, _, _) = Prepare();
        File.SetUnixFileMode(_install, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        Assert.Equal(UpdateFailureCodes.NotWritable, Record().FailureCode);
        File.SetUnixFileMode(_install, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task StagedFileTamperedAfterVerification_IsRejected()
    {
        var (installer, request, staged, _) = Prepare();
        File.WriteAllText(Path.Combine(staged, "coreclr.dll"), "runtime 0.1.0-rc.X");

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        Assert.Equal(UpdateFailureCodes.StagedFilesInvalid, Record().FailureCode);
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task UnexpectedExtraFileInStaging_IsRejected()
    {
        var (installer, request, staged, _) = Prepare();
        File.WriteAllText(Path.Combine(staged, "payload.exe"), "evil");

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        Assert.Equal(UpdateFailureCodes.StagedFilesInvalid, Record().FailureCode);
        Assert.False(File.Exists(Path.Combine(_install, "payload.exe")));
    }

    [Fact]
    public async Task RequestOutsideTheRequestsFolder_IsIgnoredEntirely()
    {
        var (installer, request, _, _) = Prepare();
        var elsewhere = Path.Combine(_kit.Root, "request.json");
        File.Copy(request, elsewhere);

        Assert.Equal(UpdaterExitCode.InvalidUsage, await installer.RunAsync(elsewhere));
        Assert.Empty(Directory.Exists(_paths.Records) ? Directory.EnumerateFiles(_paths.Records) : []);
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task UpdaterRunningFromAnotherFolderThanTheStagedOne_RefusesTheRequest()
    {
        var (_, request, _, _) = Prepare();
        var installer = new UpdateInstaller(_paths, _environment, Path.Combine(_paths.Staging, "somewhere-else"), _database);

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        Assert.Equal(UpdateFailureCodes.InvalidRequest, Record().FailureCode);
        AssertOldVersionIntact();
    }

    [Theory]
    [InlineData("\"toVersion\": \"0.1.0-rc.8\"", "\"toVersion\": \"0.1.0-rc.6\"")]
    [InlineData("\"productId\": \"carowinds-operations\"", "\"productId\": \"other\"")]
    [InlineData("\"path\": \"coreclr.dll\"", "\"path\": \"../coreclr.dll\"")]
    public async Task DowngradeWrongProductOrUnsafePathInRequest_IsRefused(string from, string to)
    {
        var (installer, request, _, _) = Prepare();
        File.WriteAllText(request, File.ReadAllText(request).Replace(from, to, StringComparison.Ordinal));

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        Assert.Equal(UpdateFailureCodes.InvalidRequest, Record().FailureCode);
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task InstallDirectoryThatIsNotAnInstallation_IsRefused()
    {
        var (installer, request, _, _) = Prepare();
        var notAnInstall = Directory.CreateDirectory(Path.Combine(_kit.Root, "Documents")).FullName;
        var json = File.ReadAllText(request).Replace(JsonSerializer.Serialize(_install)[1..^1], JsonSerializer.Serialize(notAnInstall)[1..^1], StringComparison.Ordinal);
        File.WriteAllText(request, json);

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        Assert.Equal(UpdateFailureCodes.InvalidRequest, Record().FailureCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(notAnInstall));
    }

    [Fact]
    public async Task SecondConcurrentUpdater_IsRefused()
    {
        var (installer, request, _, _) = Prepare();
        Directory.CreateDirectory(_paths.Root);
        using (new FileStream(Path.Combine(_paths.Root, "updater.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));
        }

        Assert.Equal(UpdateFailureCodes.AnotherUpdateRunning, Record().FailureCode);
        AssertOldVersionIntact();
    }

    [Fact]
    public async Task ConfirmLaunch_OnlyForTheAwaitedVersionInTheSameFolder()
    {
        var (installer, request, _, _) = Prepare();
        Guid? requestId = null;
        _environment.OnStart = (_, call) =>
        {
            if (call == 1)
            {
                var record = Record();
                requestId = record.RequestId;
                Assert.Equal(UpdateInstallStatus.AwaitingLaunchConfirmation, record.Status);
                Assert.False(new UpdateInstallRecordStore(_paths, _kit.Root).ConfirmLaunch(record.RequestId, SemanticVersion.Parse(NewVersion)));
                Assert.False(new UpdateInstallRecordStore(_paths, _install).ConfirmLaunch(record.RequestId, SemanticVersion.Parse("0.1.0-rc.9")));
                Assert.True(new UpdateInstallRecordStore(_paths, _install).ConfirmLaunch(record.RequestId, SemanticVersion.Parse(NewVersion)));
            }

            return new FakeApp();
        };

        Assert.Equal(UpdaterExitCode.Succeeded, await installer.RunAsync(request));
        Assert.NotNull(requestId);
    }

    [Fact]
    public void Launcher_RefusesAnUpdaterOutsideStaging()
    {
        var launcher = new UpdaterProcessLauncher(_paths, _install, _ => throw new InvalidOperationException("must not start"));
        var manifest = _kit.BuildVerifiedRelease(NewVersion).Manifest;
        var offer = new UpdateOffer(new ReleaseCandidate($"v{NewVersion}", manifest.Version, null, null, false, []), manifest, 1);

        var result = launcher.Launch(new StagedUpdate(offer, _install), SemanticVersion.Parse(OldVersion));
        Assert.Equal(UpdateLaunchFailureKind.StagedUpdaterMissing, result.Failure);
    }

    [Fact]
    public void Launcher_UpdaterThatDoesNotStart_LeavesNoRequestBehind()
    {
        var (zip, manifest) = _kit.BuildVerifiedRelease(NewVersion);
        var staged = Path.Combine(_paths.Staging, "x");
        SafeZipExtractor.ExtractVerified(zip, manifest, staged);
        var offer = new UpdateOffer(new ReleaseCandidate($"v{NewVersion}", manifest.Version, null, null, false, []), manifest, 1);

        var result = new UpdaterProcessLauncher(_paths, _install, _ => false).Launch(new StagedUpdate(offer, staged), SemanticVersion.Parse(OldVersion));
        Assert.Equal(UpdateLaunchFailureKind.UpdaterDidNotStart, result.Failure);
        Assert.False(File.Exists(_paths.RequestFile(result.RequestId)));
    }

    [Fact]
    public void InstallationInspector_UsesRealChecks()
    {
        Assert.Equal(InstallationReadinessKind.Ready, new InstallationInspector(_install, isWindows: true).Inspect().Kind);
        Assert.Equal(InstallationReadinessKind.UnsupportedPlatform, new InstallationInspector(_install, isWindows: false).Inspect().Kind);

        var devBuild = Directory.CreateDirectory(Path.Combine(_kit.Root, "bin-debug")).FullName;
        File.WriteAllText(Path.Combine(devBuild, "Carowinds.Passdown.Desktop.exe"), "x");
        Assert.Equal(InstallationReadinessKind.NotPortableInstallation, new InstallationInspector(devBuild, isWindows: true).Inspect().Kind);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_install, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.Equal(InstallationReadinessKind.NotWritable, new InstallationInspector(_install, isWindows: true).Inspect().Kind);
            File.SetUnixFileMode(_install, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // The probe leaves nothing behind.
        Assert.DoesNotContain(Directory.EnumerateFiles(_install), f => Path.GetFileName(f).StartsWith(".carowinds-write-probe", StringComparison.Ordinal));
    }
}
