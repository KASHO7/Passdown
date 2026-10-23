using System.Text;
using System.Text.Json;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Tests.PersonalSync;
using Carowinds.Passdown.Infrastructure.Tests.Storage;
using Carowinds.Passdown.Infrastructure.Updates;
using Carowinds.Passdown.Infrastructure.Updates.Installation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Updates;

/// <summary>
/// Phase 27: an in-app update from RC.8 (database schema 16) to a version that upgrades the database (RC.9, schema
/// 17). The installed RC.8 app hands over to the updater shipped inside the *new* package, so this engine is what
/// runs for RC.8 → RC.9. Real SQLite profile in a temporary folder, synthetic signed packages, fake processes:
/// the "new version" upgrades the database with the real migrations, then confirms, crashes, hangs or is stopped.
/// </summary>
public sealed class UpdateInstallerDatabaseSafetyTests : IDisposable
{
    private const string OldVersion = "0.1.0-rc.8";
    private const string NewVersion = "0.1.0-rc.9";

    private readonly UpdateTestKit _kit = new();
    private readonly UpdatePaths _paths;
    private readonly string _install;
    private readonly string _profile;
    private readonly string _database;
    private readonly FakeEnvironment _environment;
    private string _rc8Dump = string.Empty;

    public UpdateInstallerDatabaseSafetyTests()
    {
        _paths = new UpdatePaths(Path.Combine(_kit.Root, "Carowinds Operations Updates"));
        _install = Path.Combine(_kit.Root, "Carowinds-Operations-portable");
        _profile = Path.Combine(_kit.Root, "Carowinds Passdown");
        _database = Path.Combine(_profile, "carowinds-passdown.db");
        Directory.CreateDirectory(_install);
        foreach (var (path, bytes) in UpdateTestKit.AppFiles(OldVersion))
        {
            var target = Path.Combine(_install, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }

        _environment = new FakeEnvironment(_paths);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _kit.Dispose();
    }

    private void CreateRc8Profile()
    {
        Rc8ProfileBuilder.Create(_profile);
        _rc8Dump = Rc8ProfileBuilder.Dump(_database);
    }

    /// <summary>What RC.9 does on its first start: the real migrations (with their own pre-migration backup).</summary>
    private void NewVersionUpgradesTheDatabase() =>
        new MigrationRunner(new SqliteConnectionFactory(_database), Phase26OfficialPriceHistorySyncTests.AllMigrations(), NullLogger<MigrationRunner>.Instance).Run();

    private (UpdateInstaller Installer, string RequestPath) Prepare(SqliteDatabaseBackup? backup = null)
    {
        var (zip, manifest) = _kit.BuildVerifiedRelease(NewVersion);
        var staged = Path.Combine(_paths.Staging, $"{NewVersion}-{Guid.NewGuid():N}");
        SafeZipExtractor.ExtractVerified(zip, manifest, staged);

        // The request is written exactly as the installed app writes it (format 1, unchanged since RC.8).
        var launcher = new UpdaterProcessLauncher(_paths, _install, _ => true);
        var offer = new UpdateOffer(new ReleaseCandidate($"v{NewVersion}", manifest.Version, null, null, false, []), manifest, manifest.Package.Size);
        var launch = launcher.Launch(new StagedUpdate(offer, staged), SemanticVersion.Parse(OldVersion));
        Assert.True(launch.Started);

        var guard = SqliteUpdateDatabaseGuard.ForDatabase(_database, backup);
        return (new UpdateInstaller(_paths, _environment, staged, guard), _paths.RequestFile(launch.RequestId));
    }

    private UpdateInstallRecord Record() => new UpdateInstallRecordStore(_paths, _install).LoadLatestRecord()!;

    private string Installed(string path) => File.ReadAllText(Path.Combine(_install, path.Replace('/', Path.DirectorySeparatorChar)));

    private string[] PreUpdateBackups() =>
        Directory.Exists(Path.Combine(_profile, "backups"))
            ? Directory.GetFiles(Path.Combine(_profile, "backups"), "*pre-update-*.db")
            : [];

    private void AssertVersionInstalled(string version)
    {
        foreach (var (path, bytes) in UpdateTestKit.AppFiles(version))
        {
            Assert.Equal(Encoding.UTF8.GetString(bytes), Installed(path));
        }
    }

    private void AssertPreUpdateBackupIsTheRc8Database()
    {
        var backup = Assert.Single(PreUpdateBackups());
        Assert.Equal(16, Rc8ProfileBuilder.SchemaVersion(backup));
        Assert.Equal(_rc8Dump, Rc8ProfileBuilder.Dump(backup));
        Assert.Equal(backup, Record().DatabaseBackupPath);
        Assert.Equal(16, Record().DatabaseSchemaVersionBefore);
    }

    [Fact]
    public async Task Rc8ToRc9_BacksUpBeforeReplacingFiles_ThenSucceedsOnlyAfterTheUpgradedVersionConfirms()
    {
        CreateRc8Profile();
        var (installer, request) = Prepare();
        string[] backupsWhenTheNewVersionStarted = [];
        _environment.OnStart = content =>
        {
            backupsWhenTheNewVersionStarted = PreUpdateBackups();
            NewVersionUpgradesTheDatabase();
            _environment.Confirm(NewVersion);
            return new FakeApp();
        };

        Assert.Equal(UpdaterExitCode.Succeeded, await installer.RunAsync(request));

        Assert.Single(backupsWhenTheNewVersionStarted); // the backup existed before the new version ever ran
        AssertVersionInstalled(NewVersion);
        Assert.Equal(UpdateInstallStatus.Succeeded, Record().Status);
        AssertPreUpdateBackupIsTheRc8Database();
        Assert.Equal(17, Rc8ProfileBuilder.SchemaVersion(_database));
        Assert.Equal(Rc8ProfileBuilder.Dump(_database, "schema_version", "synced_price_observations"), Rc8ProfileBuilder.Dump(PreUpdateBackups()[0], "schema_version"));
        Assert.Equal(Rc8ProfileBuilder.AppearanceJson, File.ReadAllText(Path.Combine(_profile, "appearance.json")));
        Assert.Equal(Rc8ProfileBuilder.SyncConfigJson, File.ReadAllText(Path.Combine(_profile, "sync-config.json")));
        // The app made its own pre-migration copy too; nothing deletes either one.
        Assert.Single(Directory.GetFiles(Path.Combine(_profile, "backups"), "*pre-migration-schema-16-to-17*.db"));
    }

    [Fact]
    public async Task NoDatabaseYet_InstallsNormally()
    {
        var (installer, request) = Prepare();

        Assert.Equal(UpdaterExitCode.Succeeded, await installer.RunAsync(request));
        Assert.Null(Record().DatabaseBackupPath);
        Assert.Empty(PreUpdateBackups());
    }

    [Fact]
    public async Task BackupWithoutEnoughDiskSpace_InstallsNothing_AndStartsNothing()
    {
        CreateRc8Profile();
        var (installer, request) = Prepare(new SqliteDatabaseBackup(_ => 4096));

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));

        Assert.Equal((UpdateInstallStatus.NotInstalled, UpdateFailureCodes.InsufficientDiskSpace), (Record().Status, Record().FailureCode));
        AssertVersionInstalled(OldVersion);
        Assert.Empty(_environment.Started);
        Assert.Equal(_rc8Dump, Rc8ProfileBuilder.Dump(_database));
        Assert.Equal(16, Rc8ProfileBuilder.SchemaVersion(_database));
    }

    [Fact]
    public async Task BackupThatCannotBeWritten_InstallsNothing()
    {
        CreateRc8Profile();
        File.WriteAllText(Path.Combine(_profile, "backups"), "not a folder");
        var (installer, request) = Prepare();

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));

        Assert.Equal(UpdateFailureCodes.DatabaseBackupFailed, Record().FailureCode);
        AssertVersionInstalled(OldVersion);
        Assert.Equal(_rc8Dump, Rc8ProfileBuilder.Dump(_database));
    }

    [Fact]
    public async Task DatabaseLockedByAnotherProgram_InstallsNothing()
    {
        CreateRc8Profile();
        var (installer, request) = Prepare();
        UpdaterExitCode exit;
        using (var holder = SqliteDatabaseBackup.Open(_database, SqliteOpenMode.ReadWrite))
        {
            using (var command = holder.CreateCommand())
            {
                command.CommandText = "BEGIN EXCLUSIVE;";
                command.ExecuteNonQuery();
            }

            exit = await installer.RunAsync(request);
        }

        Assert.Equal(UpdaterExitCode.NotInstalled, exit);
        Assert.Equal(UpdateFailureCodes.DatabaseInUse, Record().FailureCode);
        AssertVersionInstalled(OldVersion);
        Assert.Equal(_rc8Dump, Rc8ProfileBuilder.Dump(_database));
    }

    [Fact]
    public async Task DamagedDatabase_IsNotUpgraded_AndNothingIsInstalled()
    {
        Directory.CreateDirectory(_profile);
        File.WriteAllText(_database, "not an SQLite database — perhaps overwritten by another program");
        var (installer, request) = Prepare();

        Assert.Equal(UpdaterExitCode.NotInstalled, await installer.RunAsync(request));

        Assert.Equal(UpdateFailureCodes.DatabaseDamaged, Record().FailureCode);
        AssertVersionInstalled(OldVersion);
        Assert.Equal("not an SQLite database — perhaps overwritten by another program", File.ReadAllText(_database));
    }

    [Fact]
    public async Task NewVersionCrashesBeforeUpgradingTheDatabase_IsRolledBack_AndRc8StartsAgainOnce()
    {
        CreateRc8Profile();
        var (installer, request) = Prepare();
        _environment.OnStart = content => content.EndsWith(NewVersion, StringComparison.Ordinal) ? new FakeApp(exited: true) : new FakeApp();

        Assert.Equal(UpdaterExitCode.RolledBack, await installer.RunAsync(request));

        AssertVersionInstalled(OldVersion);
        Assert.Equal(UpdateInstallStatus.RolledBack, Record().Status);
        Assert.Equal([$"desktop-exe {NewVersion}", $"desktop-exe {OldVersion}"], _environment.Started);
        Assert.Equal(16, Rc8ProfileBuilder.SchemaVersion(_database));
        Assert.Equal(_rc8Dump, Rc8ProfileBuilder.Dump(_database));
        AssertPreUpdateBackupIsTheRc8Database();
    }

    [Fact]
    public async Task NewVersionStoppedDuringTheUpgrade_IsRolledBack_BecauseTheUncommittedUpgradeIsUndone()
    {
        CreateRc8Profile();
        var (installer, request) = Prepare();
        SqliteConnection? migrating = null;
        _environment.OnStart = content =>
        {
            if (!content.EndsWith(NewVersion, StringComparison.Ordinal))
            {
                return new FakeApp();
            }

            // The upgrade is half-way through its transaction when the updater's confirmation timeout stops it.
            migrating = SqliteDatabaseBackup.Open(_database, SqliteOpenMode.ReadWrite);
            using var command = migrating.CreateCommand();
            command.CommandText = "BEGIN IMMEDIATE; PRAGMA user_version = 17; CREATE TABLE synced_price_observations (observation_id TEXT);";
            command.ExecuteNonQuery();
            return new FakeApp(onKill: () => migrating.Dispose());
        };

        Assert.Equal(UpdaterExitCode.RolledBack, await installer.RunAsync(request));

        AssertVersionInstalled(OldVersion);
        Assert.Equal(16, Rc8ProfileBuilder.SchemaVersion(_database));
        Assert.Equal(_rc8Dump, Rc8ProfileBuilder.Dump(_database));
        Assert.Equal(2, _environment.Started.Count);
    }

    [Theory]
    [InlineData(true)] // the upgraded version crashed after upgrading the database
    [InlineData(false)] // it upgraded the database, then hung and never confirmed
    public async Task NewVersionFailsAfterUpgradingTheDatabase_IsNotReversed_AndEverythingIsKept(bool crashes)
    {
        CreateRc8Profile();
        var (installer, request) = Prepare();
        _environment.OnStart = content =>
        {
            NewVersionUpgradesTheDatabase();
            return new FakeApp(exited: crashes);
        };

        Assert.Equal(UpdaterExitCode.RecoveryRequired, await installer.RunAsync(request));

        var record = Record();
        Assert.Equal((UpdateInstallStatus.RecoveryRequired, UpdateFailureCodes.DatabaseUpgradedNotConfirmed), (record.Status, record.FailureCode));
        // RC.8 is never started against a schema-17 database, and nothing is reversed.
        Assert.Equal([$"desktop-exe {NewVersion}"], _environment.Started);
        AssertVersionInstalled(NewVersion);
        Assert.Equal(17, Rc8ProfileBuilder.SchemaVersion(_database));
        // The upgraded database is left exactly as the new version left it; the pre-install copy is intact.
        Assert.Equal(Rc8ProfileBuilder.Dump(PreUpdateBackups()[0], "schema_version"), Rc8ProfileBuilder.Dump(_database, "schema_version", "synced_price_observations"));
        AssertPreUpdateBackupIsTheRc8Database();
        // The previous program files are kept for a manual return to RC.8, and the note explains it.
        Assert.NotNull(record.BackupDirectory);
        Assert.Equal($"desktop-exe {OldVersion}", File.ReadAllText(Path.Combine(record.BackupDirectory!, "Carowinds.Passdown.Desktop.exe")));
        var note = File.ReadAllText(Path.Combine(_install, UpdateInstaller.RecoveryNoteName));
        Assert.Contains($"do NOT start version {OldVersion} against your current database", note, StringComparison.Ordinal);
        Assert.Contains(PreUpdateBackups()[0], note, StringComparison.Ordinal);
        Assert.Contains(_database, note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpgradedDatabaseWithEditsAfterTheUpgrade_IsNeverOverwrittenByTheBackup()
    {
        CreateRc8Profile();
        var (installer, request) = Prepare();
        _environment.OnStart = _ =>
        {
            NewVersionUpgradesTheDatabase();
            using var connection = SqliteDatabaseBackup.Open(_database, SqliteOpenMode.ReadWrite);
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc) VALUES ('2026-10-23', 4, '{\"note\":\"typed after the upgrade\"}', '2026-10-23T09:00:00Z');";
            command.ExecuteNonQuery();
            return new FakeApp(exited: true);
        };

        Assert.Equal(UpdaterExitCode.RecoveryRequired, await installer.RunAsync(request));

        Assert.Contains("typed after the upgrade", Rc8ProfileBuilder.Dump(_database), StringComparison.Ordinal);
        Assert.DoesNotContain("typed after the upgrade", Rc8ProfileBuilder.Dump(PreUpdateBackups()[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DatabaseStateUnreadableAfterAFailedStart_IsTreatedAsUpgraded_AndNothingIsReversed()
    {
        CreateRc8Profile();
        var (installer, request) = Prepare();
        SqliteConnection? holder = null;
        _environment.OnStart = _ =>
        {
            holder = SqliteDatabaseBackup.Open(_database, SqliteOpenMode.ReadWrite);
            using var command = holder.CreateCommand();
            command.CommandText = "BEGIN EXCLUSIVE;";
            command.ExecuteNonQuery();
            return new FakeApp(exited: true); // something else keeps the database locked after the crash
        };

        try
        {
            Assert.Equal(UpdaterExitCode.RecoveryRequired, await installer.RunAsync(request));
        }
        finally
        {
            holder?.Dispose();
        }

        Assert.Equal(UpdateFailureCodes.DatabaseUpgradedNotConfirmed, Record().FailureCode);
        Assert.Single(_environment.Started);
        Assert.Equal(16, Rc8ProfileBuilder.SchemaVersion(_database));
    }

    [Fact]
    public async Task UpdaterRecord_StaysReadableByTheRc8RecordShape()
    {
        CreateRc8Profile();
        var (installer, request) = Prepare();
        _environment.OnStart = content => content.EndsWith(NewVersion, StringComparison.Ordinal) ? new FakeApp(exited: true) : new FakeApp();
        await installer.RunAsync(request);

        // RC.8 reads records with the same options and does not know the Phase 27 fields: they are skipped.
        var json = File.ReadAllBytes(Directory.GetFiles(_paths.Records, "install-*.json").Single());
        var asRc8 = JsonSerializer.Deserialize<Rc8InstallRecord>(json, UpdateJson.Options);
        Assert.NotNull(asRc8);
        Assert.Equal((UpdateInstallStatus.RolledBack, OldVersion, NewVersion), (asRc8.Status, asRc8.FromVersion, asRc8.ToVersion));
    }

    [Fact]
    public void UpdaterGuardsTheSameDatabaseFileTheAppUses()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "src", "Carowinds.Passdown.Desktop", "appsettings.json")));
        Assert.Equal(SqliteUpdateDatabaseGuard.DatabaseFileName, settings.RootElement.GetProperty("Storage").GetProperty("DatabaseFileName").GetString());
        Assert.Equal(SqliteUpdateDatabaseGuard.DatabaseFileName, new StorageOptions().DatabaseFileName);
        Assert.Equal(Path.Combine(AppPaths.LocalApplicationDataDirectory, SqliteUpdateDatabaseGuard.DatabaseFileName), SqliteUpdateDatabaseGuard.Default.DatabasePath);
    }

    /// <summary>The record exactly as RC.8 (commit a651e26) declares it.</summary>
    private sealed record Rc8InstallRecord(
        int FormatVersion, Guid RequestId, string FromVersion, string ToVersion, string InstallDirectory, string StagedDirectory,
        UpdateInstallStatus Status, string? FailureCode, DateTimeOffset UpdatedUtc, string? BackupDirectory, IReadOnlyList<UpdateSwapEntry> Journal);

    private sealed class FakeEnvironment(UpdatePaths paths) : IUpdaterEnvironment
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

        public Func<string, FakeApp>? OnStart { get; set; }

        public List<string> Started { get; } = [];

        public Task<bool> WaitForExitAsync(int processId, DateTimeOffset? startUtc, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);

        public int CountInstancesRunningFrom(string installDirectory) => 0;

        public IStartedApp? StartApp(string executablePath, string workingDirectory)
        {
            var content = File.ReadAllText(executablePath);
            Started.Add(content);
            if (OnStart is not null)
            {
                return OnStart(content);
            }

            if (content.EndsWith(NewVersion, StringComparison.Ordinal))
            {
                Confirm(NewVersion);
            }

            return new FakeApp();
        }

        public void Confirm(string version)
        {
            var record = Directory.EnumerateFiles(paths.Records, "install-*.json").Single();
            var id = Guid.Parse(Path.GetFileNameWithoutExtension(record)["install-".Length..]);
            File.WriteAllText(paths.ConfirmationFile(id), JsonSerializer.Serialize(new { requestId = id, version, processId = 1 }));
        }

        public long? AvailableFreeSpace(string path) => null;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            UtcNow += delay;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeApp(bool exited = false, Action? onKill = null) : IStartedApp
    {
        public bool HasExited { get; private set; } = exited;

        public void Kill()
        {
            onKill?.Invoke();
            HasExited = true;
        }

        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);

        public void Dispose()
        {
        }
    }
}
