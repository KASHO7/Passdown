using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Tests.PersonalSync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Phase 27: upgrading an RC.8 profile (schema 16) to RC.9 (schema 17) first writes a consistent, verified backup,
/// keeps every existing row, and leaves the database untouched whenever the backup or the migration fails.
/// Synthetic profiles in temporary folders only.
/// </summary>
public sealed class Phase27MigrationBackupTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "carowinds-phase27-migration", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Profile(string name = "rc8-profile") => Path.Combine(_workspace, name);

    private static string Database(string profile) => Path.Combine(profile, "carowinds-passdown.db");

    private static string[] Backups(string profile)
    {
        var directory = Path.Combine(profile, SqliteDatabaseBackup.BackupDirectoryName);
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.db*").Order().ToArray() : [];
    }

    private static MigrationRunner Runner(SqliteConnectionFactory factory, SqliteDatabaseBackup? backup = null, IReadOnlyList<IMigration>? migrations = null) =>
        new(factory, migrations ?? Phase26OfficialPriceHistorySyncTests.AllMigrations(), NullLogger<MigrationRunner>.Instance, backup);

    [Fact]
    public void Rc8Profile_UpgradeTo17_WritesAVerifiedBackupFirst_AndPreservesEveryRowAndFile()
    {
        var profile = Profile();
        var factory = Rc8ProfileBuilder.Create(profile);
        var before = Rc8ProfileBuilder.Dump(Database(profile));

        var runner = Runner(factory);
        Assert.Equal(17, runner.Run());

        var backup = Assert.Single(Backups(profile));
        Assert.Equal(backup, runner.LastPreMigrationBackupPath);
        Assert.Contains("pre-migration-schema-16-to-17", Path.GetFileName(backup), StringComparison.Ordinal);
        Assert.EndsWith(".db", backup, StringComparison.Ordinal);

        // The backup is the complete RC.8 database, still at schema 16 — openable by RC.8.
        Assert.Equal(16, Rc8ProfileBuilder.SchemaVersion(backup));
        Assert.Equal(before, Rc8ProfileBuilder.Dump(backup));
        using (var connection = SqliteDatabaseBackup.Open(backup, SqliteOpenMode.ReadOnly))
        {
            Assert.True(SqliteDatabaseBackup.IntegrityOk(connection));
        }

        // The live database gained only the new table and its schema_version row; nothing existing changed.
        Assert.Equal(17, Rc8ProfileBuilder.SchemaVersion(Database(profile)));
        Assert.Equal(
            Rc8ProfileBuilder.Dump(Database(profile), "schema_version", "synced_price_observations"),
            Rc8ProfileBuilder.Dump(backup, "schema_version"));
        Assert.Contains("Unsent RC.8 draft text", before, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexString(Rc8ProfileBuilder.HistoryPdf), before, StringComparison.Ordinal);
        Assert.Equal(Rc8ProfileBuilder.AppearanceJson, File.ReadAllText(Path.Combine(profile, "appearance.json")));
        Assert.Equal(Rc8ProfileBuilder.SyncConfigJson, File.ReadAllText(Path.Combine(profile, "sync-config.json")));
        Assert.True(File.Exists(Path.Combine(profile, SqliteDatabaseBackup.BackupDirectoryName, SqliteDatabaseBackup.ReadMeFileName)));
    }

    [Fact]
    public void SecondStart_IsIdempotent_AndMakesNoFurtherBackup()
    {
        var profile = Profile();
        var factory = Rc8ProfileBuilder.Create(profile);
        Assert.Equal(17, Runner(factory).Run());
        var migrated = Rc8ProfileBuilder.Dump(Database(profile));

        Assert.Equal(17, Runner(factory).Run());
        Assert.Equal(17, Runner(factory).Run());

        Assert.Single(Backups(profile));
        Assert.Equal(migrated, Rc8ProfileBuilder.Dump(Database(profile)));
    }

    [Fact]
    public void BrandNewProfile_IsCreatedAt17_WithoutABackup()
    {
        var profile = Profile("fresh");
        var runner = Runner(new SqliteConnectionFactory(Database(profile)));

        Assert.Equal(17, runner.Run());
        Assert.Empty(Backups(profile));
        Assert.Null(runner.LastPreMigrationBackupPath);
    }

    [Fact]
    public void InsufficientDiskSpaceForTheBackup_StopsBeforeMigrating_AndChangesNothing()
    {
        var profile = Profile();
        var factory = Rc8ProfileBuilder.Create(profile);
        var before = Rc8ProfileBuilder.Dump(Database(profile));

        var error = Assert.Throws<PreMigrationBackupException>(() => Runner(factory, new SqliteDatabaseBackup(_ => 1024)).Run());

        Assert.Equal(DatabaseBackupStatus.InsufficientDiskSpace, error.Status);
        Assert.Equal((16, 17), (error.FromVersion, error.ToVersion));
        Assert.Equal(16, Rc8ProfileBuilder.SchemaVersion(Database(profile)));
        Assert.Equal(before, Rc8ProfileBuilder.Dump(Database(profile)));
        Assert.Empty(Backups(profile));
    }

    [Fact]
    public void BackupThatCannotBeWritten_StopsBeforeMigrating_AndChangesNothing()
    {
        var profile = Profile();
        var factory = Rc8ProfileBuilder.Create(profile);
        var before = Rc8ProfileBuilder.Dump(Database(profile));
        File.WriteAllText(Path.Combine(profile, SqliteDatabaseBackup.BackupDirectoryName), "a file where the backups folder should be");

        var error = Assert.Throws<PreMigrationBackupException>(() => Runner(factory).Run());

        Assert.Equal(DatabaseBackupStatus.Failed, error.Status);
        Assert.Equal(16, Rc8ProfileBuilder.SchemaVersion(Database(profile)));
        Assert.Equal(before, Rc8ProfileBuilder.Dump(Database(profile)));
    }

    [Fact]
    public void FailedMigrationTransaction_RollsBackToSchema16_AndKeepsTheVerifiedBackup()
    {
        var profile = Profile();
        var factory = Rc8ProfileBuilder.Create(profile);
        var before = Rc8ProfileBuilder.Dump(Database(profile));
        var migrations = Phase26OfficialPriceHistorySyncTests.AllMigrations().Take(16).Append(new FailingMigration()).ToArray();

        Assert.Throws<InvalidOperationException>(() => Runner(factory, migrations: migrations).Run());

        Assert.Equal(16, Rc8ProfileBuilder.SchemaVersion(Database(profile)));
        Assert.Equal(before, Rc8ProfileBuilder.Dump(Database(profile))); // the half-made table is gone
        var backup = Assert.Single(Backups(profile));
        Assert.Equal(before, Rc8ProfileBuilder.Dump(backup));

        // A later start with a working build migrates normally (and backs up again; the first copy is kept).
        Assert.Equal(17, Runner(factory).Run());
        Assert.Equal(2, Backups(profile).Length);
    }

    [Fact]
    public void Rc8Build_RefusesTheUpgradedDatabase_ButOpensTheBackup()
    {
        var profile = Profile();
        var factory = Rc8ProfileBuilder.Create(profile);
        var runner = Runner(factory);
        runner.Run();
        var rc8Migrations = Phase26OfficialPriceHistorySyncTests.AllMigrations().Take(16).ToArray();

        var refused = Assert.Throws<UnsupportedSchemaVersionException>(() => Runner(factory, migrations: rc8Migrations).Run());
        Assert.Equal((17, 16), (refused.DatabaseVersion, refused.HighestKnownVersion));

        var restoredCopy = Path.Combine(_workspace, "restored", "carowinds-passdown.db");
        Directory.CreateDirectory(Path.GetDirectoryName(restoredCopy)!);
        File.Copy(runner.LastPreMigrationBackupPath!, restoredCopy);
        Assert.Equal(16, Runner(new SqliteConnectionFactory(restoredCopy), migrations: rc8Migrations).Run());
    }

    [Fact]
    public void Backup_IncludesWritesStillInTheWalFile()
    {
        var path = Database(Profile("wal"));
        var factory = new SqliteConnectionFactory(path);
        Runner(factory, migrations: Phase26OfficialPriceHistorySyncTests.AllMigrations().Take(16).ToArray()).Run();
        using var writer = SqliteDatabaseBackup.Open(path, SqliteOpenMode.ReadWrite);
        using (var command = writer.CreateCommand())
        {
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;
                PRAGMA wal_autocheckpoint = 0;
                INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc) VALUES ('2026-10-22', 4, '{"wal":true}', '2026-10-22T12:00:00Z');
                """;
            command.ExecuteNonQuery();
        }

        Assert.True(new FileInfo(path + "-wal").Length > 0, "the write should still be in the WAL file");

        var result = new SqliteDatabaseBackup().Create(path, Path.Combine(_workspace, "wal-backups"), "wal-test");

        Assert.Equal(DatabaseBackupStatus.Created, result.Status);
        Assert.Contains("\"wal\":true", Rc8ProfileBuilder.Dump(result.BackupPath!), StringComparison.Ordinal);
        Assert.False(File.Exists(result.BackupPath + "-wal"));
    }

    [Fact]
    public void DamagedDatabase_IsReportedAsDamaged_AndNoBackupIsKept()
    {
        var profile = Profile();
        Rc8ProfileBuilder.Create(profile);
        var path = Database(profile);
        using (var connection = SqliteDatabaseBackup.Open(path, SqliteOpenMode.ReadWrite))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE filler (x TEXT); WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 2000) INSERT INTO filler SELECT printf('%0500d', i) FROM n;";
            command.ExecuteNonQuery();
        }

        // Overwrite the middle of the file with garbage, as a failing disk might.
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = stream.Length / 2;
            stream.Write(Enumerable.Repeat((byte)0xA5, 8192).ToArray());
        }

        var result = new SqliteDatabaseBackup().Create(path, Path.Combine(profile, "backups"), "damaged");

        Assert.Equal(DatabaseBackupStatus.DatabaseDamaged, result.Status);
        Assert.Empty(Backups(profile));
    }

    [Fact]
    public void FileThatIsNotADatabase_IsReportedAsDamaged()
    {
        var path = Path.Combine(_workspace, "not-a-db", "carowinds-passdown.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "this is not an SQLite database, it is just text that happens to be long enough to have a header");

        var result = new SqliteDatabaseBackup().Create(path, Path.Combine(_workspace, "not-a-db", "backups"), "x");

        Assert.Equal(DatabaseBackupStatus.DatabaseDamaged, result.Status);
        Assert.Empty(Directory.GetFiles(Path.Combine(_workspace, "not-a-db", "backups")));
    }

    [Fact]
    public void LockedDatabase_IsReportedAsInUse_AfterTheBusyTimeout()
    {
        var profile = Profile();
        Rc8ProfileBuilder.Create(profile);
        using var holder = SqliteDatabaseBackup.Open(Database(profile), SqliteOpenMode.ReadWrite);
        using (var command = holder.CreateCommand())
        {
            command.CommandText = "BEGIN EXCLUSIVE;";
            command.ExecuteNonQuery();
        }

        var result = new SqliteDatabaseBackup().Create(Database(profile), Path.Combine(profile, "backups"), "locked");

        Assert.Equal(DatabaseBackupStatus.DatabaseInUse, result.Status);
        Assert.Empty(Backups(profile));
    }

    [Fact]
    public void Backups_NeverOverwriteEachOther_OrAnExistingReadMe()
    {
        var profile = Profile();
        Rc8ProfileBuilder.Create(profile);
        var directory = Path.Combine(profile, "backups");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, SqliteDatabaseBackup.ReadMeFileName), "my own notes");
        File.WriteAllText(Path.Combine(directory, "my-manual-backup.db"), "user's own copy");

        var first = new SqliteDatabaseBackup().Create(Database(profile), directory, "same-label");
        var second = new SqliteDatabaseBackup().Create(Database(profile), directory, "same-label");

        Assert.NotEqual(first.BackupPath, second.BackupPath);
        Assert.True(File.Exists(first.BackupPath) && File.Exists(second.BackupPath));
        Assert.Equal("my own notes", File.ReadAllText(Path.Combine(directory, SqliteDatabaseBackup.ReadMeFileName)));
        Assert.Equal("user's own copy", File.ReadAllText(Path.Combine(directory, "my-manual-backup.db")));
        Assert.Empty(Directory.GetFiles(directory, "*.partial"));
    }

    private sealed class FailingMigration : IMigration
    {
        public int Version => 17;

        public string Description => "Fails half-way";

        public void Apply(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE half_made (x TEXT);";
            command.ExecuteNonQuery();
            throw new InvalidOperationException("Simulated migration failure.");
        }
    }
}
