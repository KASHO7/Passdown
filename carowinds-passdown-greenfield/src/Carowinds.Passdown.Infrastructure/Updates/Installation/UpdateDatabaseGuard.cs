using Carowinds.Passdown.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Updates.Installation;

/// <summary>
/// Phase 27: what the external updater needs to know about the user's database. A new version may upgrade the
/// database when it first starts, and an older version cannot open an upgraded database — so swapping the old
/// program files back is a safe rollback only while the database is still in the format it had before the
/// install. The updater backs the database up before it changes anything, and checks the format again before it
/// puts the old version back.
/// </summary>
public interface IUpdateDatabaseGuard
{
    /// <summary>Makes a verified backup before any installed file is touched. <see cref="DatabaseBackupStatus.NoDatabase"/> is fine.</summary>
    DatabaseBackupResult BackUpBeforeInstall(string fromVersion, string toVersion);

    /// <summary>
    /// The database's current schema version (<paramref name="schemaVersion"/> is null when there is no database).
    /// False when it cannot be determined — locked, unreadable or damaged — which the updater treats as "changed".
    /// </summary>
    bool TryReadSchemaVersion(out int? schemaVersion);

    /// <summary>The live database path, for the recovery instructions.</summary>
    string DatabasePath { get; }
}

/// <summary>Phase 27: <see cref="IUpdateDatabaseGuard"/> over the app's real SQLite database.</summary>
public sealed class SqliteUpdateDatabaseGuard(string databasePath, string backupDirectory, SqliteDatabaseBackup? backup = null) : IUpdateDatabaseGuard
{
    /// <summary>Must match <see cref="StorageOptions.DatabaseFileName"/>'s default and the Desktop app's <c>appsettings.json</c> (guarded by a test).</summary>
    public const string DatabaseFileName = "carowinds-passdown.db";

    private readonly SqliteDatabaseBackup _backup = backup ?? new SqliteDatabaseBackup();

    /// <summary>The database the Desktop app uses: <c>%LOCALAPPDATA%\Carowinds Passdown\carowinds-passdown.db</c>.</summary>
    public static SqliteUpdateDatabaseGuard Default { get; } = ForDatabase(Path.Combine(AppPaths.LocalApplicationDataDirectory, DatabaseFileName));

    public static SqliteUpdateDatabaseGuard ForDatabase(string databasePath, SqliteDatabaseBackup? backup = null) =>
        new(databasePath, SqliteDatabaseBackup.DefaultDirectoryFor(databasePath), backup);

    public string DatabasePath { get; } = databasePath;

    public DatabaseBackupResult BackUpBeforeInstall(string fromVersion, string toVersion) =>
        _backup.Create(DatabasePath, backupDirectory, $"pre-update-{fromVersion}-to-{toVersion}");

    public bool TryReadSchemaVersion(out int? schemaVersion)
    {
        try
        {
            schemaVersion = SqliteDatabaseBackup.ReadSchemaVersion(DatabasePath);
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            schemaVersion = null;
            return false;
        }
    }
}
