using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>manager_drafts</c>: one manual draft per operating date and shift
/// (spec §6.2 "manager-drafts keyed by date or operating day/shift"). <c>shift</c>
/// defaults to a single sentinel value so a single-shift draft needs no special
/// casing, while still supporting multiple shifts per date if the Manager workflow
/// (spec §9) needs them. <c>payload_json</c>/<c>schema_version</c> follow the same
/// versioned-opaque-payload approach as <c>associate_drafts</c>.
/// </summary>
public sealed class Migration0007CreateManagerDraftsTable : IMigration
{
    public int Version => 7;

    public string Description => "Create manager_drafts table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS manager_drafts (
                operating_date TEXT NOT NULL,
                shift TEXT NOT NULL DEFAULT 'default',
                schema_version INTEGER NOT NULL,
                payload_json TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,

                PRIMARY KEY (operating_date, shift)
            );
            """;
        command.ExecuteNonQuery();
    }
}
