using Carowinds.Passdown.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>SQLite-backed <see cref="IManagerDraftRepository"/> over the <c>manager_drafts</c> table (spec §6.2).</summary>
public sealed class SqliteManagerDraftRepository(SqliteConnectionFactory connectionFactory) : IManagerDraftRepository
{
    public async Task<ManagerDraft?> GetAsync(DateOnly operatingDate, string shift, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shift);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT operating_date, shift, schema_version, payload_json, updated_at_utc " +
            "FROM manager_drafts WHERE operating_date = $operatingDate AND shift = $shift;";
        command.Parameters.AddWithValue("$operatingDate", SqliteConvert.ToText(operatingDate));
        command.Parameters.AddWithValue("$shift", shift);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new ManagerDraft(
            OperatingDate: SqliteConvert.ReadDateOnly(reader, 0),
            Shift: reader.GetString(1),
            SchemaVersion: reader.GetInt32(2),
            PayloadJson: reader.GetString(3),
            UpdatedAt: SqliteConvert.ReadDateTimeOffset(reader, 4));
    }

    public async Task SaveAsync(ManagerDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO manager_drafts (operating_date, shift, schema_version, payload_json, updated_at_utc)
            VALUES ($operatingDate, $shift, $schemaVersion, $payloadJson, $updatedAt)
            ON CONFLICT(operating_date, shift) DO UPDATE SET
                schema_version = excluded.schema_version,
                payload_json = excluded.payload_json,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$operatingDate", SqliteConvert.ToText(draft.OperatingDate));
        command.Parameters.AddWithValue("$shift", draft.Shift);
        command.Parameters.AddWithValue("$schemaVersion", draft.SchemaVersion);
        command.Parameters.AddWithValue("$payloadJson", draft.PayloadJson);
        command.Parameters.AddWithValue("$updatedAt", SqliteConvert.ToText(draft.UpdatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
