using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage;

/// <summary>
/// Opens connections to the local SQLite store, creating the containing
/// directory and database file on first use if they do not exist.
/// </summary>
public sealed class SqliteConnectionFactory(string databasePath)
{
    /// <summary>
    /// Busy-timeout (Phase 6B Part H): the interactive desktop UI's autosave and a scheduled/
    /// startup-catch-up refresh are both expected to open short-lived connections against the
    /// same database file. Microsoft.Data.Sqlite's "Default Timeout" makes a command retry
    /// internally for up to this long before raising SQLITE_BUSY, rather than failing the instant
    /// two writes overlap by a few milliseconds — comfortably longer than any single operation
    /// here takes, without redesigning storage or serializing UI and refresh writes against each
    /// other. Made explicit here rather than left at the driver's own default (itself already a
    /// generous 60 seconds): a short, deliberately-chosen value that still comfortably covers this
    /// app's short read/write operations, and fails fast rather than hanging for a full minute if
    /// something is ever genuinely stuck.
    /// </summary>
    private static readonly TimeSpan BusyTimeout = TimeSpan.FromSeconds(5);

    public string DatabasePath { get; } = databasePath;

    public SqliteConnection OpenConnection()
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(BuildConnectionString());
        connection.Open();
        return connection;
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(BuildConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private string BuildConnectionString() =>
        $"Data Source={DatabasePath};Default Timeout={(int)BusyTimeout.TotalSeconds}";
}
