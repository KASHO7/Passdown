using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Retrieval;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>
/// Phase 31: SQLite store for <see cref="OfficialReferenceDocumentRecord"/>. A success writes both the
/// success and last-attempt columns; a failure writes only the last-attempt columns (an insert for a
/// never-successful source leaves the success columns null), so last-known-good data survives every
/// failed check.
/// </summary>
public sealed class SqliteOfficialReferenceDocumentRepository(SqliteConnectionFactory connectionFactory) : IOfficialReferenceDocumentRepository
{
    public async Task<OfficialReferenceDocumentRecord?> GetAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT source_id, success_checked_at_utc, success_source_url, success_detail, success_payload_json,
                   last_attempt_status, last_attempt_checked_at_utc, last_attempt_detail,
                   last_attempt_technical_detail, last_attempt_source_url
            FROM official_reference_documents
            WHERE source_id = $sourceId;
            """;
        command.Parameters.AddWithValue("$sourceId", sourceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var successUrl = SqliteConvert.ReadNullableString(reader, 2);
        return new OfficialReferenceDocumentRecord(
            SourceId: reader.GetString(0),
            SuccessCheckedAt: SqliteConvert.ReadNullableDateTimeOffset(reader, 1),
            SuccessSourceUrl: successUrl is null ? null : new Uri(successUrl),
            SuccessDetail: SqliteConvert.ReadNullableString(reader, 3),
            SuccessPayloadJson: SqliteConvert.ReadNullableString(reader, 4),
            LastAttemptStatus: Enum.TryParse<RetrievalStatus>(reader.GetString(5), out var status) ? status : RetrievalStatus.Error,
            LastAttemptCheckedAt: SqliteConvert.ReadDateTimeOffset(reader, 6),
            LastAttemptDetail: reader.GetString(7),
            LastAttemptTechnicalDetail: SqliteConvert.ReadNullableString(reader, 8),
            LastAttemptSourceUrl: new Uri(reader.GetString(9)));
    }

    public async Task SaveSuccessAsync(string sourceId, SourceEvidence evidence, string payloadJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(payloadJson);
        if (evidence.Status != RetrievalStatus.Verified)
        {
            throw new ArgumentException("Only a verified check can replace the last-known-good document.", nameof(evidence));
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO official_reference_documents (
                source_id, success_checked_at_utc, success_source_url, success_detail, success_payload_json,
                last_attempt_status, last_attempt_checked_at_utc, last_attempt_detail,
                last_attempt_technical_detail, last_attempt_source_url
            ) VALUES (
                $sourceId, $checkedAt, $url, $detail, $payload,
                $status, $checkedAt, $detail, $technical, $url
            )
            ON CONFLICT(source_id) DO UPDATE SET
                success_checked_at_utc = excluded.success_checked_at_utc,
                success_source_url = excluded.success_source_url,
                success_detail = excluded.success_detail,
                success_payload_json = excluded.success_payload_json,
                last_attempt_status = excluded.last_attempt_status,
                last_attempt_checked_at_utc = excluded.last_attempt_checked_at_utc,
                last_attempt_detail = excluded.last_attempt_detail,
                last_attempt_technical_detail = excluded.last_attempt_technical_detail,
                last_attempt_source_url = excluded.last_attempt_source_url;
            """;
        AddAttemptParameters(command, sourceId, evidence);
        command.Parameters.AddWithValue("$payload", payloadJson);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAttemptAsync(string sourceId, SourceEvidence evidence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(evidence);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO official_reference_documents (
                source_id, last_attempt_status, last_attempt_checked_at_utc, last_attempt_detail,
                last_attempt_technical_detail, last_attempt_source_url
            ) VALUES (
                $sourceId, $status, $checkedAt, $detail, $technical, $url
            )
            ON CONFLICT(source_id) DO UPDATE SET
                last_attempt_status = excluded.last_attempt_status,
                last_attempt_checked_at_utc = excluded.last_attempt_checked_at_utc,
                last_attempt_detail = excluded.last_attempt_detail,
                last_attempt_technical_detail = excluded.last_attempt_technical_detail,
                last_attempt_source_url = excluded.last_attempt_source_url;
            """;
        AddAttemptParameters(command, sourceId, evidence);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddAttemptParameters(SqliteCommand command, string sourceId, SourceEvidence evidence)
    {
        command.Parameters.AddWithValue("$sourceId", sourceId);
        command.Parameters.AddWithValue("$checkedAt", SqliteConvert.ToText(evidence.TimeChecked));
        command.Parameters.AddWithValue("$url", evidence.SourceUrl.ToString());
        command.Parameters.AddWithValue("$detail", evidence.Detail);
        command.Parameters.AddWithValue("$status", evidence.Status.ToString());
        command.Parameters.AddWithValue("$technical", SqliteConvert.ToParameter(evidence.TechnicalDetail));
    }
}
