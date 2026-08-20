using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>SQLite-backed <see cref="ISourceStateRepository"/> over the <c>source_state</c> table (spec §6.2).</summary>
public sealed class SqliteSourceStateRepository(SqliteConnectionFactory connectionFactory) : ISourceStateRepository
{
    public async Task<SourceStateRecord?> GetAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT source_id, category, last_attempt_at_utc, last_success_at_utc,
                   next_due_at_utc, fingerprint, consecutive_failure_count
            FROM source_state
            WHERE source_id = $sourceId;
            """;
        command.Parameters.AddWithValue("$sourceId", sourceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task SaveAsync(SourceStateRecord state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO source_state (
                source_id, category, last_attempt_at_utc, last_success_at_utc,
                next_due_at_utc, fingerprint, consecutive_failure_count
            ) VALUES (
                $sourceId, $category, $lastAttemptAt, $lastSuccessAt,
                $nextDueAt, $fingerprint, $failureCount
            )
            ON CONFLICT(source_id) DO UPDATE SET
                category = excluded.category,
                last_attempt_at_utc = excluded.last_attempt_at_utc,
                last_success_at_utc = excluded.last_success_at_utc,
                next_due_at_utc = excluded.next_due_at_utc,
                fingerprint = excluded.fingerprint,
                consecutive_failure_count = excluded.consecutive_failure_count;
            """;
        command.Parameters.AddWithValue("$sourceId", state.SourceId);
        command.Parameters.AddWithValue("$category", state.Category.ToString());
        command.Parameters.AddWithValue("$lastAttemptAt", SqliteConvert.ToParameter(state.LastAttemptAt));
        command.Parameters.AddWithValue("$lastSuccessAt", SqliteConvert.ToParameter(state.LastSuccessAt));
        command.Parameters.AddWithValue("$nextDueAt", SqliteConvert.ToParameter(state.NextDueAt));
        command.Parameters.AddWithValue("$fingerprint", SqliteConvert.ToParameter(state.SourceFingerprint));
        command.Parameters.AddWithValue("$failureCount", state.ConsecutiveFailureCount);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SourceStateRecord Map(SqliteDataReader reader) => new(
        SourceId: reader.GetString(0),
        Category: Enum.Parse<SourceCategory>(reader.GetString(1)),
        LastAttemptAt: SqliteConvert.ReadNullableDateTimeOffset(reader, 2),
        LastSuccessAt: SqliteConvert.ReadNullableDateTimeOffset(reader, 3),
        NextDueAt: SqliteConvert.ReadNullableDateTimeOffset(reader, 4),
        SourceFingerprint: SqliteConvert.ReadNullableString(reader, 5),
        ConsecutiveFailureCount: reader.GetInt32(6));
}
