using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>
/// SQLite-backed <see cref="IFinalizedPassdownRepository"/> over the <c>finalized_passdowns</c> table
/// (spec Phase 9 Part L). <see cref="AddAsync"/> is a plain <c>INSERT</c> with no
/// <c>ON CONFLICT</c> clause — unlike <see cref="SqliteAssociateDraftRepository.SaveAsync"/>'s
/// upsert, a finalized passdown is insert-only (spec Part M/N); a second insert for an
/// already-finalized date is rejected by the table's unique index on <c>passdown_date</c>, surfacing
/// as a <see cref="SqliteException"/> rather than silently overwriting history.
/// </summary>
public sealed class SqliteFinalizedPassdownRepository(SqliteConnectionFactory connectionFactory) : IFinalizedPassdownRepository
{
    private const string SelectColumns =
        "id, passdown_date, finalized_at_utc, snapshot_schema_version, snapshot_json, content_hash, origin";

    public async Task<FinalizedPassdown?> GetByDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM finalized_passdowns WHERE passdown_date = $passdownDate;";
        command.Parameters.AddWithValue("$passdownDate", SqliteConvert.ToText(passdownDate));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task<bool> ExistsForDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM finalized_passdowns WHERE passdown_date = $passdownDate;";
        command.Parameters.AddWithValue("$passdownDate", SqliteConvert.ToText(passdownDate));

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count > 0;
    }

    public async Task AddAsync(FinalizedPassdown finalizedPassdown, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finalizedPassdown);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO finalized_passdowns (id, passdown_date, finalized_at_utc, snapshot_schema_version, snapshot_json, content_hash, origin)
            VALUES ($id, $passdownDate, $finalizedAt, $schemaVersion, $snapshotJson, $contentHash, $origin);
            """;
        command.Parameters.AddWithValue("$id", finalizedPassdown.Id.ToString());
        command.Parameters.AddWithValue("$passdownDate", SqliteConvert.ToText(finalizedPassdown.PassdownDate));
        command.Parameters.AddWithValue("$finalizedAt", SqliteConvert.ToText(finalizedPassdown.FinalizedAtUtc));
        command.Parameters.AddWithValue("$schemaVersion", finalizedPassdown.SnapshotSchemaVersion);
        command.Parameters.AddWithValue("$snapshotJson", finalizedPassdown.SnapshotJson);
        command.Parameters.AddWithValue("$contentHash", finalizedPassdown.ContentHash);
        command.Parameters.AddWithValue("$origin", finalizedPassdown.Origin.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FinalizedPassdownSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT passdown_date, finalized_at_utc, origin FROM finalized_passdowns ORDER BY passdown_date DESC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<FinalizedPassdownSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new FinalizedPassdownSummary(
                SqliteConvert.ReadDateOnly(reader, 0),
                SqliteConvert.ReadDateTimeOffset(reader, 1),
                Enum.Parse<FinalizationOrigin>(reader.GetString(2))));
        }

        return results;
    }

    private static FinalizedPassdown Map(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        SqliteConvert.ReadDateOnly(reader, 1),
        SqliteConvert.ReadDateTimeOffset(reader, 2),
        reader.GetInt32(3),
        reader.GetString(4),
        reader.GetString(5),
        Enum.Parse<FinalizationOrigin>(reader.GetString(6)));
}
