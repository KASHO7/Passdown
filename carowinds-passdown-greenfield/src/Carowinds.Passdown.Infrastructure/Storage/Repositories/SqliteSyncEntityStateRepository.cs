using Carowinds.Passdown.Application.PersonalSync;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.Storage.Repositories;

/// <summary>SQLite-backed <see cref="ISyncEntityStateRepository"/> over the <c>sync_entity_state</c> table (Phase 16C §5/§32).</summary>
public sealed class SqliteSyncEntityStateRepository(SqliteConnectionFactory connectionFactory) : ISyncEntityStateRepository
{
    private const string SelectColumns =
        "entity_type, entity_id, local_revision, synced_revision, synced_content_hash, last_remote_device_id, last_remote_revision, conflict_state, conflict_remote_device_id, conflict_remote_revision, conflict_remote_content_hash, conflict_detected_at_utc, updated_at_utc";

    public async Task<SyncEntityState?> GetAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM sync_entity_state WHERE entity_type = $entityType AND entity_id = $entityId;";
        command.Parameters.AddWithValue("$entityType", entityType.ToString());
        command.Parameters.AddWithValue("$entityId", entityId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<SyncEntityState>> ListByTypeAsync(SyncEntityType entityType, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM sync_entity_state WHERE entity_type = $entityType ORDER BY entity_id;";
        command.Parameters.AddWithValue("$entityType", entityType.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SyncEntityState>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpsertAsync(SyncEntityState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sync_entity_state (
                entity_type, entity_id, local_revision, synced_revision, synced_content_hash,
                last_remote_device_id, last_remote_revision, conflict_state, conflict_remote_device_id,
                conflict_remote_revision, conflict_remote_content_hash, conflict_detected_at_utc, updated_at_utc)
            VALUES (
                $entityType, $entityId, $localRevision, $syncedRevision, $syncedContentHash,
                $lastRemoteDeviceId, $lastRemoteRevision, $conflictState, $conflictRemoteDeviceId,
                $conflictRemoteRevision, $conflictRemoteContentHash, $conflictDetectedAtUtc, $updatedAtUtc)
            ON CONFLICT(entity_type, entity_id) DO UPDATE SET
                local_revision = excluded.local_revision,
                synced_revision = excluded.synced_revision,
                synced_content_hash = excluded.synced_content_hash,
                last_remote_device_id = excluded.last_remote_device_id,
                last_remote_revision = excluded.last_remote_revision,
                conflict_state = excluded.conflict_state,
                conflict_remote_device_id = excluded.conflict_remote_device_id,
                conflict_remote_revision = excluded.conflict_remote_revision,
                conflict_remote_content_hash = excluded.conflict_remote_content_hash,
                conflict_detected_at_utc = excluded.conflict_detected_at_utc,
                updated_at_utc = excluded.updated_at_utc;
            """;

        command.Parameters.AddWithValue("$entityType", state.EntityType.ToString());
        command.Parameters.AddWithValue("$entityId", state.EntityId);
        command.Parameters.AddWithValue("$localRevision", state.LocalRevision);
        command.Parameters.AddWithValue("$syncedRevision", state.SyncedRevision);
        command.Parameters.AddWithValue("$syncedContentHash", (object?)state.SyncedContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastRemoteDeviceId", state.LastRemoteDeviceId is { } lastDeviceId ? lastDeviceId.ToString() : DBNull.Value);
        command.Parameters.AddWithValue("$lastRemoteRevision", (object?)state.LastRemoteRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$conflictState", state.ConflictState.ToString());
        command.Parameters.AddWithValue("$conflictRemoteDeviceId", state.ConflictRemoteDeviceId is { } conflictDeviceId ? conflictDeviceId.ToString() : DBNull.Value);
        command.Parameters.AddWithValue("$conflictRemoteRevision", (object?)state.ConflictRemoteRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$conflictRemoteContentHash", (object?)state.ConflictRemoteContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$conflictDetectedAtUtc", state.ConflictDetectedAtUtc is { } detectedAt ? SqliteConvert.ToText(detectedAt) : DBNull.Value);
        command.Parameters.AddWithValue("$updatedAtUtc", SqliteConvert.ToText(state.UpdatedAtUtc));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SyncEntityState Map(SqliteDataReader reader) => new(
        EntityType: Enum.Parse<SyncEntityType>(reader.GetString(0)),
        EntityId: reader.GetString(1),
        LocalRevision: reader.GetInt32(2),
        SyncedRevision: reader.GetInt32(3),
        SyncedContentHash: SqliteConvert.ReadNullableString(reader, 4),
        LastRemoteDeviceId: SqliteConvert.ReadNullableString(reader, 5) is { } deviceIdText ? Guid.Parse(deviceIdText) : null,
        LastRemoteRevision: reader.IsDBNull(6) ? null : reader.GetInt32(6),
        ConflictState: Enum.Parse<SyncConflictState>(reader.GetString(7)),
        ConflictRemoteDeviceId: SqliteConvert.ReadNullableString(reader, 8) is { } conflictDeviceIdText ? Guid.Parse(conflictDeviceIdText) : null,
        ConflictRemoteRevision: reader.IsDBNull(9) ? null : reader.GetInt32(9),
        ConflictRemoteContentHash: SqliteConvert.ReadNullableString(reader, 10),
        ConflictDetectedAtUtc: SqliteConvert.ReadNullableDateTimeOffset(reader, 11),
        UpdatedAtUtc: SqliteConvert.ReadDateTimeOffset(reader, 12));
}
