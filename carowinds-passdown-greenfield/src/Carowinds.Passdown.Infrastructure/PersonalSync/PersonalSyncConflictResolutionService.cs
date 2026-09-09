using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// <see cref="IPersonalSyncConflictResolutionService"/> — every step of Phase 16D conflict review and
/// explicit resolution (§19): validation, persistence, revision/rebase changes, export, and conflict
/// clearing. No ViewModel contains any of this logic (§19); a ViewModel only calls this service and
/// renders what it returns.
/// </summary>
public sealed class PersonalSyncConflictResolutionService(
    IPersonalSyncSettingsService settingsService,
    IDeviceIdProvider deviceIdProvider,
    IAssociateDraftRepository draftRepository,
    IOperationalNoteRepository noteRepository,
    ISyncEntityStateRepository stateRepository,
    ISyncConflictRepository conflictRepository,
    IUnitOfWorkFactory unitOfWorkFactory,
    IClock clock) : IPersonalSyncConflictResolutionService
{
    private static readonly JsonSerializerOptions DraftPayloadWriteOptions = new();

    public async Task<IReadOnlyList<SyncConflictSummary>> GetConflictsAsync(CancellationToken cancellationToken = default)
    {
        var records = await conflictRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var summaries = new List<SyncConflictSummary>(records.Count);

        foreach (var record in records)
        {
            summaries.Add(await BuildSummaryAsync(record, cancellationToken).ConfigureAwait(false));
        }

        return summaries;
    }

    private async Task<SyncConflictSummary> BuildSummaryAsync(SyncConflictRecord record, CancellationToken cancellationToken)
    {
        bool isLocalDeletion;
        string displayLabel;

        if (record.EntityType == SyncEntityType.AssociateDraft)
        {
            SyncEntityIdentity.TryParseDraftId(record.EntityId, out var operatingDate);
            var local = await draftRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
            isLocalDeletion = local is null;
            displayLabel = $"{operatingDate.ToString("MMM d", CultureInfo.InvariantCulture)} Draft";
        }
        else
        {
            SyncEntityIdentity.TryParseNoteId(record.EntityId, out var noteId);
            var local = await noteRepository.GetByIdAsync(noteId, cancellationToken).ConfigureAwait(false);
            isLocalDeletion = local is null;
            var title = local?.Title ?? (record is { RemoteIsDeleted: false, RemotePayloadJson: not null }
                ? OperationalNoteSyncPayloadSerializer.Deserialize(record.RemotePayloadJson).Title
                : "Operational Note");
            displayLabel = $"Operational Note: \"{title}\"";
        }

        return new SyncConflictSummary(
            record.EntityType,
            record.EntityId,
            displayLabel,
            record.RemoteIsDeleted,
            isLocalDeletion,
            record.RemoteDeviceId,
            record.RemoteRevision,
            record.RemoteModifiedUtc,
            record.DetectedAtUtc,
            record.NewerRemoteDeviceId is not null);
    }

    public async Task<DraftConflictDetail?> GetDraftConflictDetailAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        var entityId = SyncEntityIdentity.ForDraft(operatingDate);
        var record = await conflictRepository.GetAsync(SyncEntityType.AssociateDraft, entityId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var local = await draftRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var localPayload = local is null ? null : AssociateDraftPayloadSerializer.Deserialize(local.PayloadJson);
        var remotePayload = record is { RemoteIsDeleted: false, RemotePayloadJson: not null }
            ? AssociateDraftPayloadSerializer.Deserialize(record.RemotePayloadJson)
            : null;

        return new DraftConflictDetail(
            operatingDate,
            localPayload,
            remotePayload,
            record.RemoteIsDeleted,
            local is null,
            record.RemoteDeviceId,
            record.RemoteModifiedUtc,
            record.NewerRemoteDeviceId is not null);
    }

    public async Task<NoteConflictDetail?> GetNoteConflictDetailAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var entityId = SyncEntityIdentity.ForNote(noteId);
        var record = await conflictRepository.GetAsync(SyncEntityType.OperationalNote, entityId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var local = await noteRepository.GetByIdAsync(noteId, cancellationToken).ConfigureAwait(false);
        var remotePayload = record is { RemoteIsDeleted: false, RemotePayloadJson: not null }
            ? OperationalNoteSyncPayloadSerializer.Deserialize(record.RemotePayloadJson)
            : null;

        return new NoteConflictDetail(
            noteId,
            local,
            remotePayload,
            record.RemoteIsDeleted,
            local is null,
            record.RemoteDeviceId,
            record.RemoteModifiedUtc,
            record.NewerRemoteDeviceId is not null);
    }

    public async Task<ConflictResolutionResult> ResolveKeepLocalAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
    {
        var conflict = await conflictRepository.GetAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);
        if (conflict is null)
        {
            return ConflictResolutionResult.NoConflictFound();
        }

        var snapshot = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Status != PersonalSyncStatus.Ready)
        {
            return ConflictResolutionResult.ExportUnavailable();
        }

        var root = snapshot.SyncRootPath!;
        var deviceId = deviceIdProvider.GetOrCreateDeviceId();
        var now = clock.UtcNow;
        var state = await stateRepository.GetAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);
        var newRevision = (state?.LocalRevision ?? 1) + 1;

        if (entityType == SyncEntityType.AssociateDraft)
        {
            SyncEntityIdentity.TryParseDraftId(entityId, out var operatingDate);
            var local = await draftRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);

            SyncEnvelope envelope;
            string newHash;
            if (local is not null)
            {
                var payload = AssociateDraftPayloadSerializer.Deserialize(local.PayloadJson);
                newHash = SyncContentHash.ComputeDraftHash(payload);
                envelope = new SyncEnvelope
                {
                    SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
                    EntityType = entityType,
                    EntityId = entityId,
                    DeviceId = deviceId,
                    Revision = newRevision,
                    ModifiedUtc = now,
                    IsDeleted = false,
                    PayloadSchemaVersion = local.SchemaVersion,
                    Payload = JsonSerializer.SerializeToElement(payload, DraftPayloadWriteOptions),
                };
            }
            else
            {
                newHash = SyncContentHash.TombstoneMarker;
                envelope = new SyncEnvelope
                {
                    SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
                    EntityType = entityType,
                    EntityId = entityId,
                    DeviceId = deviceId,
                    Revision = newRevision,
                    ModifiedUtc = now,
                    IsDeleted = true,
                    PayloadSchemaVersion = AssociateDraftPayload.CurrentSchemaVersion,
                    Payload = JsonSerializer.SerializeToElement<object?>(null),
                };
            }

            if (!await TryExportAsync(SyncPathService.GetDraftFilePath(root, operatingDate), envelope, cancellationToken).ConfigureAwait(false))
            {
                return ConflictResolutionResult.ExportUnavailable();
            }

            await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken).ConfigureAwait(false);
            await stateRepository.UpsertAsync(
                (state ?? SyncEntityState.CreateNew(entityType, entityId, now)) with
                {
                    LocalRevision = newRevision,
                    SyncedRevision = newRevision,
                    SyncedContentHash = newHash,
                    ConflictState = SyncConflictState.None,
                    ConflictRemoteDeviceId = null,
                    ConflictRemoteRevision = null,
                    ConflictRemoteContentHash = null,
                    ConflictDetectedAtUtc = null,
                    UpdatedAtUtc = now,
                },
                unitOfWork, cancellationToken).ConfigureAwait(false);
            await conflictRepository.DeleteAsync(entityType, entityId, unitOfWork, cancellationToken).ConfigureAwait(false);
            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ConflictResolutionResult.Resolved();
        }
        else
        {
            SyncEntityIdentity.TryParseNoteId(entityId, out var noteId);
            var local = await noteRepository.GetByIdAsync(noteId, cancellationToken).ConfigureAwait(false);

            SyncEnvelope envelope;
            string newHash;
            if (local is not null)
            {
                var payload = OperationalNoteSyncPayload.FromDomain(local);
                newHash = SyncContentHash.ComputeNoteHash(payload);
                envelope = new SyncEnvelope
                {
                    SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
                    EntityType = entityType,
                    EntityId = entityId,
                    DeviceId = deviceId,
                    Revision = newRevision,
                    ModifiedUtc = now,
                    IsDeleted = false,
                    PayloadSchemaVersion = OperationalNoteSyncPayload.CurrentSchemaVersion,
                    Payload = JsonSerializer.SerializeToElement(payload, OperationalNoteSyncPayloadSerializer.Options),
                };
            }
            else
            {
                newHash = SyncContentHash.TombstoneMarker;
                envelope = new SyncEnvelope
                {
                    SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
                    EntityType = entityType,
                    EntityId = entityId,
                    DeviceId = deviceId,
                    Revision = newRevision,
                    ModifiedUtc = now,
                    IsDeleted = true,
                    PayloadSchemaVersion = OperationalNoteSyncPayload.CurrentSchemaVersion,
                    Payload = JsonSerializer.SerializeToElement<object?>(null),
                };
            }

            if (!await TryExportAsync(SyncPathService.GetOperationalNoteFilePath(root, noteId), envelope, cancellationToken).ConfigureAwait(false))
            {
                return ConflictResolutionResult.ExportUnavailable();
            }

            await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken).ConfigureAwait(false);
            await stateRepository.UpsertAsync(
                (state ?? SyncEntityState.CreateNew(entityType, entityId, now)) with
                {
                    LocalRevision = newRevision,
                    SyncedRevision = newRevision,
                    SyncedContentHash = newHash,
                    ConflictState = SyncConflictState.None,
                    ConflictRemoteDeviceId = null,
                    ConflictRemoteRevision = null,
                    ConflictRemoteContentHash = null,
                    ConflictDetectedAtUtc = null,
                    UpdatedAtUtc = now,
                },
                unitOfWork, cancellationToken).ConfigureAwait(false);
            await conflictRepository.DeleteAsync(entityType, entityId, unitOfWork, cancellationToken).ConfigureAwait(false);
            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ConflictResolutionResult.Resolved();
        }
    }

    public async Task<ConflictResolutionResult> ResolveUseRemoteAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
    {
        var conflict = await conflictRepository.GetAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);
        if (conflict is null)
        {
            return ConflictResolutionResult.NoConflictFound();
        }

        if (conflict.RemoteIsDeleted)
        {
            return ConflictResolutionResult.WrongActionForConflict(
                "The other device deleted this item — use Accept Deletion instead of Use Other Device.");
        }

        var now = clock.UtcNow;
        var state = await stateRepository.GetAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);
        var newRevision = (state?.LocalRevision ?? 1) + 1;

        await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken).ConfigureAwait(false);

        if (entityType == SyncEntityType.AssociateDraft)
        {
            SyncEntityIdentity.TryParseDraftId(entityId, out var operatingDate);
            await draftRepository.SaveAsync(
                new AssociateDraft(operatingDate, conflict.RemotePayloadSchemaVersion ?? AssociateDraftPayload.CurrentSchemaVersion, conflict.RemotePayloadJson!, conflict.RemoteModifiedUtc),
                unitOfWork, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            SyncEntityIdentity.TryParseNoteId(entityId, out var noteId);
            var remotePayload = OperationalNoteSyncPayloadSerializer.Deserialize(conflict.RemotePayloadJson!);
            var localExists = await noteRepository.GetByIdAsync(noteId, cancellationToken).ConfigureAwait(false) is not null;
            if (localExists)
            {
                await noteRepository.UpdateAsync(remotePayload.ToDomain(), unitOfWork, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await noteRepository.AddAsync(remotePayload.ToDomain(), unitOfWork, cancellationToken).ConfigureAwait(false);
            }
        }

        await stateRepository.UpsertAsync(
            (state ?? SyncEntityState.CreateNew(entityType, entityId, now)) with
            {
                LocalRevision = newRevision,
                SyncedRevision = newRevision,
                SyncedContentHash = conflict.RemoteContentHash,
                LastRemoteDeviceId = conflict.RemoteDeviceId,
                LastRemoteRevision = conflict.RemoteRevision,
                ConflictState = SyncConflictState.None,
                ConflictRemoteDeviceId = null,
                ConflictRemoteRevision = null,
                ConflictRemoteContentHash = null,
                ConflictDetectedAtUtc = null,
                UpdatedAtUtc = now,
            },
            unitOfWork, cancellationToken).ConfigureAwait(false);
        await conflictRepository.DeleteAsync(entityType, entityId, unitOfWork, cancellationToken).ConfigureAwait(false);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ConflictResolutionResult.Resolved();
    }

    public async Task<ConflictResolutionResult> ResolveAcceptDeletionAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
    {
        var conflict = await conflictRepository.GetAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);
        if (conflict is null)
        {
            return ConflictResolutionResult.NoConflictFound();
        }

        if (!conflict.RemoteIsDeleted)
        {
            return ConflictResolutionResult.WrongActionForConflict(
                "The other device did not delete this item — use Use Other Device instead of Accept Deletion.");
        }

        var now = clock.UtcNow;
        var state = await stateRepository.GetAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);
        var newRevision = (state?.LocalRevision ?? 1) + 1;

        await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken).ConfigureAwait(false);

        if (entityType == SyncEntityType.AssociateDraft)
        {
            SyncEntityIdentity.TryParseDraftId(entityId, out var operatingDate);
            await draftRepository.DeleteAsync(operatingDate, unitOfWork, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            SyncEntityIdentity.TryParseNoteId(entityId, out var noteId);
            await noteRepository.DeleteAsync(noteId, unitOfWork, cancellationToken).ConfigureAwait(false);
        }

        await stateRepository.UpsertAsync(
            (state ?? SyncEntityState.CreateNew(entityType, entityId, now)) with
            {
                LocalRevision = newRevision,
                SyncedRevision = newRevision,
                SyncedContentHash = SyncContentHash.TombstoneMarker,
                LastRemoteDeviceId = conflict.RemoteDeviceId,
                LastRemoteRevision = conflict.RemoteRevision,
                ConflictState = SyncConflictState.None,
                ConflictRemoteDeviceId = null,
                ConflictRemoteRevision = null,
                ConflictRemoteContentHash = null,
                ConflictDetectedAtUtc = null,
                UpdatedAtUtc = now,
            },
            unitOfWork, cancellationToken).ConfigureAwait(false);
        await conflictRepository.DeleteAsync(entityType, entityId, unitOfWork, cancellationToken).ConfigureAwait(false);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ConflictResolutionResult.Resolved();
    }

    private static async Task<bool> TryExportAsync(string path, SyncEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            await AtomicFileWriter.WriteTextAsync(path, SyncEnvelopeSerializer.Serialize(envelope), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
