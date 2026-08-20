using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Catalog;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>SQLite-backed <see cref="IProductHistoryRepository"/> over the append-only <c>product_history</c> table (spec §7.2/§7.3).</summary>
public sealed class SqliteProductHistoryRepository(SqliteConnectionFactory connectionFactory) : IProductHistoryRepository
{
    public async Task AppendAsync(ProductHistoryEntry entry, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            ConfigureAppendCommand(sharedCommand, entry);
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        ConfigureAppendCommand(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ConfigureAppendCommand(SqliteCommand command, ProductHistoryEntry entry)
    {
        command.CommandText =
            """
            INSERT INTO product_history (
                product_key, recorded_at_utc, display_name, lifecycle_state,
                current_price_amount, current_display_price, comparison_price_amount, price_qualifier, change_reason
            ) VALUES (
                $productKey, $recordedAt, $displayName, $lifecycleState,
                $priceAmount, $displayPrice, $comparisonPrice, $qualifier, $changeReason
            );
            """;
        command.Parameters.AddWithValue("$productKey", entry.ProductKey);
        command.Parameters.AddWithValue("$recordedAt", SqliteConvert.ToText(entry.RecordedAt));
        command.Parameters.AddWithValue("$displayName", entry.DisplayName);
        command.Parameters.AddWithValue("$lifecycleState", entry.LifecycleState.ToString());
        command.Parameters.AddWithValue("$priceAmount", SqliteConvert.ToParameter(entry.CurrentPriceAmount));
        command.Parameters.AddWithValue("$displayPrice", SqliteConvert.ToParameter(entry.CurrentDisplayPrice));
        command.Parameters.AddWithValue("$comparisonPrice", SqliteConvert.ToParameter(entry.ComparisonPriceAmount));
        command.Parameters.AddWithValue("$qualifier", SqliteConvert.ToParameter(entry.PriceQualifier));
        command.Parameters.AddWithValue("$changeReason", SqliteConvert.ToParameter(entry.ChangeReason));
    }

    public async Task<IReadOnlyList<ProductHistoryEntry>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productKey);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT product_key, recorded_at_utc, display_name, lifecycle_state,
                   current_price_amount, current_display_price, comparison_price_amount, price_qualifier, change_reason
            FROM product_history
            WHERE product_key = $productKey
            ORDER BY recorded_at_utc;
            """;
        command.Parameters.AddWithValue("$productKey", productKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<ProductHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ProductHistoryEntry(
                ProductKey: reader.GetString(0),
                RecordedAt: SqliteConvert.ReadDateTimeOffset(reader, 1),
                DisplayName: reader.GetString(2),
                LifecycleState: Enum.Parse<ProductLifecycleState>(reader.GetString(3)),
                CurrentPriceAmount: SqliteConvert.ReadNullableDecimal(reader, 4),
                CurrentDisplayPrice: SqliteConvert.ReadNullableString(reader, 5),
                ComparisonPriceAmount: SqliteConvert.ReadNullableDecimal(reader, 6),
                PriceQualifier: SqliteConvert.ReadNullableString(reader, 7),
                ChangeReason: SqliteConvert.ReadNullableString(reader, 8)));
        }

        return results;
    }
}
