using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>
/// SQLite-backed <see cref="IDatedResultRepository"/> over the <c>dated_results</c>
/// table (spec §6.2). <see cref="RecordSuccessAsync"/> writes the same evidence into
/// both the success and latest-attempt columns; <see cref="RecordFailedAttemptAsync"/>
/// writes only the latest-attempt columns, so a prior successful value is never
/// overwritten by a later failure (spec §6.7) — the guarantee lives entirely in
/// which columns each statement's <c>ON CONFLICT ... DO UPDATE SET</c> clause touches.
/// </summary>
public sealed class SqliteDatedResultRepository(SqliteConnectionFactory connectionFactory) : IDatedResultRepository
{
    private const string SelectColumns =
        """
        category, requested_date,
        success_status, success_confirmed_date, success_checked_at_utc, success_strategy,
        success_detail, success_technical_detail, success_source_name, success_source_url, success_payload_json,
        last_attempt_status, last_attempt_confirmed_date, last_attempt_checked_at_utc, last_attempt_strategy,
        last_attempt_detail, last_attempt_technical_detail, last_attempt_source_name, last_attempt_source_url
        """;

    public async Task<DatedResultRecord?> GetAsync(DatedResultKey key, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM dated_results WHERE category = $category AND requested_date = $requestedDate;";
        command.Parameters.AddWithValue("$category", key.Category.ToString());
        command.Parameters.AddWithValue("$requestedDate", SqliteConvert.ToText(key.Date));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task<IReadOnlyList<DatedResultRecord>> GetByRequestedDateAsync(DateOnly requestedDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM dated_results WHERE requested_date = $requestedDate ORDER BY category;";
        command.Parameters.AddWithValue("$requestedDate", SqliteConvert.ToText(requestedDate));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<DatedResultRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<DatedResultRecord>> GetByCategoryAndDateRangeAsync(SourceCategory category, DateOnly startDate, DateOnly endDateInclusive, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM dated_results WHERE category = $category AND requested_date BETWEEN $startDate AND $endDate ORDER BY requested_date;";
        command.Parameters.AddWithValue("$category", category.ToString());
        command.Parameters.AddWithValue("$startDate", SqliteConvert.ToText(startDate));
        command.Parameters.AddWithValue("$endDate", SqliteConvert.ToText(endDateInclusive));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<DatedResultRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task RecordSuccessAsync(DatedResultKey key, SourceEvidence evidence, string? payloadJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        RequireMatchingKey(key, evidence);
        if (!evidence.Status.IsSuccessfulCompletion())
        {
            throw new ArgumentException(
                $"RecordSuccessAsync requires a successful completion status; got '{evidence.Status}'. " +
                "Use RecordFailedAttemptAsync for a failed attempt.",
                nameof(evidence));
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO dated_results (
                category, requested_date,
                success_status, success_confirmed_date, success_checked_at_utc, success_strategy,
                success_detail, success_technical_detail, success_source_name, success_source_url, success_payload_json,
                last_attempt_status, last_attempt_confirmed_date, last_attempt_checked_at_utc, last_attempt_strategy,
                last_attempt_detail, last_attempt_technical_detail, last_attempt_source_name, last_attempt_source_url
            ) VALUES (
                $category, $requestedDate,
                $status, $confirmedDate, $checkedAt, $strategy, $detail, $technicalDetail, $sourceName, $sourceUrl, $payloadJson,
                $status, $confirmedDate, $checkedAt, $strategy, $detail, $technicalDetail, $sourceName, $sourceUrl
            )
            ON CONFLICT(category, requested_date) DO UPDATE SET
                success_status = excluded.success_status,
                success_confirmed_date = excluded.success_confirmed_date,
                success_checked_at_utc = excluded.success_checked_at_utc,
                success_strategy = excluded.success_strategy,
                success_detail = excluded.success_detail,
                success_technical_detail = excluded.success_technical_detail,
                success_source_name = excluded.success_source_name,
                success_source_url = excluded.success_source_url,
                success_payload_json = excluded.success_payload_json,
                last_attempt_status = excluded.last_attempt_status,
                last_attempt_confirmed_date = excluded.last_attempt_confirmed_date,
                last_attempt_checked_at_utc = excluded.last_attempt_checked_at_utc,
                last_attempt_strategy = excluded.last_attempt_strategy,
                last_attempt_detail = excluded.last_attempt_detail,
                last_attempt_technical_detail = excluded.last_attempt_technical_detail,
                last_attempt_source_name = excluded.last_attempt_source_name,
                last_attempt_source_url = excluded.last_attempt_source_url;
            """;

        AddKeyParameters(command, key);
        AddEvidenceParameters(command, evidence);
        command.Parameters.AddWithValue("$payloadJson", SqliteConvert.ToParameter(payloadJson));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordFailedAttemptAsync(DatedResultKey key, SourceEvidence evidence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        RequireMatchingKey(key, evidence);
        if (evidence.Status.IsSuccessfulCompletion())
        {
            throw new ArgumentException(
                $"RecordFailedAttemptAsync requires a non-successful status; got '{evidence.Status}'. " +
                "Use RecordSuccessAsync for a successful completion.",
                nameof(evidence));
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO dated_results (
                category, requested_date,
                last_attempt_status, last_attempt_confirmed_date, last_attempt_checked_at_utc, last_attempt_strategy,
                last_attempt_detail, last_attempt_technical_detail, last_attempt_source_name, last_attempt_source_url
            ) VALUES (
                $category, $requestedDate,
                $status, $confirmedDate, $checkedAt, $strategy, $detail, $technicalDetail, $sourceName, $sourceUrl
            )
            ON CONFLICT(category, requested_date) DO UPDATE SET
                last_attempt_status = excluded.last_attempt_status,
                last_attempt_confirmed_date = excluded.last_attempt_confirmed_date,
                last_attempt_checked_at_utc = excluded.last_attempt_checked_at_utc,
                last_attempt_strategy = excluded.last_attempt_strategy,
                last_attempt_detail = excluded.last_attempt_detail,
                last_attempt_technical_detail = excluded.last_attempt_technical_detail,
                last_attempt_source_name = excluded.last_attempt_source_name,
                last_attempt_source_url = excluded.last_attempt_source_url;
            """;

        AddKeyParameters(command, key);
        AddEvidenceParameters(command, evidence);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void RequireMatchingKey(DatedResultKey key, SourceEvidence evidence)
    {
        if (evidence.RequestedDate is { } requestedDate && requestedDate != key.Date)
        {
            throw new ArgumentException(
                $"Evidence's requested date ({requestedDate:yyyy-MM-dd}) does not match the dated result key's date ({key.Date:yyyy-MM-dd}).",
                nameof(evidence));
        }
    }

    private static void AddKeyParameters(SqliteCommand command, DatedResultKey key)
    {
        command.Parameters.AddWithValue("$category", key.Category.ToString());
        command.Parameters.AddWithValue("$requestedDate", SqliteConvert.ToText(key.Date));
    }

    private static void AddEvidenceParameters(SqliteCommand command, SourceEvidence evidence)
    {
        command.Parameters.AddWithValue("$status", evidence.Status.ToString());
        command.Parameters.AddWithValue("$confirmedDate", SqliteConvert.ToParameter(evidence.ConfirmedDate));
        command.Parameters.AddWithValue("$checkedAt", SqliteConvert.ToText(evidence.TimeChecked));
        command.Parameters.AddWithValue("$strategy", evidence.Strategy.ToString());
        command.Parameters.AddWithValue("$detail", evidence.Detail);
        command.Parameters.AddWithValue("$technicalDetail", SqliteConvert.ToParameter(evidence.TechnicalDetail));
        command.Parameters.AddWithValue("$sourceName", evidence.SourceName);
        command.Parameters.AddWithValue("$sourceUrl", evidence.SourceUrl.ToString());
    }

    private static DatedResultRecord Map(SqliteDataReader reader)
    {
        var category = Enum.Parse<SourceCategory>(reader.GetString(0));
        var requestedDate = SqliteConvert.ReadDateOnly(reader, 1);
        var key = new DatedResultKey(category, requestedDate);

        SourceEvidence? successEvidence = reader.IsDBNull(2)
            ? null
            : new SourceEvidence(
                sourceName: reader.GetString(8),
                sourceUrl: new Uri(reader.GetString(9)),
                timeChecked: SqliteConvert.ReadDateTimeOffset(reader, 4),
                status: Enum.Parse<RetrievalStatus>(reader.GetString(2)),
                detail: reader.GetString(6),
                strategy: Enum.Parse<RetrievalStrategy>(reader.GetString(5)),
                requestedDate: requestedDate,
                confirmedDate: SqliteConvert.ReadNullableDateOnly(reader, 3),
                technicalDetail: SqliteConvert.ReadNullableString(reader, 7));

        var successPayloadJson = SqliteConvert.ReadNullableString(reader, 10);

        var lastAttemptEvidence = new SourceEvidence(
            sourceName: reader.GetString(17),
            sourceUrl: new Uri(reader.GetString(18)),
            timeChecked: SqliteConvert.ReadDateTimeOffset(reader, 13),
            status: Enum.Parse<RetrievalStatus>(reader.GetString(11)),
            detail: reader.GetString(15),
            strategy: Enum.Parse<RetrievalStrategy>(reader.GetString(14)),
            requestedDate: requestedDate,
            confirmedDate: SqliteConvert.ReadNullableDateOnly(reader, 12),
            technicalDetail: SqliteConvert.ReadNullableString(reader, 16));

        return new DatedResultRecord(key, successEvidence, successPayloadJson, lastAttemptEvidence);
    }
}
