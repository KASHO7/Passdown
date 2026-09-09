using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>SQLite-backed <see cref="ISyncConflictRepository"/> over the <c>sync_conflicts</c> table (Phase 16D §2/§31).</summary>
public sealed class SqliteSyncConflictRepository(SqliteConnectionFactory connectionFactory) : ISyncConflictRepository
{
    private const string SelectColumns =
        "entity_type, entity_id, remote_device_id, remote_revision, remote_modified_at_utc, remote_is_deleted, remote_payload_schema_version, remote_payload_json, remote_content_hash, detected_at_utc, newer_remote_device_id, newer_remote_revision, newer_remote_detected_at_utc";

    public async Task<SyncConflictRecord?> GetAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM sync_conflicts WHERE entity_type = $entityType AND entity_id = $entityId;";
        command.Parameters.AddWithValue("$entityType", entityType.ToString());
        command.Parameters.AddWithValue("$entityId", entityId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<SyncConflictRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM sync_conflicts ORDER BY detected_at_utc ASC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SyncConflictRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpsertAsync(SyncConflictRecord record, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            ConfigureUpsertCommand(sharedCommand, record);
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        ConfigureUpsertCommand(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(SyncEntityType entityType, string entityId, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        if (unitOfWork is SqliteUnitOfWork sqliteUnitOfWork)
        {
            using var sharedCommand = sqliteUnitOfWork.Connection.CreateCommand();
            sharedCommand.Transaction = sqliteUnitOfWork.Transaction;
            ConfigureDeleteCommand(sharedCommand, entityType, entityId);
            await sharedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        ConfigureDeleteCommand(command, entityType, entityId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ConfigureDeleteCommand(SqliteCommand command, SyncEntityType entityType, string entityId)
    {
        command.CommandText = "DELETE FROM sync_conflicts WHERE entity_type = $entityType AND entity_id = $entityId;";
        command.Parameters.AddWithValue("$entityType", entityType.ToString());
        command.Parameters.AddWithValue("$entityId", entityId);
    }

    private static void ConfigureUpsertCommand(SqliteCommand command, SyncConflictRecord record)
    {
        command.CommandText =
            """
            INSERT INTO sync_conflicts (
                entity_type, entity_id, remote_device_id, remote_revision, remote_modified_at_utc,
                remote_is_deleted, remote_payload_schema_version, remote_payload_json, remote_content_hash,
                detected_at_utc, newer_remote_device_id, newer_remote_revision, newer_remote_detected_at_utc)
            VALUES (
                $entityType, $entityId, $remoteDeviceId, $remoteRevision, $remoteModifiedAtUtc,
                $remoteIsDeleted, $remotePayloadSchemaVersion, $remotePayloadJson, $remoteContentHash,
                $detectedAtUtc, $newerRemoteDeviceId, $newerRemoteRevision, $newerRemoteDetectedAtUtc)
            ON CONFLICT(entity_type, entity_id) DO UPDATE SET
                remote_device_id = excluded.remote_device_id,
                remote_revision = excluded.remote_revision,
                remote_modified_at_utc = excluded.remote_modified_at_utc,
                remote_is_deleted = excluded.remote_is_deleted,
                remote_payload_schema_version = excluded.remote_payload_schema_version,
                remote_payload_json = excluded.remote_payload_json,
                remote_content_hash = excluded.remote_content_hash,
                detected_at_utc = excluded.detected_at_utc,
                newer_remote_device_id = excluded.newer_remote_device_id,
                newer_remote_revision = excluded.newer_remote_revision,
                newer_remote_detected_at_utc = excluded.newer_remote_detected_at_utc;
            """;

        command.Parameters.AddWithValue("$entityType", record.EntityType.ToString());
        command.Parameters.AddWithValue("$entityId", record.EntityId);
        command.Parameters.AddWithValue("$remoteDeviceId", record.RemoteDeviceId.ToString());
        command.Parameters.AddWithValue("$remoteRevision", record.RemoteRevision);
        command.Parameters.AddWithValue("$remoteModifiedAtUtc", SqliteConvert.ToText(record.RemoteModifiedUtc));
        command.Parameters.AddWithValue("$remoteIsDeleted", record.RemoteIsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$remotePayloadSchemaVersion", (object?)record.RemotePayloadSchemaVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$remotePayloadJson", (object?)record.RemotePayloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$remoteContentHash", (object?)record.RemoteContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$detectedAtUtc", SqliteConvert.ToText(record.DetectedAtUtc));
        command.Parameters.AddWithValue("$newerRemoteDeviceId", record.NewerRemoteDeviceId is { } newerDeviceId ? newerDeviceId.ToString() : DBNull.Value);
        command.Parameters.AddWithValue("$newerRemoteRevision", (object?)record.NewerRemoteRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$newerRemoteDetectedAtUtc", record.NewerRemoteDetectedAtUtc is { } newerDetectedAt ? SqliteConvert.ToText(newerDetectedAt) : DBNull.Value);
    }

    private static SyncConflictRecord Map(SqliteDataReader reader) => new(
        EntityType: Enum.Parse<SyncEntityType>(reader.GetString(0)),
        EntityId: reader.GetString(1),
        RemoteDeviceId: Guid.Parse(reader.GetString(2)),
        RemoteRevision: reader.GetInt32(3),
        RemoteModifiedUtc: SqliteConvert.ReadDateTimeOffset(reader, 4),
        RemoteIsDeleted: reader.GetInt32(5) != 0,
        RemotePayloadSchemaVersion: reader.IsDBNull(6) ? null : reader.GetInt32(6),
        RemotePayloadJson: SqliteConvert.ReadNullableString(reader, 7),
        RemoteContentHash: SqliteConvert.ReadNullableString(reader, 8),
        DetectedAtUtc: SqliteConvert.ReadDateTimeOffset(reader, 9),
        NewerRemoteDeviceId: SqliteConvert.ReadNullableString(reader, 10) is { } newerDeviceIdText ? Guid.Parse(newerDeviceIdText) : null,
        NewerRemoteRevision: reader.IsDBNull(11) ? null : reader.GetInt32(11),
        NewerRemoteDetectedAtUtc: SqliteConvert.ReadNullableDateTimeOffset(reader, 12));
}
