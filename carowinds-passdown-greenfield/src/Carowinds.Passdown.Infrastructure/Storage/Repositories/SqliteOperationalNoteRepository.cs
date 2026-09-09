using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>
/// SQLite-backed <see cref="IOperationalNoteRepository"/> over the <c>operational_notes</c> table (spec
/// Phase 11A Part M). <see cref="UpdateAsync"/> is a plain <c>UPDATE ... WHERE id = ...</c> — unlike
/// <see cref="SqliteFinalizedPassdownRepository"/>'s insert-only contract, a persistent note is a
/// genuinely mutable row (spec Part B/P), but there is deliberately no delete method.
/// </summary>
public sealed class SqliteOperationalNoteRepository(SqliteConnectionFactory connectionFactory) : IOperationalNoteRepository
{
    private const string SelectColumns =
        "id, title, body, category, priority, effective_from, effective_through, status, created_at_utc, updated_at_utc, resolved_at_utc";

    public async Task<OperationalNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM operational_notes WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<OperationalNote>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM operational_notes ORDER BY created_at_utc DESC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<OperationalNote>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    /// <summary>
    /// Pushes the status/date-range filter into SQL so a caller asking "what's relevant today" never
    /// loads every resolved or out-of-range note (spec Part M). Callers still re-check
    /// <see cref="OperationalNoteRelevance.IsRelevantForDate"/> — see that type's remarks.
    /// </summary>
    public async Task<IReadOnlyList<OperationalNote>> ListRelevantForDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {SelectColumns} FROM operational_notes
            WHERE status = $status
              AND effective_from <= $operatingDate
              AND (effective_through IS NULL OR effective_through >= $operatingDate)
            ORDER BY created_at_utc ASC;
            """;
        command.Parameters.AddWithValue("$status", OperationalNoteStatus.Active.ToString());
        command.Parameters.AddWithValue("$operatingDate", SqliteConvert.ToText(operatingDate));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<OperationalNote>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task AddAsync(OperationalNote note, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);

        const string commandText =
            """
            INSERT INTO operational_notes (id, title, body, category, priority, effective_from, effective_through, status, created_at_utc, updated_at_utc, resolved_at_utc)
            VALUES ($id, $title, $body, $category, $priority, $effectiveFrom, $effectiveThrough, $status, $createdAt, $updatedAt, $resolvedAt);
            """;

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            sharedCommand.CommandText = commandText;
            BindParameters(sharedCommand, note);
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        BindParameters(command, note);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(OperationalNote note, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);

        const string commandText =
            """
            UPDATE operational_notes
            SET title = $title, body = $body, category = $category, priority = $priority,
                effective_from = $effectiveFrom, effective_through = $effectiveThrough, status = $status,
                created_at_utc = $createdAt, updated_at_utc = $updatedAt, resolved_at_utc = $resolvedAt
            WHERE id = $id;
            """;

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            sharedCommand.CommandText = commandText;
            BindParameters(sharedCommand, note);
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        BindParameters(command, note);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the note row for <paramref name="id"/>, if one exists (Phase 16D §13/§14) — the sync engine's tombstone primitive; no product delete action calls this today (§12).</summary>
    public async Task DeleteAsync(Guid id, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        const string commandText = "DELETE FROM operational_notes WHERE id = $id;";

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            sharedCommand.CommandText = commandText;
            sharedCommand.Parameters.AddWithValue("$id", id.ToString());
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void BindParameters(SqliteCommand command, OperationalNote note)
    {
        command.Parameters.AddWithValue("$id", note.Id.ToString());
        command.Parameters.AddWithValue("$title", note.Title);
        command.Parameters.AddWithValue("$body", (object?)note.Body ?? DBNull.Value);
        command.Parameters.AddWithValue("$category", note.Category.ToString());
        command.Parameters.AddWithValue("$priority", note.Priority.ToString());
        command.Parameters.AddWithValue("$effectiveFrom", SqliteConvert.ToText(note.EffectiveFrom));
        command.Parameters.AddWithValue("$effectiveThrough", note.EffectiveThrough is { } effectiveThrough ? SqliteConvert.ToText(effectiveThrough) : DBNull.Value);
        command.Parameters.AddWithValue("$status", note.Status.ToString());
        command.Parameters.AddWithValue("$createdAt", SqliteConvert.ToText(note.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAt", SqliteConvert.ToText(note.UpdatedAtUtc));
        command.Parameters.AddWithValue("$resolvedAt", note.ResolvedAtUtc is { } resolvedAt ? SqliteConvert.ToText(resolvedAt) : DBNull.Value);
    }

    private static OperationalNote Map(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        SqliteConvert.ReadNullableString(reader, 2),
        Enum.Parse<OperationalNoteCategory>(reader.GetString(3)),
        Enum.Parse<OperationalNotePriority>(reader.GetString(4)),
        SqliteConvert.ReadDateOnly(reader, 5),
        SqliteConvert.ReadNullableDateOnly(reader, 6),
        Enum.Parse<OperationalNoteStatus>(reader.GetString(7)),
        SqliteConvert.ReadDateTimeOffset(reader, 8),
        SqliteConvert.ReadDateTimeOffset(reader, 9),
        SqliteConvert.ReadNullableDateTimeOffset(reader, 10));
}
