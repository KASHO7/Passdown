using Carowinds.Passdown.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>SQLite-backed <see cref="IDeepRefreshStatusRepository"/> over the single-row <c>deep_refresh_status</c> table (Phase 13D Part U).</summary>
public sealed class SqliteDeepRefreshStatusRepository(SqliteConnectionFactory connectionFactory) : IDeepRefreshStatusRepository
{
    public async Task<DeepRefreshStatusRecord?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT last_attempt_at_utc, last_success_at_utc, outcome, coverage_through,
                   source_success_count, source_failure_count
            FROM deep_refresh_status
            WHERE id = 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task SaveAsync(DeepRefreshStatusRecord status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO deep_refresh_status (
                id, last_attempt_at_utc, last_success_at_utc, outcome, coverage_through,
                source_success_count, source_failure_count
            ) VALUES (
                1, $lastAttemptAt, $lastSuccessAt, $outcome, $coverageThrough,
                $successCount, $failureCount
            )
            ON CONFLICT(id) DO UPDATE SET
                last_attempt_at_utc = excluded.last_attempt_at_utc,
                last_success_at_utc = excluded.last_success_at_utc,
                outcome = excluded.outcome,
                coverage_through = excluded.coverage_through,
                source_success_count = excluded.source_success_count,
                source_failure_count = excluded.source_failure_count;
            """;
        command.Parameters.AddWithValue("$lastAttemptAt", SqliteConvert.ToParameter(status.LastAttemptAtUtc));
        command.Parameters.AddWithValue("$lastSuccessAt", SqliteConvert.ToParameter(status.LastSuccessAtUtc));
        command.Parameters.AddWithValue("$outcome", status.Outcome.ToString());
        command.Parameters.AddWithValue("$coverageThrough", SqliteConvert.ToParameter(status.CoverageThrough));
        command.Parameters.AddWithValue("$successCount", status.SourceSuccessCount);
        command.Parameters.AddWithValue("$failureCount", status.SourceFailureCount);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DeepRefreshStatusRecord Map(SqliteDataReader reader) => new(
        LastAttemptAtUtc: SqliteConvert.ReadNullableDateTimeOffset(reader, 0)!.Value,
        LastSuccessAtUtc: SqliteConvert.ReadNullableDateTimeOffset(reader, 1),
        Outcome: Enum.Parse<DeepRefreshOutcome>(reader.GetString(2)),
        CoverageThrough: SqliteConvert.ReadNullableDateOnly(reader, 3),
        SourceSuccessCount: reader.GetInt32(4),
        SourceFailureCount: reader.GetInt32(5));
}
