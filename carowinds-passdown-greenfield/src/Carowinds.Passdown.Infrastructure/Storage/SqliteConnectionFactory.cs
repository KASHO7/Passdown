using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage;

/// <summary>
/// Opens connections to the local SQLite store, creating the containing
/// directory and database file on first use if they do not exist.
/// </summary>
public sealed class SqliteConnectionFactory(string databasePath)
{
    public string DatabasePath { get; } = databasePath;

    public SqliteConnection OpenConnection()
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        return connection;
    }
}
