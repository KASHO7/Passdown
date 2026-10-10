using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>finalized_passdowns</c>: the immutable historical record of exactly what associates
/// received for one operating date (spec Phase 9 Parts B/L), completely separate from the editable
/// <c>associate_drafts</c> row for the same date. <c>id</c> is a surrogate key independent of
/// <c>passdown_date</c> so a future revision design (explicitly deferred — spec Part B) would not
/// require redefining this table's identity; <c>ux_finalized_passdowns_passdown_date</c> is what
/// actually enforces "one finalized record per date" for now. <c>snapshot_json</c> is opaque to this
/// migration, exactly like <c>associate_drafts.payload_json</c> — <c>FinalizedPassdownSnapshotSerializer</c>
/// is the only code that knows its shape, gated by <c>snapshot_schema_version</c>. No PDF blob column
/// yet (spec Part I) — deferred to Phase 9C's migration rather than added now as an unused nullable
/// column, since a later <c>ALTER TABLE ... ADD COLUMN</c> is itself a safe, additive migration and
/// this table's identity/uniqueness does not change when that column arrives.
/// </summary>
public sealed class Migration0008CreateFinalizedPassdownsTable : IMigration
{
    public int Version => 8;

    public string Description => "Create finalized_passdowns table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS finalized_passdowns (
                id TEXT PRIMARY KEY,
                passdown_date TEXT NOT NULL,
                finalized_at_utc TEXT NOT NULL,
                snapshot_schema_version INTEGER NOT NULL,
                snapshot_json TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                origin TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_finalized_passdowns_passdown_date ON finalized_passdowns (passdown_date);
            """;
        command.ExecuteNonQuery();
    }
}
