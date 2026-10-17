using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Catalog;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>
/// SQLite-backed <see cref="ISeasonPriceHistoryRepository"/> over <c>season_price_history</c>
/// (Phase 20A). Writes made through a <see cref="SqliteUnitOfWork"/> share its connection and its
/// <c>BEGIN IMMEDIATE</c> transaction, so the read-latest/decide/write sequence for one product is
/// serialized against the scheduled-refresh process and the interactive app alike; the
/// (product_key, sequence) unique key is the last line of defense against a duplicate interval.
/// </summary>
public sealed class SqliteSeasonPriceHistoryRepository(SqliteConnectionFactory connectionFactory) : ISeasonPriceHistoryRepository
{
    private const string SelectColumns =
        """
        SELECT product_key, sequence, amount, price_kind, display_price, price_qualifier, display_name,
               source_name, first_observed_at_utc, last_confirmed_at_utc, origin
        FROM season_price_history
        """;

    public async Task<SeasonPriceHistoryInterval?> GetLatestAsync(string productKey, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productKey);

        return await WithCommandAsync(unitOfWork, async command =>
        {
            command.CommandText = SelectColumns + " WHERE product_key = $productKey ORDER BY sequence DESC LIMIT 1;";
            command.Parameters.AddWithValue("$productKey", productKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertAsync(SeasonPriceHistoryInterval interval, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interval);

        await WithCommandAsync(unitOfWork, async command =>
        {
            ConfigureInsertCommand(command, interval);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ConfirmAsync(string productKey, int sequence, DateTimeOffset confirmedAt, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productKey);

        await WithCommandAsync(unitOfWork, async command =>
        {
            // Only ever moves forward, and only on the row named — older intervals are immutable.
            command.CommandText =
                """
                UPDATE season_price_history
                SET last_confirmed_at_utc = $confirmedAt
                WHERE product_key = $productKey AND sequence = $sequence AND last_confirmed_at_utc < $confirmedAt;
                """;
            command.Parameters.AddWithValue("$productKey", productKey);
            command.Parameters.AddWithValue("$sequence", sequence);
            command.Parameters.AddWithValue("$confirmedAt", SqliteConvert.ToText(confirmedAt));
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SeasonPriceHistoryInterval>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productKey);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " WHERE product_key = $productKey ORDER BY sequence;";
        command.Parameters.AddWithValue("$productKey", productKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SeasonPriceHistoryInterval>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    public async Task<IReadOnlySet<string>> GetTrackedProductKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT product_key FROM season_price_history;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public async Task<SeasonPriceHistorySummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        // A "change" is any interval after a product's first one.
        command.CommandText =
            """
            SELECT COUNT(DISTINCT product_key),
                   COUNT(*),
                   (SELECT MAX(first_observed_at_utc) FROM season_price_history WHERE sequence > 1)
            FROM season_price_history;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new SeasonPriceHistorySummary(
            reader.GetInt32(0),
            reader.GetInt32(1),
            SqliteConvert.ReadNullableDateTimeOffset(reader, 2));
    }

    internal static void ConfigureInsertCommand(SqliteCommand command, SeasonPriceHistoryInterval interval)
    {
        command.CommandText =
            """
            INSERT INTO season_price_history (
                product_key, sequence, amount, price_kind, display_price, price_qualifier, display_name,
                source_name, first_observed_at_utc, last_confirmed_at_utc, origin
            ) VALUES (
                $productKey, $sequence, $amount, $priceKind, $displayPrice, $qualifier, $displayName,
                $sourceName, $firstObserved, $lastConfirmed, $origin
            );
            """;
        command.Parameters.AddWithValue("$productKey", interval.ProductKey);
        command.Parameters.AddWithValue("$sequence", interval.Sequence);
        command.Parameters.AddWithValue("$amount", interval.Amount);
        command.Parameters.AddWithValue("$priceKind", interval.PriceKind.ToString());
        command.Parameters.AddWithValue("$displayPrice", SqliteConvert.ToParameter(interval.DisplayPrice));
        command.Parameters.AddWithValue("$qualifier", SqliteConvert.ToParameter(interval.PriceQualifier));
        command.Parameters.AddWithValue("$displayName", interval.DisplayName);
        command.Parameters.AddWithValue("$sourceName", interval.SourceName);
        command.Parameters.AddWithValue("$firstObserved", SqliteConvert.ToText(interval.FirstObservedAt));
        command.Parameters.AddWithValue("$lastConfirmed", SqliteConvert.ToText(interval.LastConfirmedAt));
        command.Parameters.AddWithValue("$origin", interval.Origin.ToString());
    }

    private static SeasonPriceHistoryInterval Read(SqliteDataReader reader) => new(
        ProductKey: reader.GetString(0),
        Sequence: reader.GetInt32(1),
        Amount: reader.GetDecimal(2),
        PriceKind: Enum.TryParse<PriceKind>(reader.GetString(3), out var kind) ? kind : PriceKind.Unknown,
        DisplayPrice: SqliteConvert.ReadNullableString(reader, 4),
        PriceQualifier: SqliteConvert.ReadNullableString(reader, 5),
        DisplayName: reader.GetString(6),
        SourceName: reader.GetString(7),
        FirstObservedAt: SqliteConvert.ReadDateTimeOffset(reader, 8),
        LastConfirmedAt: SqliteConvert.ReadDateTimeOffset(reader, 9),
        Origin: Enum.TryParse<SeasonPriceHistoryOrigin>(reader.GetString(10), out var origin) ? origin : SeasonPriceHistoryOrigin.Observed);

    private async Task<T> WithCommandAsync<T>(IUnitOfWork? unitOfWork, Func<SqliteCommand, Task<T>> execute, CancellationToken cancellationToken)
    {
        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            return await execute(sharedCommand).ConfigureAwait(false);
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        return await execute(command).ConfigureAwait(false);
    }
}
