using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Diagnostics;

/// <summary>
/// Reads local database metadata for diagnostics (Phase 7F Part J) without ever creating, writing
/// to, or migrating the database. Checks <see cref="File.Exists"/> before ever opening a connection
/// — <see cref="SqliteConnectionFactory.OpenConnection"/> creates the file on first use, which would
/// otherwise turn a diagnostic export into a silent side effect on a machine that has never launched
/// the app. A corrupt or unreadable database is reported via <see cref="DatabaseDiagnostics.Note"/>,
/// never thrown — a tester exporting diagnostics specifically because the database looks broken must
/// not have that export itself fail.
/// </summary>
public sealed class SqliteDatabaseDiagnosticsProvider(SqliteConnectionFactory connectionFactory) : IDatabaseDiagnosticsProvider
{
    public Task<DatabaseDiagnostics> GetAsync(CancellationToken cancellationToken = default)
    {
        var path = connectionFactory.DatabasePath;
        if (!File.Exists(path))
        {
            return Task.FromResult(new DatabaseDiagnostics(false, null, null, false, null));
        }

        long? fileSizeBytes = null;
        try
        {
            fileSizeBytes = new FileInfo(path).Length;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var walPresent = File.Exists(path + "-wal");

        int? schemaVersion = null;
        string? note = null;
        try
        {
            using var connection = connectionFactory.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            schemaVersion = Convert.ToInt32(command.ExecuteScalar());
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            note = "Could not read the schema version — the database file may be corrupt or inaccessible.";
        }

        return Task.FromResult(new DatabaseDiagnostics(true, schemaVersion, fileSizeBytes, walPresent, note));
    }
}
