using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// <see cref="IPersonalSyncService"/> — the one Phase 16C sync pass: import every valid Associate
/// draft/Operational Note file, detect (never auto-resolve) conflicts, then export local
/// pending/bootstrap changes (spec §23). Reuses <see cref="IPersonalSyncSettingsService"/> for
/// readiness/folder/manifest checks rather than re-implementing that logic (spec §1 "identical to
/// Phase 15/16B" when sync is off or not ready). All conflict/idempotence decisions are delegated to
/// the pure <see cref="SyncReconciliation.Decide"/> function; this type only does filesystem
/// enumeration/I-O and repository calls around it.
/// </summary>
public sealed class PersonalSyncService(
    IPersonalSyncSettingsService settingsService,
    IDeviceIdProvider deviceIdProvider,
    IAssociateDraftRepository draftRepository,
    IOperationalNoteRepository noteRepository,
    ISyncEntityStateRepository stateRepository,
    IClock clock) : IPersonalSyncService
{
    private static readonly JsonSerializerOptions DraftPayloadWriteOptions = new();

    /// <summary>
    /// Prevents two sync passes from running concurrently in this process (spec §27) — a plain local
    /// primitive, deliberately not <c>IScheduledRefreshLock</c> (that lock exists to serialize
    /// scheduled *retrieval* refreshes on one machine; cross-device simultaneity here is expected and
    /// handled by revision/conflict logic, not a shared lock).
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PersonalSyncResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return PersonalSyncResult.AlreadyRunning();
        }

        try
        {
            var snapshot = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!snapshot.PersonalSyncEnabled)
            {
                return PersonalSyncResult.Disabled();
            }

            if (snapshot.Status != PersonalSyncStatus.Ready)
            {
                return PersonalSyncResult.NotReady(snapshot.Status, snapshot.StatusDetail);
            }

            var root = snapshot.SyncRootPath!;
            var deviceId = deviceIdProvider.GetOrCreateDeviceId();
            var now = clock.UtcNow;
            var counters = new Counters();

            await ImportDraftsAsync(root, deviceId, now, counters, cancellationToken).ConfigureAwait(false);
            await ImportNotesAsync(root, deviceId, now, counters, cancellationToken).ConfigureAwait(false);
            await ExportDraftsAsync(root, deviceId, now, counters, cancellationToken).ConfigureAwait(false);
            await ExportNotesAsync(root, deviceId, now, counters, cancellationToken).ConfigureAwait(false);

            return counters.ToResult();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ----- Drafts: import -----

    private async Task ImportDraftsAsync(string root, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var directory = SyncPathService.GetDraftsDirectoryPath(root);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Only an exact yyyy-MM-dd.json filename is a canonical draft file — an OneDrive
            // conflict copy or any other stray file is left untouched and silently ignored (§28).
            if (!SyncEntityIdentity.TryParseDraftId(Path.GetFileNameWithoutExtension(file), out var operatingDate))
            {
                continue;
            }

            var readResult = await SyncFileReader.ReadJsonAsync<SyncEnvelope>(
                file,
                SyncEnvelopeSerializer.Options,
                envelope => envelope.SyncSchemaVersion <= SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
                cancellationToken).ConfigureAwait(false);

            switch (readResult.Status)
            {
                case SyncFileReadStatus.Success:
                    await ProcessDraftEnvelopeAsync(readResult.Value!, operatingDate, deviceId, now, counters, cancellationToken).ConfigureAwait(false);
                    break;

                case SyncFileReadStatus.Missing:
                    // Disappeared between the directory listing and the read — never inferred as a deletion (§29).
                    break;

                case SyncFileReadStatus.IoError:
                    counters.IoFailures++;
                    break;

                default: // Malformed, UnsupportedVersion
                    counters.Malformed++;
                    break;
            }
        }
    }

    private async Task ProcessDraftEnvelopeAsync(
        SyncEnvelope envelope, DateOnly operatingDate, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var entityId = SyncEntityIdentity.ForDraft(operatingDate);
        if (envelope.EntityType != SyncEntityType.AssociateDraft || !string.Equals(envelope.EntityId, entityId, StringComparison.Ordinal))
        {
            // Never trust the filename alone — an envelope claiming the wrong entity/date is rejected, not guessed at.
            counters.Malformed++;
            return;
        }

        AssociateDraftPayload remotePayload;
        try
        {
            remotePayload = AssociateDraftPayloadSerializer.Deserialize(envelope.Payload.GetRawText());
        }
        catch (JsonException)
        {
            counters.Malformed++;
            return;
        }

        var localDraft = await draftRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var state = await stateRepository.GetAsync(SyncEntityType.AssociateDraft, entityId, cancellationToken).ConfigureAwait(false);

        var remoteHash = SyncContentHash.ComputeDraftHash(remotePayload);
        var localHash = localDraft is null ? null : SyncContentHash.ComputeDraftHash(AssociateDraftPayloadSerializer.Deserialize(localDraft.PayloadJson));

        var decision = SyncReconciliation.Decide(deviceId, envelope, localDraft is not null, localHash, state, remoteHash);

        switch (decision)
        {
            case SyncImportDecision.Ignored:
                counters.Unchanged++;
                break;

            case SyncImportDecision.RemoteOnlyImport:
                await draftRepository.SaveAsync(
                    new AssociateDraft(operatingDate, envelope.PayloadSchemaVersion, JsonSerializer.Serialize(remotePayload, DraftPayloadWriteOptions), envelope.ModifiedUtc),
                    cancellationToken).ConfigureAwait(false);
                await stateRepository.UpsertAsync(
                    SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, entityId, now) with
                    {
                        SyncedRevision = 1,
                        SyncedContentHash = remoteHash,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                    },
                    cancellationToken).ConfigureAwait(false);
                counters.Imported++;
                break;

            case SyncImportDecision.Converged:
            {
                var baseRevision = state?.LocalRevision ?? 1;
                await stateRepository.UpsertAsync(
                    (state ?? SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, entityId, now)) with
                    {
                        LocalRevision = baseRevision,
                        SyncedRevision = baseRevision,
                        SyncedContentHash = remoteHash,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                        ConflictState = SyncConflictState.None,
                        ConflictRemoteDeviceId = null,
                        ConflictRemoteRevision = null,
                        ConflictRemoteContentHash = null,
                        ConflictDetectedAtUtc = null,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken).ConfigureAwait(false);
                counters.Unchanged++;
                break;
            }

            case SyncImportDecision.SafeRemoteUpdate:
            {
                var newRevision = (state?.LocalRevision ?? 1) + 1;
                await draftRepository.SaveAsync(
                    new AssociateDraft(operatingDate, envelope.PayloadSchemaVersion, JsonSerializer.Serialize(remotePayload, DraftPayloadWriteOptions), envelope.ModifiedUtc),
                    cancellationToken).ConfigureAwait(false);
                await stateRepository.UpsertAsync(
                    (state ?? SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, entityId, now)) with
                    {
                        LocalRevision = newRevision,
                        SyncedRevision = newRevision,
                        SyncedContentHash = remoteHash,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                        ConflictState = SyncConflictState.None,
                        ConflictRemoteDeviceId = null,
                        ConflictRemoteRevision = null,
                        ConflictRemoteContentHash = null,
                        ConflictDetectedAtUtc = null,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken).ConfigureAwait(false);
                counters.Imported++;
                break;
            }

            case SyncImportDecision.Conflict:
                await stateRepository.UpsertAsync(
                    (state ?? SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, entityId, now)) with
                    {
                        ConflictState = SyncConflictState.Conflicting,
                        ConflictRemoteDeviceId = envelope.DeviceId,
                        ConflictRemoteRevision = envelope.Revision,
                        ConflictRemoteContentHash = remoteHash,
                        ConflictDetectedAtUtc = now,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken).ConfigureAwait(false);
                counters.Conflicts++;
                break;
        }
    }

    // ----- Drafts: export -----

    private async Task ExportDraftsAsync(string root, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var dates = await draftRepository.ListSavedDraftDatesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var date in dates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entityId = SyncEntityIdentity.ForDraft(date);
            var state = await stateRepository.GetAsync(SyncEntityType.AssociateDraft, entityId, cancellationToken).ConfigureAwait(false);
            if (state?.ConflictState == SyncConflictState.Conflicting)
            {
                // Never export over an unresolved conflict — 16D owns resolution (§13).
                continue;
            }

            var draft = await draftRepository.GetAsync(date, cancellationToken).ConfigureAwait(false);
            if (draft is null)
            {
                continue;
            }

            var payload = AssociateDraftPayloadSerializer.Deserialize(draft.PayloadJson);
            var hash = SyncContentHash.ComputeDraftHash(payload);

            if (state is not null && string.Equals(state.SyncedContentHash, hash, StringComparison.Ordinal))
            {
                continue; // Already in sync — no unnecessary rewrite (§6/§15).
            }

            var revision = state?.LocalRevision ?? 1;
            var envelope = new SyncEnvelope
            {
                SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
                EntityType = SyncEntityType.AssociateDraft,
                EntityId = entityId,
                DeviceId = deviceId,
                Revision = revision,
                ModifiedUtc = draft.UpdatedAt,
                IsDeleted = false,
                PayloadSchemaVersion = draft.SchemaVersion,
                Payload = JsonSerializer.SerializeToElement(payload, DraftPayloadWriteOptions),
            };

            try
            {
                await AtomicFileWriter.WriteTextAsync(SyncPathService.GetDraftFilePath(root, date), SyncEnvelopeSerializer.Serialize(envelope), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                counters.IoFailures++;
                continue; // Left dirty (state unchanged) — retried next pass (§7/§30).
            }

            await stateRepository.UpsertAsync(
                (state ?? SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, entityId, now)) with
                {
                    LocalRevision = revision,
                    SyncedRevision = revision,
                    SyncedContentHash = hash,
                    UpdatedAtUtc = now,
                },
                cancellationToken).ConfigureAwait(false);
            counters.Exported++;
        }
    }

    // ----- Notes: import -----

    private async Task ImportNotesAsync(string root, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var directory = SyncPathService.GetNotesDirectoryPath(root);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SyncEntityIdentity.TryParseNoteId(Path.GetFileNameWithoutExtension(file), out var noteId))
            {
                continue; // Not a canonical <guid>.json filename — leave untouched (§28).
            }

            var readResult = await SyncFileReader.ReadJsonAsync<SyncEnvelope>(
                file,
                SyncEnvelopeSerializer.Options,
                envelope => envelope.SyncSchemaVersion <= SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
                cancellationToken).ConfigureAwait(false);

            switch (readResult.Status)
            {
                case SyncFileReadStatus.Success:
                    await ProcessNoteEnvelopeAsync(readResult.Value!, noteId, deviceId, now, counters, cancellationToken).ConfigureAwait(false);
                    break;

                case SyncFileReadStatus.Missing:
                    break;

                case SyncFileReadStatus.IoError:
                    counters.IoFailures++;
                    break;

                default:
                    counters.Malformed++;
                    break;
            }
        }
    }

    private async Task ProcessNoteEnvelopeAsync(
        SyncEnvelope envelope, Guid noteId, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var entityId = SyncEntityIdentity.ForNote(noteId);
        if (envelope.EntityType != SyncEntityType.OperationalNote || !string.Equals(envelope.EntityId, entityId, StringComparison.Ordinal))
        {
            counters.Malformed++;
            return;
        }

        OperationalNoteSyncPayload remotePayload;
        try
        {
            remotePayload = OperationalNoteSyncPayloadSerializer.Deserialize(envelope.Payload);
        }
        catch (JsonException)
        {
            counters.Malformed++;
            return;
        }

        var localNote = await noteRepository.GetByIdAsync(noteId, cancellationToken).ConfigureAwait(false);
        var state = await stateRepository.GetAsync(SyncEntityType.OperationalNote, entityId, cancellationToken).ConfigureAwait(false);

        var remoteHash = SyncContentHash.ComputeNoteHash(remotePayload);
        var localHash = localNote is null ? null : SyncContentHash.ComputeNoteHash(OperationalNoteSyncPayload.FromDomain(localNote));

        var decision = SyncReconciliation.Decide(deviceId, envelope, localNote is not null, localHash, state, remoteHash);

        switch (decision)
        {
            case SyncImportDecision.Ignored:
                counters.Unchanged++;
                break;

            case SyncImportDecision.RemoteOnlyImport:
                await noteRepository.AddAsync(remotePayload.ToDomain(), cancellationToken).ConfigureAwait(false);
                await stateRepository.UpsertAsync(
                    SyncEntityState.CreateNew(SyncEntityType.OperationalNote, entityId, now) with
                    {
                        SyncedRevision = 1,
                        SyncedContentHash = remoteHash,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                    },
                    cancellationToken).ConfigureAwait(false);
                counters.Imported++;
                break;

            case SyncImportDecision.Converged:
            {
                var baseRevision = state?.LocalRevision ?? 1;
                await stateRepository.UpsertAsync(
                    (state ?? SyncEntityState.CreateNew(SyncEntityType.OperationalNote, entityId, now)) with
                    {
                        LocalRevision = baseRevision,
                        SyncedRevision = baseRevision,
                        SyncedContentHash = remoteHash,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                        ConflictState = SyncConflictState.None,
                        ConflictRemoteDeviceId = null,
                        ConflictRemoteRevision = null,
                        ConflictRemoteContentHash = null,
                        ConflictDetectedAtUtc = null,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken).ConfigureAwait(false);
                counters.Unchanged++;
                break;
            }

            case SyncImportDecision.SafeRemoteUpdate:
            {
                var newRevision = (state?.LocalRevision ?? 1) + 1;
                await noteRepository.UpdateAsync(remotePayload.ToDomain(), cancellationToken).ConfigureAwait(false);
                await stateRepository.UpsertAsync(
                    (state ?? SyncEntityState.CreateNew(SyncEntityType.OperationalNote, entityId, now)) with
                    {
                        LocalRevision = newRevision,
                        SyncedRevision = newRevision,
                        SyncedContentHash = remoteHash,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                        ConflictState = SyncConflictState.None,
                        ConflictRemoteDeviceId = null,
                        ConflictRemoteRevision = null,
                        ConflictRemoteContentHash = null,
                        ConflictDetectedAtUtc = null,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken).ConfigureAwait(false);
                counters.Imported++;
                break;
            }

            case SyncImportDecision.Conflict:
                await stateRepository.UpsertAsync(
                    (state ?? SyncEntityState.CreateNew(SyncEntityType.OperationalNote, entityId, now)) with
                    {
                        ConflictState = SyncConflictState.Conflicting,
                        ConflictRemoteDeviceId = envelope.DeviceId,
                        ConflictRemoteRevision = envelope.Revision,
                        ConflictRemoteContentHash = remoteHash,
                        ConflictDetectedAtUtc = now,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken).ConfigureAwait(false);
                counters.Conflicts++;
                break;
        }
    }

    // ----- Notes: export -----

    private async Task ExportNotesAsync(string root, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var notes = await noteRepository.ListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var note in notes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entityId = SyncEntityIdentity.ForNote(note.Id);
            var state = await stateRepository.GetAsync(SyncEntityType.OperationalNote, entityId, cancellationToken).ConfigureAwait(false);
            if (state?.ConflictState == SyncConflictState.Conflicting)
            {
                continue;
            }

            var payload = OperationalNoteSyncPayload.FromDomain(note);
            var hash = SyncContentHash.ComputeNoteHash(payload);

            if (state is not null && string.Equals(state.SyncedContentHash, hash, StringComparison.Ordinal))
            {
                continue;
            }

            var revision = state?.LocalRevision ?? 1;
            var envelope = new SyncEnvelope
            {
                SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
                EntityType = SyncEntityType.OperationalNote,
                EntityId = entityId,
                DeviceId = deviceId,
                Revision = revision,
                ModifiedUtc = note.UpdatedAtUtc,
                IsDeleted = false,
                PayloadSchemaVersion = OperationalNoteSyncPayload.CurrentSchemaVersion,
                Payload = JsonSerializer.SerializeToElement(payload, OperationalNoteSyncPayloadSerializer.Options),
            };

            try
            {
                await AtomicFileWriter.WriteTextAsync(SyncPathService.GetOperationalNoteFilePath(root, note.Id), SyncEnvelopeSerializer.Serialize(envelope), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                counters.IoFailures++;
                continue;
            }

            await stateRepository.UpsertAsync(
                (state ?? SyncEntityState.CreateNew(SyncEntityType.OperationalNote, entityId, now)) with
                {
                    LocalRevision = revision,
                    SyncedRevision = revision,
                    SyncedContentHash = hash,
                    UpdatedAtUtc = now,
                },
                cancellationToken).ConfigureAwait(false);
            counters.Exported++;
        }
    }

    private sealed class Counters
    {
        public int Imported;
        public int Exported;
        public int Unchanged;
        public int Conflicts;
        public int Malformed;
        public int IoFailures;

        public PersonalSyncResult ToResult() => new(
            PersonalSyncOutcome.Completed, Imported, Exported, Unchanged, Conflicts, Malformed, IoFailures, null);
    }
}
