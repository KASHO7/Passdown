using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>source_state</c>: per-source scheduling state (spec §6.2) —
/// last attempt, last success, next due time, a fingerprint for lightweight
/// change detection, and a consecutive-failure count. <c>source_id</c> names
/// one concrete adapter (e.g. "park-hours", "season-pass-addons"); <c>category</c>
/// is the freshness-tier grouping several adapters can share (spec §6.1), mirroring
/// <c>Carowinds.Passdown.Domain.Retrieval.SourceState</c>.
/// </summary>
public sealed class Migration0002CreateSourceStateTable : IMigration
{
    public int Version => 2;

    public string Description => "Create source_state table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS source_state (
                source_id TEXT PRIMARY KEY,
                category TEXT NOT NULL,
                last_attempt_at_utc TEXT NULL,
                last_success_at_utc TEXT NULL,
                next_due_at_utc TEXT NULL,
                fingerprint TEXT NULL,
                consecutive_failure_count INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS ix_source_state_category ON source_state (category);
            """;
        command.ExecuteNonQuery();
    }
}
