using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Tests.PersonalSync;
using Carowinds.Passdown.Infrastructure.Updates.Installation;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// RC.11 release gate: an RC.10 profile (database schema 17, with drafts, notes, season price history, synced
/// price observations and a History PDF) upgraded to RC.11 (schema 18). The updater's pre-update backup and the
/// runner's pre-migration backup are both written and verified first; every existing row survives; the result
/// passes SQLite's integrity check; RC.10 refuses the upgraded database instead of silently using it; a failed
/// upgrade rolls back to 17. Synthetic profiles in temporary folders only — never a real database.
/// </summary>
public sealed class Rc11MigrationSafetyTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "carowinds-rc11-migration", Guid.NewGuid().ToString("N"));

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

    /// <summary>RC.9/RC.10 migrations (schema 17).</summary>
    private static IReadOnlyList<IMigration> Rc10Migrations() => Phase26OfficialPriceHistorySyncTests.AllMigrations();

    /// <summary>RC.11 migrations — the same list the Desktop app registers (schema 18).</summary>
    private static IReadOnlyList<IMigration> Rc11Migrations() => [.. Rc10Migrations(), new Migration0018CreateOfficialReferenceDocumentsTable()];

    private static MigrationRunner Runner(SqliteConnectionFactory factory, IReadOnlyList<IMigration> migrations) =>
        new(factory, migrations, NullLogger<MigrationRunner>.Instance);

    private static string Database(string profile) => Path.Combine(profile, "carowinds-passdown.db");

    private static string[] Backups(string profile, string contains)
    {
        var directory = Path.Combine(profile, SqliteDatabaseBackup.BackupDirectoryName);
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.db").Where(file => Path.GetFileName(file).Contains(contains, StringComparison.Ordinal)).Order().ToArray()
            : [];
    }

    private static bool IntegrityOk(string path)
    {
        using var connection = SqliteDatabaseBackup.Open(path, SqliteOpenMode.ReadOnly);
        return SqliteDatabaseBackup.IntegrityOk(connection);
    }

    /// <summary>An RC.10-shaped profile: the RC.8 fixture rows, upgraded to 17, plus a synced price observation.</summary>
    private (string Profile, SqliteConnectionFactory Factory) CreateRc10Profile()
    {
        var profile = Path.Combine(_workspace, "rc10-profile");
        var factory = Rc8ProfileBuilder.Create(profile);
        Assert.Equal(17, Runner(factory, Rc10Migrations()).Run());
        using (var connection = factory.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO synced_price_observations (observation_id, contributor_device_id, contributor_sequence, product_key, amount, price_kind, display_price, price_qualifier, display_name, source_name, first_observed_at_utc, last_confirmed_at_utc, origin, currency, billing_unit, imported_at_utc)
                VALUES ('obs-rc10-1', 'device-b', 1, 'season-pass:gold:2027', '104.00', 'StandardCatalog', '$104.00', NULL, 'Gold Season Pass — 2027', 'Carowinds Memberships & Season Passes', '2026-10-20T12:00:00.0000000+00:00', '2026-10-22T12:00:00.0000000+00:00', 'Observed', 'USD', 'OneTime', '2026-10-22T12:05:00.0000000+00:00');
                INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc)
                VALUES ('2026-11-20', 4, '{"SectionSelection":{"IncludeOperationalNotes":false},"note":"RC.10 draft"}', '2026-11-20T18:00:00.0000000+00:00');
                """;
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        return (profile, factory);
    }

    [Fact]
    public void Rc10ToRc11_UpdaterBacksUpFirst_ThenMigrationBacksUp_AndEveryRowSurvivesAt18()
    {
        var (profile, factory) = CreateRc10Profile();
        var before = Rc8ProfileBuilder.Dump(Database(profile));

        // 1. The external updater, before it replaces any program file.
        var preUpdate = SqliteUpdateDatabaseGuard.ForDatabase(Database(profile)).BackUpBeforeInstall("0.1.0-rc.10", "0.1.0-rc.11");
        Assert.Equal(DatabaseBackupStatus.Created, preUpdate.Status);
        Assert.Contains("pre-update-0.1.0-rc.10-to-0.1.0-rc.11", Path.GetFileName(preUpdate.BackupPath), StringComparison.Ordinal);
        Assert.Equal(17, Rc8ProfileBuilder.SchemaVersion(preUpdate.BackupPath!));
        Assert.Equal(before, Rc8ProfileBuilder.Dump(preUpdate.BackupPath!));
        Assert.True(IntegrityOk(preUpdate.BackupPath!));

        // 2. RC.11's first start.
        var runner = Runner(factory, Rc11Migrations());
        Assert.Equal(18, runner.Run());

        var preMigration = Assert.Single(Backups(profile, "pre-migration-schema-17-to-18"));
        Assert.Equal(preMigration, runner.LastPreMigrationBackupPath);
        Assert.Equal(17, Rc8ProfileBuilder.SchemaVersion(preMigration));
        Assert.Equal(before, Rc8ProfileBuilder.Dump(preMigration));
        Assert.True(IntegrityOk(preMigration));

        // 3. The upgraded database: integrity OK, only the new table and its log row added.
        Assert.Equal(18, Rc8ProfileBuilder.SchemaVersion(Database(profile)));
        Assert.True(IntegrityOk(Database(profile)));
        var after = Rc8ProfileBuilder.Dump(Database(profile), "official_reference_documents");
        var newLogRows = after.Split('\n').Where(line => line.StartsWith("18|", StringComparison.Ordinal)).ToList();
        Assert.Single(newLogRows);
        Assert.Equal(before, string.Join('\n', after.Split('\n').Where(line => !newLogRows.Contains(line))));

        foreach (var expected in new[] { "Unsent RC.8 draft text", "RC.10 draft", "Gate 3 closed", "$104.00", "obs-rc10-1", Convert.ToHexString(Rc8ProfileBuilder.HistoryPdf) })
        {
            Assert.Contains(expected, after, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SecondRc11Start_IsIdempotent_AndMakesNoFurtherBackup()
    {
        var (profile, factory) = CreateRc10Profile();
        Assert.Equal(18, Runner(factory, Rc11Migrations()).Run());
        var migrated = Rc8ProfileBuilder.Dump(Database(profile));

        Assert.Equal(18, Runner(factory, Rc11Migrations()).Run());

        Assert.Single(Backups(profile, "pre-migration-schema-17-to-18"));
        Assert.Equal(migrated, Rc8ProfileBuilder.Dump(Database(profile)));
    }

    [Fact]
    public void Rc10Build_RefusesTheUpgradedDatabase_NeverSilentlyDowngrades_ButOpensTheBackup()
    {
        var (profile, factory) = CreateRc10Profile();
        var runner = Runner(factory, Rc11Migrations());
        runner.Run();
        var upgraded = Rc8ProfileBuilder.Dump(Database(profile));

        var refused = Assert.Throws<UnsupportedSchemaVersionException>(() => Runner(factory, Rc10Migrations()).Run());
        Assert.Equal((18, 17), (refused.DatabaseVersion, refused.HighestKnownVersion));
        Assert.Equal(18, Rc8ProfileBuilder.SchemaVersion(Database(profile)));
        Assert.Equal(upgraded, Rc8ProfileBuilder.Dump(Database(profile))); // untouched by the refusal

        var restored = Path.Combine(_workspace, "restored", "carowinds-passdown.db");
        Directory.CreateDirectory(Path.GetDirectoryName(restored)!);
        File.Copy(runner.LastPreMigrationBackupPath!, restored);
        Assert.Equal(17, Runner(new SqliteConnectionFactory(restored), Rc10Migrations()).Run());
    }

    [Fact]
    public void FailedUpgradeTo18_RollsBackTo17_KeepsTheBackup_AndALaterStartSucceeds()
    {
        var (profile, factory) = CreateRc10Profile();
        var before = Rc8ProfileBuilder.Dump(Database(profile));

        Assert.Throws<InvalidOperationException>(() => Runner(factory, [.. Rc10Migrations(), new FailingMigration18()]).Run());

        Assert.Equal(17, Rc8ProfileBuilder.SchemaVersion(Database(profile)));
        Assert.Equal(before, Rc8ProfileBuilder.Dump(Database(profile)));
        Assert.True(IntegrityOk(Database(profile)));
        var backup = Assert.Single(Backups(profile, "pre-migration-schema-17-to-18"));
        Assert.Equal(before, Rc8ProfileBuilder.Dump(backup));

        Assert.Equal(18, Runner(factory, Rc11Migrations()).Run());
        Assert.Equal(2, Backups(profile, "pre-migration-schema-17-to-18").Length);
    }

    [Fact]
    public void InsufficientSpaceForThePreMigrationBackup_StopsAt17_AndChangesNothing()
    {
        var (profile, factory) = CreateRc10Profile();
        var before = Rc8ProfileBuilder.Dump(Database(profile));

        var error = Assert.Throws<PreMigrationBackupException>(
            () => new MigrationRunner(factory, Rc11Migrations(), NullLogger<MigrationRunner>.Instance, new SqliteDatabaseBackup(_ => 1024)).Run());

        Assert.Equal((17, 18), (error.FromVersion, error.ToVersion));
        Assert.Equal(17, Rc8ProfileBuilder.SchemaVersion(Database(profile)));
        Assert.Equal(before, Rc8ProfileBuilder.Dump(Database(profile)));
        Assert.Empty(Backups(profile, "pre-migration-schema-17-to-18"));
    }

    [Fact]
    public void DesktopApp_RegistersExactlyTheRc11MigrationList()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Carowinds.Passdown.Desktop", "App.axaml.cs"));
        foreach (var migration in Rc11Migrations())
        {
            Assert.Contains($"new {migration.GetType().Name}()", app, StringComparison.Ordinal);
        }

        Assert.Equal(18, Rc11Migrations().Max(migration => migration.Version));
        Assert.DoesNotContain("Migration0019", app, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FailingMigration18 : IMigration
    {
        public int Version => 18;

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
