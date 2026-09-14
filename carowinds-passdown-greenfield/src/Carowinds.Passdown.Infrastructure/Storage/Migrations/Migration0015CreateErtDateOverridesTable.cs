using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>ert_date_overrides</c> (Phase 17B spec Parts 3/19): an optional, per-operating-date
/// Early Ride Time exception (maintenance, a delayed opening, a temporary attraction swap) that wins
/// over that date's monthly default. Primary key is <c>operating_date</c>, the same shape
/// <c>associate_drafts</c> already uses for date-keyed rows — an override may exist even when no
/// <c>monthly_ert_configurations</c> row exists for its month at all (spec Part 38.I).
/// </summary>
public sealed class Migration0015CreateErtDateOverridesTable : IMigration
{
    public int Version => 15;

    public string Description => "Create ert_date_overrides table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS ert_date_overrides (
                operating_date TEXT PRIMARY KEY,
                state TEXT NOT NULL,
                start_time TEXT,
                rides_json TEXT NOT NULL,
                reason TEXT,
                modified_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
