using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>associate_drafts</c>: one manual draft per operating date (spec §6.2
/// "associate-drafts keyed by date"). <c>payload_json</c> is opaque to this
/// migration — the Associate form's own schema evolves independently of storage;
/// <c>schema_version</c> lets a future reader detect and migrate older payload
/// shapes on read, per spec §6.2's "models are versioned."
/// </summary>
public sealed class Migration0006CreateAssociateDraftsTable : IMigration
{
    public int Version => 6;

    public string Description => "Create associate_drafts table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS associate_drafts (
                operating_date TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL,
                payload_json TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
