using Carowinds.Passdown.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>SQLite-backed <see cref="IAssociateDraftRepository"/> over the <c>associate_drafts</c> table (spec §6.2).</summary>
public sealed class SqliteAssociateDraftRepository(SqliteConnectionFactory connectionFactory) : IAssociateDraftRepository
{
    public async Task<AssociateDraft?> GetAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT operating_date, schema_version, payload_json, updated_at_utc FROM associate_drafts WHERE operating_date = $operatingDate;";
        command.Parameters.AddWithValue("$operatingDate", SqliteConvert.ToText(operatingDate));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new AssociateDraft(
            OperatingDate: SqliteConvert.ReadDateOnly(reader, 0),
            SchemaVersion: reader.GetInt32(1),
            PayloadJson: reader.GetString(2),
            UpdatedAt: SqliteConvert.ReadDateTimeOffset(reader, 3));
    }

    public async Task SaveAsync(AssociateDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO associate_drafts (operating_date, schema_version, payload_json, updated_at_utc)
            VALUES ($operatingDate, $schemaVersion, $payloadJson, $updatedAt)
            ON CONFLICT(operating_date) DO UPDATE SET
                schema_version = excluded.schema_version,
                payload_json = excluded.payload_json,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$operatingDate", SqliteConvert.ToText(draft.OperatingDate));
        command.Parameters.AddWithValue("$schemaVersion", draft.SchemaVersion);
        command.Parameters.AddWithValue("$payloadJson", draft.PayloadJson);
        command.Parameters.AddWithValue("$updatedAt", SqliteConvert.ToText(draft.UpdatedAt));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AssociateDraft>> GetByDateRangeAsync(DateOnly startDate, DateOnly endDateInclusive, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT operating_date, schema_version, payload_json, updated_at_utc FROM associate_drafts WHERE operating_date BETWEEN $startDate AND $endDate ORDER BY operating_date;";
        command.Parameters.AddWithValue("$startDate", SqliteConvert.ToText(startDate));
        command.Parameters.AddWithValue("$endDate", SqliteConvert.ToText(endDateInclusive));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var drafts = new List<AssociateDraft>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            drafts.Add(new AssociateDraft(
                OperatingDate: SqliteConvert.ReadDateOnly(reader, 0),
                SchemaVersion: reader.GetInt32(1),
                PayloadJson: reader.GetString(2),
                UpdatedAt: SqliteConvert.ReadDateTimeOffset(reader, 3)));
        }

        return drafts;
    }

    public async Task<IReadOnlyList<DateOnly>> ListSavedDraftDatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT operating_date FROM associate_drafts ORDER BY operating_date ASC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var dates = new List<DateOnly>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            dates.Add(SqliteConvert.ReadDateOnly(reader, 0));
        }

        return dates;
    }
}
