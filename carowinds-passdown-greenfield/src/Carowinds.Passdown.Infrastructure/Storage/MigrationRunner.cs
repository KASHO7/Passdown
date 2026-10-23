using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Storage;

/// <summary>
/// Applies pending <see cref="IMigration"/>s to the local SQLite database,
/// in ascending version order, gated by <c>PRAGMA user_version</c> so an
/// already-applied migration is never re-run.
/// <para>
/// Phase 27: before the first pending migration changes an existing database, a verified backup is written to
/// <see cref="BackupDirectory"/> (<see cref="SqliteDatabaseBackup"/>) while this connection already holds the
/// write lock, so the backup is exactly the state the migration starts from. No backup, no migration: the
/// database is left as it was and startup fails. After migrations ran, <c>PRAGMA integrity_check</c> must pass
/// before anything else (the window, the update confirmation) can use the database.
/// </para>
/// </summary>
public sealed class MigrationRunner(
    SqliteConnectionFactory connectionFactory,
    IReadOnlyList<IMigration> migrations,
    ILogger<MigrationRunner> logger,
    SqliteDatabaseBackup? backup = null)
{
    private readonly SqliteDatabaseBackup _backup = backup ?? new SqliteDatabaseBackup();

    /// <summary>Where pre-migration backups go: <c>backups</c> beside the database.</summary>
    public string BackupDirectory => SqliteDatabaseBackup.DefaultDirectoryFor(connectionFactory.DatabasePath);

    /// <summary>The verified backup this run made before migrating, if it had to make one.</summary>
    public string? LastPreMigrationBackupPath { get; private set; }

    public int Run()
    {
        using var connection = connectionFactory.OpenConnection();
        var applied = 0;

        var currentVersion = ReadUserVersion(connection);

        // Phase 7D Part G: a database reporting a version newer than any migration this build
        // knows about (e.g. an older portable build re-run against data a newer build already
        // migrated) must never be silently reinterpreted or downgraded — fail before touching
        // anything else. highestKnownVersion is 0 (never newer than any real database) if this
        // build somehow has no migrations registered at all.
        var highestKnownVersion = migrations.Count == 0 ? 0 : migrations.Max(m => m.Version);
        if (currentVersion > highestKnownVersion)
        {
            throw new UnsupportedSchemaVersionException(currentVersion, highestKnownVersion);
        }

        foreach (var migration in migrations.OrderBy(m => m.Version))
        {
            if (migration.Version <= currentVersion)
            {
                continue;
            }

            using (var transaction = connection.BeginTransaction())
            {
                // Phase 18A REL-001: the interactive app and a --scheduled-refresh process can both
                // start against the same database right after an upgrade. BeginTransaction takes the
                // write lock (BEGIN IMMEDIATE), so re-reading the version here — not only the
                // pre-transaction read above — is what guarantees a migration the other process
                // committed while this one waited is never re-applied (a non-idempotent step such as
                // Migration0009's ALTER TABLE ADD COLUMN would otherwise fail and abort startup).
                var committedVersion = ReadUserVersion(connection);
                if (committedVersion >= migration.Version)
                {
                    transaction.Commit();
                    currentVersion = committedVersion;
                    continue;
                }

                if (applied == 0 && committedVersion > 0)
                {
                    BackUpBeforeMigrating(committedVersion, highestKnownVersion);
                }

                migration.Apply(connection);
                RecordHistory(connection, migration);
                WriteUserVersion(connection, migration.Version);
                transaction.Commit();
            }

            logger.LogInformation(
                "Applied migration {Version}: {Description}",
                migration.Version,
                migration.Description);

            currentVersion = migration.Version;
            applied++;
        }

        if (applied > 0 && !SqliteDatabaseBackup.IntegrityOk(connection))
        {
            throw new DatabaseIntegrityException(currentVersion);
        }

        return currentVersion;
    }

    private void BackUpBeforeMigrating(int fromVersion, int toVersion)
    {
        var result = _backup.Create(connectionFactory.DatabasePath, BackupDirectory, $"pre-migration-schema-{fromVersion}-to-{toVersion}");
        if (result.Status != DatabaseBackupStatus.Created)
        {
            throw new PreMigrationBackupException(fromVersion, toVersion, result.Status);
        }

        LastPreMigrationBackupPath = result.BackupPath;
        logger.LogInformation(
            "Verified pre-migration backup of schema {FromVersion} written before migrating to {ToVersion}: {BackupFile}",
            fromVersion,
            toVersion,
            Path.GetFileName(result.BackupPath));
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void WriteUserVersion(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private static void RecordHistory(SqliteConnection connection, IMigration migration)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                description TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL
            );
            INSERT OR REPLACE INTO schema_version (version, description, applied_at_utc)
            VALUES ($version, $description, $appliedAtUtc);
            """;
        command.Parameters.AddWithValue("$version", migration.Version);
        command.Parameters.AddWithValue("$description", migration.Description);
        command.Parameters.AddWithValue("$appliedAtUtc", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
}
