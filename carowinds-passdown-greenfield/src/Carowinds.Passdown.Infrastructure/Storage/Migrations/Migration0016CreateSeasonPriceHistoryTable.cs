using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Migrations;

/// <summary>
/// Phase 20A: creates <c>season_price_history</c> — one row per advertised price interval of a
/// tracked Season Pass / Season Pass Add-On (<see cref="SeasonPriceHistoryInterval"/>). A new table
/// rather than reusing <c>product_history</c>: that table is an append-only audit of every catalog
/// change (lifecycle, name, comparison price) with no "last confirmed" time, and rows written for an
/// absence carry the previous price forward, so it cannot answer "how long was this price shown"
/// without misreading lifecycle rows as price observations. Nothing existing is altered.
///
/// <b>Backfill</b> (one time, inside this migration's transaction): <c>product_history</c> rows were
/// written only from successful official retrievals, with the check time, so the <c>Active</c>-state
/// rows of a tracked product are genuine price observations. They are folded into intervals with the
/// same rules as live recording (<see cref="SeasonPriceTimeline"/>): identical consecutive states
/// extend one interval, a retirement (<c>Archived</c>/<c>Expired</c> row) means the next observation
/// opens a new one, and absence/retirement rows themselves never become a price state. The open
/// interval's last-confirmed time is taken from <c>catalog_products.last_seen_at_utc</c> when the
/// catalog still shows that same state. Backfilled rows are marked <c>origin = 'Backfilled'</c>.
/// Nothing is inferred beyond those rows — no earlier price is ever fabricated.
/// </summary>
public sealed class Migration0016CreateSeasonPriceHistoryTable : IMigration
{
    public int Version => 16;

    public string Description => "Create season_price_history table";

    public void Apply(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS season_price_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    product_key TEXT NOT NULL,
                    sequence INTEGER NOT NULL,
                    amount REAL NOT NULL,
                    price_kind TEXT NOT NULL,
                    display_price TEXT NULL,
                    price_qualifier TEXT NULL,
                    display_name TEXT NOT NULL,
                    source_name TEXT NOT NULL,
                    first_observed_at_utc TEXT NOT NULL,
                    last_confirmed_at_utc TEXT NOT NULL,
                    origin TEXT NOT NULL,
                    UNIQUE (product_key, sequence)
                );
                """;
            command.ExecuteNonQuery();
        }

        Backfill(connection);
    }

    private sealed record HistoryRow(
        string ProductKey,
        DateTimeOffset RecordedAt,
        string DisplayName,
        ProductLifecycleState LifecycleState,
        decimal? Amount,
        string? DisplayPrice,
        decimal? ComparisonAmount,
        string? Qualifier);

    private sealed record CatalogRow(
        SourceCategory Category,
        string SourceName,
        string DisplayName,
        bool DateSpecific,
        ProductLifecycleState LifecycleState,
        DateTimeOffset LastSeenAt,
        decimal? Amount,
        string? Qualifier,
        decimal? ComparisonAmount);

    private static void Backfill(SqliteConnection connection)
    {
        var catalog = ReadCatalogCandidates(connection);
        if (catalog.Count == 0)
        {
            return;
        }

        var history = ReadHistory(connection);
        foreach (var (productKey, rows) in history)
        {
            if (!catalog.TryGetValue(productKey, out var product) ||
                !PriceHistoryTrackingPolicy.ShouldTrackPriceHistory(product.Category, product.SourceName, product.DisplayName, product.DateSpecific))
            {
                continue;
            }

            var intervals = Fold(productKey, rows, product);
            foreach (var interval in intervals)
            {
                Insert(connection, interval);
            }
        }
    }

    private static List<SeasonPriceHistoryInterval> Fold(string productKey, IReadOnlyList<HistoryRow> rows, CatalogRow product)
    {
        var intervals = new List<SeasonPriceHistoryInterval>();
        var retiredSinceLastObservation = false;

        foreach (var row in rows)
        {
            if (row.LifecycleState is ProductLifecycleState.Archived or ProductLifecycleState.Expired)
            {
                retiredSinceLastObservation = true;
                continue;
            }

            if (row.LifecycleState != ProductLifecycleState.Active || row.Amount is not { } amount)
            {
                continue; // PendingConfirmation rows record an absence; no amount is not a price state.
            }

            var state = new SeasonPriceState(amount, PriceSemanticsClassifier.ClassifyCatalog(row.Qualifier, row.ComparisonAmount));
            var current = intervals.Count == 0 ? null : intervals[^1];
            var outcome = SeasonPriceTimeline.Classify(current?.State, state, retiredSinceLastObservation);
            retiredSinceLastObservation = false;

            if (!SeasonPriceTimeline.StartsNewInterval(outcome))
            {
                if (row.RecordedAt > current!.LastConfirmedAt)
                {
                    intervals[^1] = current with { LastConfirmedAt = row.RecordedAt };
                }

                continue;
            }

            intervals.Add(new SeasonPriceHistoryInterval(
                productKey,
                intervals.Count + 1,
                state.Amount,
                state.Kind,
                row.DisplayPrice,
                row.Qualifier,
                row.DisplayName,
                product.SourceName,
                row.RecordedAt,
                row.RecordedAt,
                SeasonPriceHistoryOrigin.Backfilled));
        }

        if (intervals.Count > 0 &&
            product.LifecycleState is ProductLifecycleState.Active or ProductLifecycleState.PendingConfirmation &&
            !retiredSinceLastObservation &&
            product.Amount is { } catalogAmount &&
            new SeasonPriceState(catalogAmount, PriceSemanticsClassifier.ClassifyCatalog(product.Qualifier, product.ComparisonAmount)) == intervals[^1].State &&
            product.LastSeenAt > intervals[^1].LastConfirmedAt)
        {
            intervals[^1] = intervals[^1] with { LastConfirmedAt = product.LastSeenAt };
        }

        return intervals;
    }

    private static Dictionary<string, CatalogRow> ReadCatalogCandidates(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT product_key, category, last_evidence_source_name, display_name, date_specific, lifecycle_state,
                   last_seen_at_utc, current_price_amount, price_qualifier, comparison_price_amount
            FROM catalog_products
            WHERE category IN ('SeasonPasses', 'AddOns', 'MilitaryFirstResponder');
            """;

        var result = new Dictionary<string, CatalogRow>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!Enum.TryParse<SourceCategory>(reader.GetString(1), out var category) ||
                !Enum.TryParse<ProductLifecycleState>(reader.GetString(5), out var lifecycle))
            {
                continue;
            }

            result[reader.GetString(0)] = new CatalogRow(
                category,
                reader.GetString(2),
                reader.GetString(3),
                SqliteConvert.ReadBool(reader, 4),
                lifecycle,
                SqliteConvert.ReadDateTimeOffset(reader, 6),
                SqliteConvert.ReadNullableDecimal(reader, 7),
                SqliteConvert.ReadNullableString(reader, 8),
                SqliteConvert.ReadNullableDecimal(reader, 9));
        }

        return result;
    }

    private static Dictionary<string, List<HistoryRow>> ReadHistory(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT product_key, recorded_at_utc, display_name, lifecycle_state,
                   current_price_amount, current_display_price, comparison_price_amount, price_qualifier
            FROM product_history
            ORDER BY product_key, recorded_at_utc, id;
            """;

        var result = new Dictionary<string, List<HistoryRow>>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!Enum.TryParse<ProductLifecycleState>(reader.GetString(3), out var lifecycle))
            {
                continue;
            }

            var key = reader.GetString(0);
            if (!result.TryGetValue(key, out var rows))
            {
                rows = [];
                result[key] = rows;
            }

            rows.Add(new HistoryRow(
                key,
                SqliteConvert.ReadDateTimeOffset(reader, 1),
                reader.GetString(2),
                lifecycle,
                SqliteConvert.ReadNullableDecimal(reader, 4),
                SqliteConvert.ReadNullableString(reader, 5),
                SqliteConvert.ReadNullableDecimal(reader, 6),
                SqliteConvert.ReadNullableString(reader, 7)));
        }

        return result;
    }

    private static void Insert(SqliteConnection connection, SeasonPriceHistoryInterval interval)
    {
        using var command = connection.CreateCommand();
        Repositories.SqliteSeasonPriceHistoryRepository.ConfigureInsertCommand(command, interval);
        command.ExecuteNonQuery();
    }
}
