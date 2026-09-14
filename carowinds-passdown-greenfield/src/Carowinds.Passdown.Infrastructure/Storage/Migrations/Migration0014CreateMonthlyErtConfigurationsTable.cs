using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>monthly_ert_configurations</c> (Phase 17B spec Parts 2/5/19): a persistent Early Ride
/// Time default for one calendar month, replacing the old per-date-derived-from-park-hours behavior.
/// Primary key is <c>year_month</c> (invariant <c>yyyy-MM</c>, see <c>MonthlyErtConfiguration.YearMonthKey</c>)
/// rather than a surrogate id — September 2026 and September 2027 are different rows by construction
/// (spec Part 5's "do not key by month number alone"), and this is the exact same identity the
/// Personal Sync entity uses, so no separate mapping table is needed. <c>rides_json</c> is a plain
/// JSON string array — no attraction catalog exists anywhere in this codebase to key against (spec
/// Part 10), matching the existing <c>EarlyRideTimeDetails.ParticipatingAttractions</c> precedent.
/// </summary>
public sealed class Migration0014CreateMonthlyErtConfigurationsTable : IMigration
{
    public int Version => 14;

    public string Description => "Create monthly_ert_configurations table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS monthly_ert_configurations (
                year_month TEXT PRIMARY KEY,
                year INTEGER NOT NULL,
                month INTEGER NOT NULL,
                state TEXT NOT NULL,
                start_time TEXT,
                rides_json TEXT NOT NULL,
                note TEXT,
                modified_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
