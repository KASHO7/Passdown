using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>sync_conflicts</c>: the durable store for an unresolved conflict's full conflicting
/// remote envelope, including its payload (Phase 16D §2/§31) — <c>sync_entity_state</c> (Migration
/// 0012) only ever carried identity/hash fingerprints, not enough to resolve a conflict after an app
/// restart or after the sync folder becomes temporarily unavailable. One generic table, keyed by
/// (entity_type, entity_id), for every conflict-capable entity type — never a per-entity-type table.
/// Purely additive: no existing table is altered. DB schema 12 -&gt; 13. Draft schema remains 3;
/// Snapshot schema remains 1.
/// </summary>
public sealed class Migration0013CreateSyncConflictsTable : IMigration
{
    public int Version => 13;

    public string Description => "Create sync_conflicts table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS sync_conflicts (
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                remote_device_id TEXT NOT NULL,
                remote_revision INTEGER NOT NULL,
                remote_modified_at_utc TEXT NOT NULL,
                remote_is_deleted INTEGER NOT NULL,
                remote_payload_schema_version INTEGER,
                remote_payload_json TEXT,
                remote_content_hash TEXT,
                detected_at_utc TEXT NOT NULL,
                newer_remote_device_id TEXT,
                newer_remote_revision INTEGER,
                newer_remote_detected_at_utc TEXT,
                PRIMARY KEY (entity_type, entity_id)
            );
            """;
        command.ExecuteNonQuery();
    }
}
