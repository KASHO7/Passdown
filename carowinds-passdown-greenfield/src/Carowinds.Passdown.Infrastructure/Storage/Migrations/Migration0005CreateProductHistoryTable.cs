using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>product_history</c>: an append-only record of catalog product
/// snapshots over time, for traceability (spec §7.2 "First seen"/"Last seen" and
/// §7.3 "retains history if practical" / archived products "remain recoverable for
/// diagnostics/history"). Rows are never updated or deleted by this schema; each
/// change to a <c>catalog_products</c> row is expected to append one history row
/// alongside it. Not foreign-keyed to <c>catalog_products</c> so history for a
/// product key can be retained independently of the current product row's lifecycle.
/// </summary>
public sealed class Migration0005CreateProductHistoryTable : IMigration
{
    public int Version => 5;

    public string Description => "Create product_history table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS product_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                product_key TEXT NOT NULL,
                recorded_at_utc TEXT NOT NULL,
                display_name TEXT NOT NULL,
                lifecycle_state TEXT NOT NULL,
                current_price_amount REAL NULL,
                current_display_price TEXT NULL,
                comparison_price_amount REAL NULL,
                price_qualifier TEXT NULL,
                change_reason TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_product_history_product_key
                ON product_history (product_key, recorded_at_utc);
            """;
        command.ExecuteNonQuery();
    }
}
