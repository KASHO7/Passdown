using System.Globalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain.Catalog;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>
/// SQLite-backed <see cref="ISyncedPriceObservationRepository"/> over <c>synced_price_observations</c>
/// (Phase 26). Merge-only by construction: the only statements are an insert of an unknown identity
/// and a forward-only <c>last_confirmed_at_utc</c> update of a known one — there is no delete. A whole
/// batch runs inside one <c>BEGIN IMMEDIATE</c> transaction, so a failure part-way leaves the table
/// exactly as it was.
/// </summary>
public sealed class SqliteSyncedPriceObservationRepository(SqliteConnectionFactory connectionFactory) : ISyncedPriceObservationRepository
{
    private const string SelectColumns =
        """
        SELECT observation_id, contributor_device_id, contributor_sequence, product_key, amount, price_kind,
               display_price, price_qualifier, display_name, source_name, first_observed_at_utc,
               last_confirmed_at_utc, origin, currency, billing_unit, imported_at_utc
        FROM synced_price_observations
        """;

    /// <summary>Test-only fault seam: invoked after each row written inside the merge transaction.</summary>
    internal Action<int>? AfterRowWrittenForTests { get; set; }

    public async Task<SyncedPriceObservationMergeResult> MergeAsync(
        IReadOnlyList<OfficialPriceObservation> observations,
        DateTimeOffset importedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
        {
            return SyncedPriceObservationMergeResult.Empty;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using (var begin = connection.CreateCommand())
        {
            // IMMEDIATE takes the write lock up front, like SqliteUnitOfWork, so read-decide-write is serialized.
            begin.CommandText = "BEGIN IMMEDIATE;";
            await begin.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            int added = 0, extended = 0, unchanged = 0, conflicts = 0, written = 0;
            foreach (var observation in observations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stored = await GetByIdAsync(connection, observation.ObservationId, cancellationToken).ConfigureAwait(false);
                if (stored is null)
                {
                    await InsertAsync(connection, observation, importedAtUtc, cancellationToken).ConfigureAwait(false);
                    added++;
                    AfterRowWrittenForTests?.Invoke(++written);
                    continue;
                }

                if (!stored.Observation.HasSameImmutableContent(observation))
                {
                    conflicts++; // keep the validated stored record; never overwrite either side
                    continue;
                }

                if (observation.LastConfirmedUtc > stored.Observation.LastConfirmedUtc)
                {
                    await ExtendAsync(connection, observation, cancellationToken).ConfigureAwait(false);
                    extended++;
                    AfterRowWrittenForTests?.Invoke(++written);
                    continue;
                }

                unchanged++;
            }

            using (var commit = connection.CreateCommand())
            {
                commit.CommandText = "COMMIT;";
                await commit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return new SyncedPriceObservationMergeResult(added, extended, unchanged, conflicts);
        }
        catch
        {
            using var rollback = connection.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            rollback.ExecuteNonQuery();
            throw;
        }
    }

    public async Task<IReadOnlyList<SyncedPriceObservation>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " ORDER BY product_key, first_observed_at_utc, observation_id;";
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SyncedPriceObservation>> GetForProductAsync(string productKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productKey);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " WHERE product_key = $productKey ORDER BY first_observed_at_utc, observation_id;";
        command.Parameters.AddWithValue("$productKey", productKey);
        return await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM synced_price_observations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<SyncedPriceObservation?> GetByIdAsync(SqliteConnection connection, string observationId, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " WHERE observation_id = $id;";
        command.Parameters.AddWithValue("$id", observationId);
        var rows = await ReadAllAsync(command, cancellationToken).ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task InsertAsync(SqliteConnection connection, OfficialPriceObservation o, DateTimeOffset importedAtUtc, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO synced_price_observations (
                observation_id, contributor_device_id, contributor_sequence, product_key, amount, price_kind,
                display_price, price_qualifier, display_name, source_name, first_observed_at_utc,
                last_confirmed_at_utc, origin, currency, billing_unit, imported_at_utc
            ) VALUES (
                $id, $contributor, $sequence, $productKey, $amount, $priceKind,
                $displayPrice, $qualifier, $displayName, $sourceName, $firstObserved,
                $lastConfirmed, $origin, $currency, $billingUnit, $importedAt
            );
            """;
        command.Parameters.AddWithValue("$id", o.ObservationId);
        command.Parameters.AddWithValue("$contributor", o.ContributorDeviceId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sequence", o.ContributorSequence);
        command.Parameters.AddWithValue("$productKey", o.ProductKey);
        command.Parameters.AddWithValue("$amount", OfficialPriceObservationIdentity.FormatAmount(o.Amount));
        command.Parameters.AddWithValue("$priceKind", o.PriceKind.ToString());
        command.Parameters.AddWithValue("$displayPrice", SqliteConvert.ToParameter(o.DisplayPrice));
        command.Parameters.AddWithValue("$qualifier", SqliteConvert.ToParameter(o.PriceQualifier));
        command.Parameters.AddWithValue("$displayName", o.DisplayName);
        command.Parameters.AddWithValue("$sourceName", o.SourceName);
        command.Parameters.AddWithValue("$firstObserved", SqliteConvert.ToText(o.FirstObservedUtc));
        command.Parameters.AddWithValue("$lastConfirmed", SqliteConvert.ToText(o.LastConfirmedUtc));
        command.Parameters.AddWithValue("$origin", o.Origin.ToString());
        command.Parameters.AddWithValue("$currency", o.Currency);
        command.Parameters.AddWithValue("$billingUnit", o.BillingUnit.ToString());
        command.Parameters.AddWithValue("$importedAt", SqliteConvert.ToText(importedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExtendAsync(SqliteConnection connection, OfficialPriceObservation o, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        // Forward-only, and only the one mutable field — every other column is the contributor's original.
        command.CommandText =
            """
            UPDATE synced_price_observations
            SET last_confirmed_at_utc = $lastConfirmed
            WHERE observation_id = $id AND last_confirmed_at_utc < $lastConfirmed;
            """;
        command.Parameters.AddWithValue("$id", o.ObservationId);
        command.Parameters.AddWithValue("$lastConfirmed", SqliteConvert.ToText(o.LastConfirmedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SyncedPriceObservation>> ReadAllAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<SyncedPriceObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (TryRead(reader) is { } row)
            {
                results.Add(row);
            }
        }

        return results;
    }

    /// <summary>A row this build cannot interpret (e.g. an enum value from a newer build) is skipped, never rewritten.</summary>
    private static SyncedPriceObservation? TryRead(SqliteDataReader reader)
    {
        if (!Guid.TryParseExact(reader.GetString(1), "D", out var contributor)
            || !decimal.TryParse(reader.GetString(4), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || !Enum.TryParse<PriceKind>(reader.GetString(5), out var kind)
            || !Enum.TryParse<SeasonPriceHistoryOrigin>(reader.GetString(12), out var origin)
            || !Enum.TryParse<PassPriceUnit>(reader.GetString(14), out var unit))
        {
            return null;
        }

        var observation = new OfficialPriceObservation(
            ObservationId: reader.GetString(0),
            ContributorDeviceId: contributor,
            ContributorSequence: reader.GetInt32(2),
            ProductKey: reader.GetString(3),
            Amount: amount,
            PriceKind: kind,
            DisplayPrice: SqliteConvert.ReadNullableString(reader, 6),
            PriceQualifier: SqliteConvert.ReadNullableString(reader, 7),
            DisplayName: reader.GetString(8),
            SourceName: reader.GetString(9),
            FirstObservedUtc: SqliteConvert.ReadDateTimeOffset(reader, 10),
            LastConfirmedUtc: SqliteConvert.ReadDateTimeOffset(reader, 11),
            Origin: origin,
            Currency: reader.GetString(13),
            BillingUnit: unit);
        return new SyncedPriceObservation(observation, SqliteConvert.ReadDateTimeOffset(reader, 15));
    }
}
