using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage;

public enum DatabaseBackupStatus
{
    /// <summary>A new backup file was written and verified.</summary>
    Created,

    /// <summary>There is no database yet (a computer that never ran the app) — nothing to protect.</summary>
    NoDatabase,

    InsufficientDiskSpace,

    /// <summary>Another process held the database for longer than the busy timeout.</summary>
    DatabaseInUse,

    /// <summary>The database (and so any copy of it) failed SQLite's integrity check, or is not a database.</summary>
    DatabaseDamaged,

    Failed,
}

/// <param name="BackupPath">The verified backup file, when one was created.</param>
/// <param name="SchemaVersion">The database's <c>PRAGMA user_version</c> at the moment it was copied.</param>
public sealed record DatabaseBackupResult(DatabaseBackupStatus Status, string? BackupPath, int? SchemaVersion)
{
    public bool Succeeded => Status is DatabaseBackupStatus.Created or DatabaseBackupStatus.NoDatabase;
}

/// <summary>
/// Phase 27: a consistent, verified copy of the local SQLite database, taken before anything changes its format
/// (by <see cref="MigrationRunner"/>) or replaces the program that reads it (by the external updater).
/// <para>
/// The copy is made with SQLite's online backup API from a live connection — never a file copy — so it is a
/// single consistent snapshot that already includes anything still in a rollback journal or WAL file, and a hot
/// journal left by a crash is rolled back by SQLite before anything is read. It is written to a new
/// <c>.partial</c> file, checked (<c>PRAGMA integrity_check</c>, same schema version, same tables, same row counts)
/// and only then renamed to its final, unique name. An existing file is never overwritten and a failed attempt
/// leaves nothing behind. Backups are kept until the user deletes them.
/// </para>
/// </summary>
public sealed class SqliteDatabaseBackup(Func<string, long?>? availableFreeSpace = null)
{
    /// <summary><c>%LOCALAPPDATA%\Carowinds Passdown\backups</c> in production: beside the database, never beside the program files.</summary>
    public const string BackupDirectoryName = "backups";

    public const string ReadMeFileName = "README.txt";

    public const long FreeSpaceMarginBytes = 16L * 1024 * 1024;

    private const int BusyTimeoutSeconds = 5;

    private readonly Func<string, long?> _availableFreeSpace = availableFreeSpace ?? DriveFreeSpace;

    public static string DefaultDirectoryFor(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".", BackupDirectoryName);

    /// <param name="label">Describes why the backup was taken, e.g. <c>pre-migration-schema-16-to-17</c>; becomes part of the file name.</param>
    public DatabaseBackupResult Create(string databasePath, string backupDirectory, string label)
    {
        if (!File.Exists(databasePath))
        {
            return new DatabaseBackupResult(DatabaseBackupStatus.NoDatabase, null, null);
        }

        string? partial = null;
        try
        {
            Directory.CreateDirectory(backupDirectory);
            if (_availableFreeSpace(backupDirectory) is { } free && free < RequiredSpace(databasePath))
            {
                return new DatabaseBackupResult(DatabaseBackupStatus.InsufficientDiskSpace, null, null);
            }

            var final = UniqueBackupPath(backupDirectory, label);
            partial = final + ".partial";

            int schemaVersion;
            using (var source = Open(databasePath, SqliteOpenMode.ReadWrite))
            {
                using (var destination = Open(partial, SqliteOpenMode.ReadWriteCreate))
                {
                    source.BackupDatabase(destination);

                    // The copy carries the source's journal mode; a WAL-mode copy would need side files. One
                    // self-contained file is what a person restoring it by hand expects.
                    using var single = destination.CreateCommand();
                    single.CommandText = "PRAGMA journal_mode = DELETE;";
                    single.ExecuteNonQuery();
                }

                schemaVersion = UserVersion(source);
                if (!Verify(source, partial, schemaVersion, out var damaged))
                {
                    TryDelete(partial);
                    return new DatabaseBackupResult(damaged ? DatabaseBackupStatus.DatabaseDamaged : DatabaseBackupStatus.Failed, null, schemaVersion);
                }
            }

            File.Move(partial, final, overwrite: false);
            WriteReadMe(backupDirectory);
            return new DatabaseBackupResult(DatabaseBackupStatus.Created, final, schemaVersion);
        }
        catch (SqliteException ex)
        {
            TryDelete(partial);
            return new DatabaseBackupResult(Classify(ex), null, null);
        }
        catch (IOException ex) when (ex.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027))
        {
            TryDelete(partial);
            return new DatabaseBackupResult(DatabaseBackupStatus.InsufficientDiskSpace, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(partial);
            return new DatabaseBackupResult(DatabaseBackupStatus.Failed, null, null);
        }
    }

    /// <summary>
    /// The database's schema version, or null when there is no database. Opening read-write lets SQLite roll back
    /// a hot journal left by a process that was stopped mid-transaction, so the answer is the committed version.
    /// Throws <see cref="SqliteException"/>/<see cref="IOException"/> when it cannot be determined.
    /// </summary>
    public static int? ReadSchemaVersion(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return null;
        }

        using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
        return UserVersion(connection);
    }

    /// <summary>Opens without pooling, so no handle outlives the call (Windows cannot rename or replace an open file).</summary>
    internal static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = BusyTimeoutSeconds,
        }.ToString());
        connection.Open();
        return connection;
    }

    internal static int UserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>True when <c>PRAGMA integrity_check</c> reports exactly "ok".</summary>
    internal static bool IntegrityOk(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows is ["ok"];
    }

    private static bool Verify(SqliteConnection source, string backupPath, int schemaVersion, out bool damaged)
    {
        damaged = false;
        using var backup = Open(backupPath, SqliteOpenMode.ReadOnly);
        if (!IntegrityOk(backup))
        {
            // The copy is page-for-page what SQLite read from the source, so a failed check means the source is damaged.
            damaged = true;
            return false;
        }

        if (UserVersion(backup) != schemaVersion)
        {
            return false;
        }

        var sourceCounts = RowCounts(source);
        var backupCounts = RowCounts(backup);
        return sourceCounts.Count == backupCounts.Count
            && sourceCounts.All(pair => backupCounts.TryGetValue(pair.Key, out var count) && count == pair.Value);
    }

    private static Dictionary<string, long> RowCounts(SqliteConnection connection)
    {
        var tables = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\";";
            counts[table] = Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        return counts;
    }

    private static DatabaseBackupStatus Classify(SqliteException ex) => ex.SqliteErrorCode switch
    {
        5 or 6 => DatabaseBackupStatus.DatabaseInUse, // SQLITE_BUSY, SQLITE_LOCKED
        11 or 26 => DatabaseBackupStatus.DatabaseDamaged, // SQLITE_CORRUPT, SQLITE_NOTADB
        13 => DatabaseBackupStatus.InsufficientDiskSpace, // SQLITE_FULL
        _ => DatabaseBackupStatus.Failed,
    };

    /// <summary>Room for the copy itself (database plus any journal/WAL it may absorb) and a margin.</summary>
    private static long RequiredSpace(string databasePath)
    {
        long size = new FileInfo(databasePath).Length;
        foreach (var suffix in new[] { "-wal", "-journal" })
        {
            var side = new FileInfo(databasePath + suffix);
            if (side.Exists)
            {
                size += side.Length;
            }
        }

        return size + FreeSpaceMarginBytes;
    }

    private static string UniqueBackupPath(string directory, string label)
    {
        var safe = new StringBuilder();
        foreach (var c in label)
        {
            safe.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '-');
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        while (true)
        {
            var path = Path.Combine(directory, $"carowinds-passdown-{safe}-{stamp}Z-{Guid.NewGuid().ToString("N")[..8]}.db");
            if (!File.Exists(path) && !File.Exists(path + ".partial"))
            {
                return path;
            }
        }
    }

    private static void WriteReadMe(string directory)
    {
        var path = Path.Combine(directory, ReadMeFileName);
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream);
            writer.Write(
                """
                Carowinds Operations database backups

                Each .db file here is a complete, checked copy of your Carowinds Operations database, made automatically
                before an update or before the app changed the database format. The file name says when and why.
                Nothing here is deleted automatically; you can delete old backups once the new version works for you.

                To restore one (only if asked to, or if the recovery guide says so):
                1. Close Carowinds Operations completely (and avoid 6:00 AM, when the daily refresh runs).
                2. In the folder above this one, move carowinds-passdown.db (and any carowinds-passdown.db-wal,
                   carowinds-passdown.db-shm or carowinds-passdown.db-journal) into a new folder. Do not delete them:
                   they hold any work done since the backup.
                3. Copy the backup you want into the folder above and rename the copy to carowinds-passdown.db.
                4. Start the version of Carowinds Operations that matches the backup (see the release notes).
                """);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Already there, or not writable: the backups themselves are what matters.
        }
    }

    private static long? DriveFreeSpace(string path)
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

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
