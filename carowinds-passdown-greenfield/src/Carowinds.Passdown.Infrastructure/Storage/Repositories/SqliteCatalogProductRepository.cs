using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>
/// SQLite-backed <see cref="ICatalogProductRepository"/> over the <c>catalog_products</c>
/// table (spec §7.2). Persists whatever lifecycle/pricing state the caller supplies;
/// deciding what that state should become (add/update/archive) is a later phase's
/// reconciliation responsibility, not this repository's.
/// </summary>
public sealed class SqliteCatalogProductRepository(SqliteConnectionFactory connectionFactory) : ICatalogProductRepository
{
    private const string SelectColumns =
        """
        product_key, category, display_name, lifecycle_state,
        first_seen_at_utc, last_seen_at_utc, last_verified_at_utc,
        consecutive_confirmed_absence_count, archived_at_utc, archived_reason,
        current_price_amount, current_display_price, comparison_price_amount, price_qualifier,
        date_specific, applicable_date,
        last_evidence_status, last_evidence_checked_at_utc, last_evidence_strategy,
        last_evidence_source_name, last_evidence_source_url, last_evidence_detail, last_evidence_technical_detail
        """;

    public async Task<CatalogProductRecord?> GetAsync(CatalogProductKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM catalog_products WHERE product_key = $productKey;";
        command.Parameters.AddWithValue("$productKey", key.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task UpsertAsync(CatalogProductUpsert product, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            ConfigureUpsertCommand(sharedCommand, product);
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        ConfigureUpsertCommand(command, product);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ConfigureUpsertCommand(SqliteCommand command, CatalogProductUpsert product)
    {
        command.CommandText =
            """
            INSERT INTO catalog_products (
                product_key, category, display_name, lifecycle_state,
                first_seen_at_utc, last_seen_at_utc, last_verified_at_utc,
                consecutive_confirmed_absence_count, archived_at_utc, archived_reason,
                current_price_amount, current_display_price, comparison_price_amount, price_qualifier,
                date_specific, applicable_date,
                last_evidence_status, last_evidence_checked_at_utc, last_evidence_strategy,
                last_evidence_source_name, last_evidence_source_url, last_evidence_detail, last_evidence_technical_detail
            ) VALUES (
                $productKey, $category, $displayName, $lifecycleState,
                $firstSeenAt, $lastSeenAt, $lastVerifiedAt,
                $absenceCount, $archivedAt, $archivedReason,
                $priceAmount, $displayPrice, $comparisonPrice, $qualifier,
                $dateSpecific, $applicableDate,
                $evidenceStatus, $evidenceCheckedAt, $evidenceStrategy,
                $evidenceSourceName, $evidenceSourceUrl, $evidenceDetail, $evidenceTechnicalDetail
            )
            ON CONFLICT(product_key) DO UPDATE SET
                category = excluded.category,
                display_name = excluded.display_name,
                lifecycle_state = excluded.lifecycle_state,
                first_seen_at_utc = excluded.first_seen_at_utc,
                last_seen_at_utc = excluded.last_seen_at_utc,
                last_verified_at_utc = excluded.last_verified_at_utc,
                consecutive_confirmed_absence_count = excluded.consecutive_confirmed_absence_count,
                archived_at_utc = excluded.archived_at_utc,
                archived_reason = excluded.archived_reason,
                current_price_amount = excluded.current_price_amount,
                current_display_price = excluded.current_display_price,
                comparison_price_amount = excluded.comparison_price_amount,
                price_qualifier = excluded.price_qualifier,
                date_specific = excluded.date_specific,
                applicable_date = excluded.applicable_date,
                last_evidence_status = excluded.last_evidence_status,
                last_evidence_checked_at_utc = excluded.last_evidence_checked_at_utc,
                last_evidence_strategy = excluded.last_evidence_strategy,
                last_evidence_source_name = excluded.last_evidence_source_name,
                last_evidence_source_url = excluded.last_evidence_source_url,
                last_evidence_detail = excluded.last_evidence_detail,
                last_evidence_technical_detail = excluded.last_evidence_technical_detail;
            """;

        command.Parameters.AddWithValue("$productKey", product.Key.ToString());
        command.Parameters.AddWithValue("$category", product.Key.Category.ToString());
        command.Parameters.AddWithValue("$displayName", product.DisplayName);
        command.Parameters.AddWithValue("$lifecycleState", product.LifecycleState.ToString());
        command.Parameters.AddWithValue("$firstSeenAt", SqliteConvert.ToText(product.FirstSeenAt));
        command.Parameters.AddWithValue("$lastSeenAt", SqliteConvert.ToText(product.LastSeenAt));
        command.Parameters.AddWithValue("$lastVerifiedAt", SqliteConvert.ToParameter(product.LastVerifiedAt));
        command.Parameters.AddWithValue("$absenceCount", product.ConsecutiveConfirmedAbsenceCount);
        command.Parameters.AddWithValue("$archivedAt", SqliteConvert.ToParameter(product.ArchivedAt));
        command.Parameters.AddWithValue("$archivedReason", SqliteConvert.ToParameter(product.ArchivedReason));
        command.Parameters.AddWithValue("$priceAmount", SqliteConvert.ToParameter(product.CurrentPriceAmount));
        command.Parameters.AddWithValue("$displayPrice", SqliteConvert.ToParameter(product.CurrentDisplayPrice));
        command.Parameters.AddWithValue("$comparisonPrice", SqliteConvert.ToParameter(product.ComparisonPriceAmount));
        command.Parameters.AddWithValue("$qualifier", SqliteConvert.ToParameter(product.PriceQualifier));
        command.Parameters.AddWithValue("$dateSpecific", SqliteConvert.ToParameter(product.DateSpecific));
        command.Parameters.AddWithValue("$applicableDate", SqliteConvert.ToParameter(product.ApplicableDate));
        command.Parameters.AddWithValue("$evidenceStatus", product.LastEvidence.Status.ToString());
        command.Parameters.AddWithValue("$evidenceCheckedAt", SqliteConvert.ToText(product.LastEvidence.TimeChecked));
        command.Parameters.AddWithValue("$evidenceStrategy", product.LastEvidence.Strategy.ToString());
        command.Parameters.AddWithValue("$evidenceSourceName", product.LastEvidence.SourceName);
        command.Parameters.AddWithValue("$evidenceSourceUrl", product.LastEvidence.SourceUrl.ToString());
        command.Parameters.AddWithValue("$evidenceDetail", product.LastEvidence.Detail);
        command.Parameters.AddWithValue("$evidenceTechnicalDetail", SqliteConvert.ToParameter(product.LastEvidence.TechnicalDetail));
    }

    public async Task<IReadOnlyList<CatalogProductRecord>> GetByCategoryAsync(
        SourceCategory category,
        ProductLifecycleState? lifecycleState = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = lifecycleState is null
            ? $"SELECT {SelectColumns} FROM catalog_products WHERE category = $category ORDER BY product_key;"
            : $"SELECT {SelectColumns} FROM catalog_products WHERE category = $category AND lifecycle_state = $lifecycleState ORDER BY product_key;";
        command.Parameters.AddWithValue("$category", category.ToString());
        if (lifecycleState is { } state)
        {
            command.Parameters.AddWithValue("$lifecycleState", state.ToString());
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<CatalogProductRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    private static CatalogProductRecord Map(SqliteDataReader reader)
    {
        // Columns 16-21: last_evidence_status, last_evidence_checked_at_utc, last_evidence_strategy,
        // last_evidence_source_name, last_evidence_source_url, last_evidence_detail, last_evidence_technical_detail.
        var lastEvidence = new SourceEvidence(
            sourceName: reader.GetString(19),
            sourceUrl: new Uri(reader.GetString(20)),
            timeChecked: SqliteConvert.ReadDateTimeOffset(reader, 17),
            status: Enum.Parse<RetrievalStatus>(reader.GetString(16)),
            detail: reader.GetString(21),
            strategy: Enum.Parse<RetrievalStrategy>(reader.GetString(18)),
            technicalDetail: SqliteConvert.ReadNullableString(reader, 22));

        return new CatalogProductRecord(
            ProductKey: reader.GetString(0),
            Category: Enum.Parse<SourceCategory>(reader.GetString(1)),
            DisplayName: reader.GetString(2),
            LifecycleState: Enum.Parse<ProductLifecycleState>(reader.GetString(3)),
            FirstSeenAt: SqliteConvert.ReadDateTimeOffset(reader, 4),
            LastSeenAt: SqliteConvert.ReadDateTimeOffset(reader, 5),
            LastVerifiedAt: SqliteConvert.ReadNullableDateTimeOffset(reader, 6),
            ConsecutiveConfirmedAbsenceCount: reader.GetInt32(7),
            ArchivedAt: SqliteConvert.ReadNullableDateTimeOffset(reader, 8),
            ArchivedReason: SqliteConvert.ReadNullableString(reader, 9),
            CurrentPriceAmount: SqliteConvert.ReadNullableDecimal(reader, 10),
            CurrentDisplayPrice: SqliteConvert.ReadNullableString(reader, 11),
            ComparisonPriceAmount: SqliteConvert.ReadNullableDecimal(reader, 12),
            PriceQualifier: SqliteConvert.ReadNullableString(reader, 13),
            DateSpecific: SqliteConvert.ReadBool(reader, 14),
            ApplicableDate: SqliteConvert.ReadNullableDateOnly(reader, 15),
            LastEvidence: lastEvidence);
    }
}
