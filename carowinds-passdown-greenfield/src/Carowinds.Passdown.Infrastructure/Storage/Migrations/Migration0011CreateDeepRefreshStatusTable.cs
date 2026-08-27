using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>deep_refresh_status</c>: a single-row table recording the outcome of the most recent
/// Deep Refresh operation (Phase 13D Part U/BD), whichever caller ran it — scheduled/startup-catch-up
/// maintenance, the manual "Run Deep Refresh" button, or the insufficient-coverage Check Online
/// bootstrap. A small, genuinely-required addition: the existing <c>source_state</c> table is keyed
/// per-source (spec §6.2), so it can represent "was Park Hours last checked" but not "how many
/// sources succeeded/failed in the last whole Deep Refresh job" or "what future date range is
/// actually covered" — neither of those is a per-source concept. Always exactly one row (id fixed at
/// 1) — this is a single current-status record, not a history log.
/// </summary>
public sealed class Migration0011CreateDeepRefreshStatusTable : IMigration
{
    public int Version => 11;

    public string Description => "Create deep_refresh_status table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS deep_refresh_status (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                last_attempt_at_utc TEXT NOT NULL,
                last_success_at_utc TEXT,
                outcome TEXT NOT NULL,
                coverage_through TEXT,
                source_success_count INTEGER NOT NULL,
                source_failure_count INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
