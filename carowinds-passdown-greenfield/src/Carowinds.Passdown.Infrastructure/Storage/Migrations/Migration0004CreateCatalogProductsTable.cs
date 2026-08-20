using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Creates <c>catalog_products</c>: one row per stable normalized product identity
/// (spec §7.2, mirrors <c>Carowinds.Passdown.Domain.Catalog.CatalogProductKey</c>),
/// with <c>product_key</c> as primary key enforcing that identity at the storage
/// layer. Lifecycle columns (<c>lifecycle_state</c>, seen/verified timestamps,
/// consecutive-absence count, archive fields) mirror
/// <c>Carowinds.Passdown.Domain.Catalog.CatalogProductLifecycle</c>.
///
/// Pricing/qualifier columns are included per spec §7.2's "store at least" list so
/// later phases can populate them without a further migration, but they are plain
/// nullable value columns with no reference to any specific retrieval adapter —
/// this migration does not couple catalog storage to how a value was retrieved.
/// </summary>
public sealed class Migration0004CreateCatalogProductsTable : IMigration
{
    public int Version => 4;

    public string Description => "Create catalog_products table";

    public void Apply(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS catalog_products (
                product_key TEXT PRIMARY KEY,
                category TEXT NOT NULL,
                display_name TEXT NOT NULL,
                lifecycle_state TEXT NOT NULL,

                first_seen_at_utc TEXT NOT NULL,
                last_seen_at_utc TEXT NOT NULL,
                last_verified_at_utc TEXT NULL,
                consecutive_confirmed_absence_count INTEGER NOT NULL DEFAULT 0,
                archived_at_utc TEXT NULL,
                archived_reason TEXT NULL,

                current_price_amount REAL NULL,
                current_display_price TEXT NULL,
                comparison_price_amount REAL NULL,
                price_qualifier TEXT NULL,
                date_specific INTEGER NOT NULL DEFAULT 0,
                applicable_date TEXT NULL,

                last_evidence_status TEXT NOT NULL,
                last_evidence_checked_at_utc TEXT NOT NULL,
                last_evidence_strategy TEXT NOT NULL,
                last_evidence_source_name TEXT NOT NULL,
                last_evidence_source_url TEXT NOT NULL,
                last_evidence_detail TEXT NOT NULL,
                last_evidence_technical_detail TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_catalog_products_category_state
                ON catalog_products (category, lifecycle_state);
            """;
        command.ExecuteNonQuery();
    }
}
