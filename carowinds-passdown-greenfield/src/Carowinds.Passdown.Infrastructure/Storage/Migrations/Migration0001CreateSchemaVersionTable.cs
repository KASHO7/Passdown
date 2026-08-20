using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates the diagnostic <c>schema_version</c> history table. The
/// authoritative applied-version gate is <c>PRAGMA user_version</c>
/// (set by <see cref="MigrationRunner"/>); this table exists so the applied
/// migration history can be inspected for troubleshooting and diagnostic
/// exports.
/// </summary>
public sealed class Migration0001CreateSchemaVersionTable : IMigration
{
    public int Version => 1;

    public string Description => "Create schema_version history table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                description TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
