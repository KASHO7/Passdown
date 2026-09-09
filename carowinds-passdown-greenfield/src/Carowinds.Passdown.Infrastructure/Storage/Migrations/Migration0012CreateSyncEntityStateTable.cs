using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>sync_entity_state</c>: the one generic local revision/conflict metadata store for every
/// syncable entity (Phase 16C §4/§5/§32) — <c>AssociateDraft</c> and <c>OperationalNote</c> today,
/// keyed by (entity_type, entity_id) rather than by adding sync columns to
/// <c>associate_drafts</c>/<c>operational_notes</c> themselves. A row here never duplicates the
/// entity's own payload — only revision counters, the last confirmed-shared content hash, and (when
/// present) conflict identity. Existing DB 11 installations upgrade in place; no prior table is
/// altered. DB schema 11 -&gt; 12. Draft schema remains 3; Snapshot schema remains 1.
/// </summary>
public sealed class Migration0012CreateSyncEntityStateTable : IMigration
{
    public int Version => 12;

    public string Description => "Create sync_entity_state table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS sync_entity_state (
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                local_revision INTEGER NOT NULL,
                synced_revision INTEGER NOT NULL,
                synced_content_hash TEXT,
                last_remote_device_id TEXT,
                last_remote_revision INTEGER,
                conflict_state TEXT NOT NULL,
                conflict_remote_device_id TEXT,
                conflict_remote_revision INTEGER,
                conflict_remote_content_hash TEXT,
                conflict_detected_at_utc TEXT,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (entity_type, entity_id)
            );
            """;
        command.ExecuteNonQuery();
    }
}
