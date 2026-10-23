using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Phase 26: creates <c>synced_price_observations</c> — verified official price observations merged
/// in from other computers through Personal Sync, kept apart from this computer's own
/// <c>season_price_history</c>. A separate table rather than new rows in <c>season_price_history</c>:
/// that table's (product, sequence) series is this computer's own recorder state — its newest row is
/// what the next official check extends or closes — so foreign rows there would let an older imported
/// interval masquerade as this computer's latest observation. Nothing existing is altered and no row
/// is copied or rewritten; every RC.8 row survives untouched.
/// <para>
/// <c>observation_id</c> is the content identity (<c>OfficialPriceObservationIdentity</c>); every other
/// column is the contributor's original value except <c>last_confirmed_at_utc</c> (only ever moved
/// forward) and <c>imported_at_utc</c> (when this computer first stored it — never shown as an official
/// check time). <c>amount</c> is canonical invariant text ("99.00") so the identity can be recomputed
/// exactly.
/// </para>
/// </summary>
public sealed class Migration0017CreateSyncedPriceObservationsTable : IMigration
{
    public int Version => 17;

    public string Description => "Create synced_price_observations table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS synced_price_observations (
                observation_id TEXT PRIMARY KEY,
                contributor_device_id TEXT NOT NULL,
                contributor_sequence INTEGER NOT NULL,
                product_key TEXT NOT NULL,
                amount TEXT NOT NULL,
                price_kind TEXT NOT NULL,
                display_price TEXT NULL,
                price_qualifier TEXT NULL,
                display_name TEXT NOT NULL,
                source_name TEXT NOT NULL,
                first_observed_at_utc TEXT NOT NULL,
                last_confirmed_at_utc TEXT NOT NULL,
                origin TEXT NOT NULL,
                currency TEXT NOT NULL,
                billing_unit TEXT NOT NULL,
                imported_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_synced_price_observations_product_key ON synced_price_observations (product_key);
            """;
        command.ExecuteNonQuery();
    }
}
