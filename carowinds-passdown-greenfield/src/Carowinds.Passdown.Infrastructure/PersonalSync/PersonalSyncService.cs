using System.Security.Cryptography;
using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Time;
using Microsoft.Data.Sqlite;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// <see cref="IPersonalSyncService"/> — the one Phase 16C/16D/16E sync pass: import every valid
/// Associate draft/Operational Note file (including tombstones, Phase 16D §11-§18) and every valid
/// finalized History/PDF pair (Phase 16E §11-§19), detect (never auto-resolve) conflicts — persisting
/// the full conflicting remote envelope so resolution UI can act on it after a restart (§2) — then
/// export local pending/bootstrap changes, including any pending local deletion (§13) and any
/// never-synced finalized passdown (§10/§22). Reuses <see cref="IPersonalSyncSettingsService"/> for
/// readiness/folder/manifest checks rather than re-implementing that logic (spec §1 "identical to Phase
/// 15/16B" when sync is off or not ready). Draft/note conflict/idempotence/deletion decisions are
/// delegated to <see cref="SyncReconciliation.Decide"/>; finalized-passdown decisions to the simpler,
/// insert-only <see cref="FinalizedPassdownSyncReconciliation.Decide"/> (Phase 16E §12 — a different
/// conflict model, not reused verbatim from drafts/notes). This type only does filesystem
/// enumeration/I-O and repository calls around those pure decisions. A failure isolated to one History
/// record (malformed JSON, missing/corrupt PDF, an integrity conflict) never blocks drafts/notes sync,
/// and vice versa (Phase 16E §23).
/// </summary>
public sealed class PersonalSyncService(
    IPersonalSyncSettingsService settingsService,
    IDeviceIdProvider deviceIdProvider,
    IAssociateDraftRepository draftRepository,
    IOperationalNoteRepository noteRepository,
    IFinalizedPassdownRepository finalizedPassdownRepository,
    ISyncEntityStateRepository stateRepository,
    ISyncConflictRepository conflictRepository,
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
            await ImportHistoryAsync(root, deviceId, now, counters, cancellationToken).ConfigureAwait(false);
            await ExportDraftsAsync(root, deviceId, now, counters, cancellationToken).ConfigureAwait(false);
            await ExportNotesAsync(root, deviceId, now, counters, cancellationToken).ConfigureAwait(false);
            await ExportHistoryAsync(root, deviceId, now, counters, cancellationToken).ConfigureAwait(false);

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
                    // Disappeared between the directory listing and the read — never inferred as a deletion (§18/§29).
                    break;

                case SyncFileReadStatus.IoError:
                    counters.IoFailures++;
                    break;

                default: // Malformed, UnsupportedVersion — includes a malformed tombstone (§18/§32): never destructive.
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

        AssociateDraftPayload? remotePayload = null;
        string remoteHash = string.Empty;
        string? remotePayloadJson = null;

        if (!envelope.IsDeleted)
        {
            try
            {
                remotePayload = AssociateDraftPayloadSerializer.Deserialize(envelope.Payload.GetRawText());
            }
            catch (JsonException)
            {
                counters.Malformed++;
                return;
            }

            remoteHash = SyncContentHash.ComputeDraftHash(remotePayload);
            remotePayloadJson = JsonSerializer.Serialize(remotePayload, DraftPayloadWriteOptions);
        }

        var localDraft = await draftRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var state = await stateRepository.GetAsync(SyncEntityType.AssociateDraft, entityId, cancellationToken).ConfigureAwait(false);

        var localHash = localDraft is null ? null : SyncContentHash.ComputeDraftHash(AssociateDraftPayloadSerializer.Deserialize(localDraft.PayloadJson));

        var decision = SyncReconciliation.Decide(deviceId, envelope, localDraft is not null, localHash, state, remoteHash);

        switch (decision)
        {
            case SyncImportDecision.Ignored:
                counters.Unchanged++;
                break;

            case SyncImportDecision.RemoteOnlyImport:
                await draftRepository.SaveAsync(
                    new AssociateDraft(operatingDate, envelope.PayloadSchemaVersion, remotePayloadJson!, envelope.ModifiedUtc),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await stateRepository.UpsertAsync(
                    SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, entityId, now) with
                    {
                        SyncedRevision = 1,
                        SyncedContentHash = remoteHash,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
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
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                counters.Unchanged++;
                break;
            }

            case SyncImportDecision.SafeRemoteUpdate:
            {
                var newRevision = (state?.LocalRevision ?? 1) + 1;
                await draftRepository.SaveAsync(
                    new AssociateDraft(operatingDate, envelope.PayloadSchemaVersion, remotePayloadJson!, envelope.ModifiedUtc),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
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
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                counters.Imported++;
                break;
            }

            case SyncImportDecision.Conflict:
            case SyncImportDecision.LocalDeletionConflict:
                await RecordConflictAsync(SyncEntityType.AssociateDraft, entityId, envelope, remotePayloadJson, remoteHash, isDeleted: false, state, now, cancellationToken).ConfigureAwait(false);
                counters.Conflicts++;
                break;

            case SyncImportDecision.RemoteDeletionAlreadyAbsent:
                if (state is not null)
                {
                    await stateRepository.UpsertAsync(
                        state with { LastRemoteDeviceId = envelope.DeviceId, LastRemoteRevision = envelope.Revision, UpdatedAtUtc = now },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                counters.Unchanged++;
                break;

            case SyncImportDecision.RemoteDeletionSafeApply:
            {
                await draftRepository.DeleteAsync(operatingDate, cancellationToken: cancellationToken).ConfigureAwait(false);
                var newRevision = (state?.LocalRevision ?? 1) + 1;
                await stateRepository.UpsertAsync(
                    (state ?? SyncEntityState.CreateNew(SyncEntityType.AssociateDraft, entityId, now)) with
                    {
                        LocalRevision = newRevision,
                        SyncedRevision = newRevision,
                        SyncedContentHash = SyncContentHash.TombstoneMarker,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                        ConflictState = SyncConflictState.None,
                        ConflictRemoteDeviceId = null,
                        ConflictRemoteRevision = null,
                        ConflictRemoteContentHash = null,
                        ConflictDetectedAtUtc = null,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                counters.Imported++;
                break;
            }

            case SyncImportDecision.RemoteDeletionConflict:
                await RecordConflictAsync(SyncEntityType.AssociateDraft, entityId, envelope, remotePayloadJson: null, remoteHash: null, isDeleted: true, state, now, cancellationToken).ConfigureAwait(false);
                counters.Conflicts++;
                break;
        }
    }

    // ----- Drafts: export -----

    private async Task ExportDraftsAsync(string root, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var dates = await draftRepository.ListSavedDraftDatesAsync(cancellationToken).ConfigureAwait(false);
        var liveEntityIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var date in dates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entityId = SyncEntityIdentity.ForDraft(date);
            liveEntityIds.Add(entityId);

            var state = await stateRepository.GetAsync(SyncEntityType.AssociateDraft, entityId, cancellationToken).ConfigureAwait(false);
            if (state?.ConflictState == SyncConflictState.Conflicting)
            {
                // Never export over an unresolved conflict — 16D resolution owns clearing this (§7/§13).
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
                cancellationToken: cancellationToken).ConfigureAwait(false);
            counters.Exported++;
        }

        await ExportPendingTombstonesAsync(root, deviceId, now, liveEntityIds, SyncEntityType.AssociateDraft, counters, cancellationToken).ConfigureAwait(false);
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

        OperationalNoteSyncPayload? remotePayload = null;
        string remoteHash = string.Empty;
        string? remotePayloadJson = null;

        if (!envelope.IsDeleted)
        {
            try
            {
                remotePayload = OperationalNoteSyncPayloadSerializer.Deserialize(envelope.Payload);
            }
            catch (JsonException)
            {
                counters.Malformed++;
                return;
            }

            remoteHash = SyncContentHash.ComputeNoteHash(remotePayload);
            remotePayloadJson = OperationalNoteSyncPayloadSerializer.Serialize(remotePayload);
        }

        var localNote = await noteRepository.GetByIdAsync(noteId, cancellationToken).ConfigureAwait(false);
        var state = await stateRepository.GetAsync(SyncEntityType.OperationalNote, entityId, cancellationToken).ConfigureAwait(false);

        var localHash = localNote is null ? null : SyncContentHash.ComputeNoteHash(OperationalNoteSyncPayload.FromDomain(localNote));

        var decision = SyncReconciliation.Decide(deviceId, envelope, localNote is not null, localHash, state, remoteHash);

        switch (decision)
        {
            case SyncImportDecision.Ignored:
                counters.Unchanged++;
                break;

            case SyncImportDecision.RemoteOnlyImport:
                await noteRepository.AddAsync(remotePayload!.ToDomain(), cancellationToken: cancellationToken).ConfigureAwait(false);
                await stateRepository.UpsertAsync(
                    SyncEntityState.CreateNew(SyncEntityType.OperationalNote, entityId, now) with
                    {
                        SyncedRevision = 1,
                        SyncedContentHash = remoteHash,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
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
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                counters.Unchanged++;
                break;
            }

            case SyncImportDecision.SafeRemoteUpdate:
            {
                var newRevision = (state?.LocalRevision ?? 1) + 1;
                await noteRepository.UpdateAsync(remotePayload!.ToDomain(), cancellationToken: cancellationToken).ConfigureAwait(false);
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
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                counters.Imported++;
                break;
            }

            case SyncImportDecision.Conflict:
            case SyncImportDecision.LocalDeletionConflict:
                await RecordConflictAsync(SyncEntityType.OperationalNote, entityId, envelope, remotePayloadJson, remoteHash, isDeleted: false, state, now, cancellationToken).ConfigureAwait(false);
                counters.Conflicts++;
                break;

            case SyncImportDecision.RemoteDeletionAlreadyAbsent:
                if (state is not null)
                {
                    await stateRepository.UpsertAsync(
                        state with { LastRemoteDeviceId = envelope.DeviceId, LastRemoteRevision = envelope.Revision, UpdatedAtUtc = now },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                counters.Unchanged++;
                break;

            case SyncImportDecision.RemoteDeletionSafeApply:
            {
                await noteRepository.DeleteAsync(noteId, cancellationToken: cancellationToken).ConfigureAwait(false);
                var newRevision = (state?.LocalRevision ?? 1) + 1;
                await stateRepository.UpsertAsync(
                    (state ?? SyncEntityState.CreateNew(SyncEntityType.OperationalNote, entityId, now)) with
                    {
                        LocalRevision = newRevision,
                        SyncedRevision = newRevision,
                        SyncedContentHash = SyncContentHash.TombstoneMarker,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                        ConflictState = SyncConflictState.None,
                        ConflictRemoteDeviceId = null,
                        ConflictRemoteRevision = null,
                        ConflictRemoteContentHash = null,
                        ConflictDetectedAtUtc = null,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                counters.Imported++;
                break;
            }

            case SyncImportDecision.RemoteDeletionConflict:
                await RecordConflictAsync(SyncEntityType.OperationalNote, entityId, envelope, remotePayloadJson: null, remoteHash: null, isDeleted: true, state, now, cancellationToken).ConfigureAwait(false);
                counters.Conflicts++;
                break;
        }
    }

    // ----- Notes: export -----

    private async Task ExportNotesAsync(string root, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var notes = await noteRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var liveEntityIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var note in notes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entityId = SyncEntityIdentity.ForNote(note.Id);
            liveEntityIds.Add(entityId);

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
                cancellationToken: cancellationToken).ConfigureAwait(false);
            counters.Exported++;
        }

        await ExportPendingTombstonesAsync(root, deviceId, now, liveEntityIds, SyncEntityType.OperationalNote, counters, cancellationToken).ConfigureAwait(false);
    }

    // ----- History: import (Phase 16E §11-§19) -----

    /// <summary>
    /// Scans <c>history/</c> for canonical <c>&lt;yyyy-MM-dd&gt;-&lt;guid&gt;.json</c> files (Phase 16E
    /// §5/§6) and processes each valid one. An unexpected filename (an OneDrive conflict copy, a stray
    /// file, anything that doesn't match the exact naming <see cref="SyncPathService.TryParseHistoryFileName"/>
    /// expects — including an orphan <c>.pdf</c> with no JSON, which this loop never even looks for,
    /// §18) is silently left untouched, mirroring drafts/notes' own §28 rule.
    /// </summary>
    private async Task ImportHistoryAsync(string root, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var directory = SyncPathService.GetHistoryDirectoryPath(root);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SyncPathService.TryParseHistoryFileName(Path.GetFileNameWithoutExtension(file), out var fileDate, out var fileId))
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
                    await ProcessHistoryEnvelopeAsync(readResult.Value!, root, fileDate, fileId, deviceId, now, counters, cancellationToken).ConfigureAwait(false);
                    break;

                case SyncFileReadStatus.Missing:
                    // Disappeared between the directory listing and the read — never inferred as anything (§19).
                    break;

                case SyncFileReadStatus.IoError:
                    counters.IoFailures++;
                    break;

                default: // Malformed, UnsupportedVersion — never destructive (§19).
                    counters.Malformed++;
                    break;
            }
        }
    }

    private async Task ProcessHistoryEnvelopeAsync(
        SyncEnvelope envelope, string root, DateOnly fileDate, Guid fileId, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var entityId = SyncEntityIdentity.ForFinalizedPassdown(fileId);

        // Never trust the filename alone — an envelope claiming the wrong entity/type, a tombstone
        // (History has no deletion model, §20), or missing its content hash is rejected, not guessed at.
        if (envelope.EntityType != SyncEntityType.FinalizedPassdown
            || !string.Equals(envelope.EntityId, entityId, StringComparison.Ordinal)
            || envelope.IsDeleted
            || string.IsNullOrEmpty(envelope.ContentHash))
        {
            counters.Malformed++;
            return;
        }

        if (envelope.DeviceId == deviceId)
        {
            // Our own previously exported pair, read back during this pass's scan — never a remote change.
            counters.HistoryUnchanged++;
            counters.Unchanged++;
            return;
        }

        FinalizedPassdownSyncPayload payload;
        try
        {
            payload = FinalizedPassdownSyncPayloadSerializer.Deserialize(envelope.Payload);
        }
        catch (JsonException)
        {
            counters.Malformed++;
            return;
        }

        if (payload.Id != fileId
            || payload.PassdownDate != fileDate
            || !string.Equals(payload.ContentHash, envelope.ContentHash, StringComparison.Ordinal))
        {
            // Identity/hash mismatch between filename, envelope, and payload — never trusted (§19).
            counters.Malformed++;
            return;
        }

        try
        {
            FinalizedPassdownSnapshotSerializer.Deserialize(payload.SnapshotJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            counters.Malformed++;
            return;
        }

        if (!string.Equals(FinalizedPassdownSnapshotSerializer.ComputeContentHash(payload.SnapshotJson), payload.ContentHash, StringComparison.Ordinal))
        {
            // The stored snapshot text and the claimed content hash disagree — corrupt, never trusted (§19).
            counters.Malformed++;
            return;
        }

        var pdfPath = SyncPathService.GetHistoryPdfFilePath(root, fileDate, fileId);
        if (!File.Exists(pdfPath))
        {
            // JSON without its PDF companion yet — never import an incomplete pair (§16). Retried
            // automatically once the PDF appears; the JSON file itself is left untouched either way.
            counters.HistoryIncomplete++;
            return;
        }

        byte[] pdfBytes;
        try
        {
            pdfBytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            counters.IoFailures++;
            return;
        }

        var actualPdfHash = Convert.ToHexStringLower(SHA256.HashData(pdfBytes));
        if (!string.Equals(actualPdfHash, payload.PdfSha256, StringComparison.OrdinalIgnoreCase) || pdfBytes.LongLength != payload.PdfSizeBytes)
        {
            // Corrupt/hash-mismatched PDF — never imported, and the local (if any) copy is never
            // touched (§17). Counted as a conflict: this needs a human to look at it, exactly like the
            // integrity conditions below.
            counters.HistoryConflicts++;
            counters.Conflicts++;
            return;
        }

        var localById = await finalizedPassdownRepository.GetByIdAsync(fileId, cancellationToken).ConfigureAwait(false);
        var localContentHashForSameId = localById?.ContentHash;
        var localExistsForSameDate = localById is null
            && await finalizedPassdownRepository.ExistsForDateAsync(fileDate, cancellationToken).ConfigureAwait(false);

        var decision = FinalizedPassdownSyncReconciliation.Decide(deviceId, envelope.DeviceId, envelope.ContentHash, localContentHashForSameId, localExistsForSameDate);

        switch (decision)
        {
            case FinalizedPassdownSyncDecision.Ignored:
                counters.HistoryUnchanged++;
                counters.Unchanged++;
                break;

            case FinalizedPassdownSyncDecision.RemoteOnlyImport:
            {
                var record = payload.ToDomain(pdfBytes);
                try
                {
                    // A dedicated immutable-import path (Phase 16E §11) — never the normal finalization
                    // workflow, which would recompute the snapshot/hash from live data and could mutate
                    // timestamps/content. This inserts the exact frozen bytes as-is, preserving identity.
                    await finalizedPassdownRepository.AddAsync(record, cancellationToken).ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    // Defensive only: a genuine local finalization for this exact date could theoretically
                    // race this import between the checks above and this insert. Never crashes the pass;
                    // simply retried (and re-evaluated fresh) next time.
                    counters.IoFailures++;
                    return;
                }

                await stateRepository.UpsertAsync(
                    SyncEntityState.CreateNew(SyncEntityType.FinalizedPassdown, entityId, now) with
                    {
                        SyncedRevision = 1,
                        SyncedContentHash = envelope.ContentHash,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                counters.HistoryImported++;
                counters.Imported++;
                break;
            }

            case FinalizedPassdownSyncDecision.Converged:
            {
                var state = await stateRepository.GetAsync(SyncEntityType.FinalizedPassdown, entityId, cancellationToken).ConfigureAwait(false);
                await stateRepository.UpsertAsync(
                    (state ?? SyncEntityState.CreateNew(SyncEntityType.FinalizedPassdown, entityId, now)) with
                    {
                        SyncedRevision = 1,
                        SyncedContentHash = envelope.ContentHash,
                        LastRemoteDeviceId = envelope.DeviceId,
                        LastRemoteRevision = envelope.Revision,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                counters.HistoryUnchanged++;
                counters.Unchanged++;
                break;
            }

            case FinalizedPassdownSyncDecision.ImmutableConflict:
            {
                // Same Guid, different content — never expected in practice (two devices cannot mint the
                // same finalization id independently), but never silently overwritten either side if it
                // somehow occurs (Phase 16E §13). Keyed by the shared id, so this local record's own
                // export is also paused (never re-exported over an unresolved conflict).
                var state = await stateRepository.GetAsync(SyncEntityType.FinalizedPassdown, entityId, cancellationToken).ConfigureAwait(false);
                await RecordConflictAsync(SyncEntityType.FinalizedPassdown, entityId, envelope, FinalizedPassdownSyncPayloadSerializer.Serialize(payload), envelope.ContentHash, isDeleted: false, state, now, cancellationToken).ConfigureAwait(false);
                counters.HistoryConflicts++;
                counters.Conflicts++;
                break;
            }

            case FinalizedPassdownSyncDecision.DualFinalizationConflict:
            {
                // Two devices independently finalized the same operating date under different ids (Phase
                // 16E §14). Deliberately keyed by the REMOTE record's own entity id, not the local
                // record's — the local finalized passdown is a legitimate, distinct record that must keep
                // syncing normally; only this specific remote record is flagged as needing review, and
                // both source artifacts (the local file this device already exported, and the remote file
                // untouched in the shared folder) are preserved as-is (never mutated, never deleted).
                var state = await stateRepository.GetAsync(SyncEntityType.FinalizedPassdown, entityId, cancellationToken).ConfigureAwait(false);
                await RecordConflictAsync(SyncEntityType.FinalizedPassdown, entityId, envelope, FinalizedPassdownSyncPayloadSerializer.Serialize(payload), envelope.ContentHash, isDeleted: false, state, now, cancellationToken).ConfigureAwait(false);
                counters.HistoryConflicts++;
                counters.Conflicts++;
                break;
            }
        }
    }

    // ----- History: export (Phase 16E §8/§9/§10) -----

    /// <summary>
    /// Walks every local finalized passdown (Phase 16E §10 bootstrap included — a never-synced record
    /// has no <see cref="SyncEntityState"/> row, so its hash trivially never matches
    /// <see cref="SyncEntityState.SyncedContentHash"/>) and exports any not already confirmed in sync.
    /// Since finalized passdowns are immutable, <see cref="FinalizedPassdown.ContentHash"/> for a given
    /// id never changes — a single equality check against <see cref="SyncEntityState.SyncedContentHash"/>
    /// is sufficient for both "never exported" and "already exported, nothing to do" (no revision
    /// bookkeeping needed the way drafts/notes need it). No tombstone export exists for History (Phase
    /// 16E §20 — there is no delete operation for finalized passdowns at all).
    /// </summary>
    private async Task ExportHistoryAsync(string root, Guid deviceId, DateTimeOffset now, Counters counters, CancellationToken cancellationToken)
    {
        var metadata = await finalizedPassdownRepository.ListForSyncAsync(cancellationToken).ConfigureAwait(false);

        foreach (var item in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entityId = SyncEntityIdentity.ForFinalizedPassdown(item.Id);
            var state = await stateRepository.GetAsync(SyncEntityType.FinalizedPassdown, entityId, cancellationToken).ConfigureAwait(false);
            if (state?.ConflictState == SyncConflictState.Conflicting)
            {
                // Never export over an unresolved immutable-history conflict (§13/§20).
                continue;
            }

            if (state is not null && string.Equals(state.SyncedContentHash, item.ContentHash, StringComparison.Ordinal))
            {
                continue; // Already in sync — insert-only content never changes, so this alone is sufficient (§6/§21).
            }

            var record = await finalizedPassdownRepository.GetByDateAsync(item.PassdownDate, cancellationToken).ConfigureAwait(false);
            if (record is null || record.Id != item.Id || !record.HasFrozenPdf)
            {
                // Defensive: raced with something else since the metadata listing, or a legacy pre-9C
                // row with no frozen PDF at all — never regenerated (§4), so there is nothing to export.
                continue;
            }

            var payload = FinalizedPassdownSyncPayload.FromDomain(record);
            var envelope = new SyncEnvelope
            {
                SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
                EntityType = SyncEntityType.FinalizedPassdown,
                EntityId = entityId,
                DeviceId = deviceId,
                Revision = 1,
                ModifiedUtc = record.FinalizedAtUtc,
                IsDeleted = false,
                PayloadSchemaVersion = FinalizedPassdownSyncPayload.CurrentSchemaVersion,
                Payload = JsonSerializer.SerializeToElement(payload, FinalizedPassdownSyncPayloadSerializer.Options),
                ContentHash = record.ContentHash,
            };

            var jsonPath = SyncPathService.GetHistoryJsonFilePath(root, record.PassdownDate, record.Id);
            var pdfPath = SyncPathService.GetHistoryPdfFilePath(root, record.PassdownDate, record.Id);

            try
            {
                // PDF first, JSON last (§8/§9/§24) — a reader must never observe a JSON envelope whose
                // PDF companion isn't already fully present; the presence of a valid JSON record means
                // its PDF is guaranteed complete.
                await AtomicFileWriter.WriteBytesAsync(pdfPath, record.PdfBytes!, cancellationToken).ConfigureAwait(false);
                await AtomicFileWriter.WriteTextAsync(jsonPath, SyncEnvelopeSerializer.Serialize(envelope), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                counters.IoFailures++;
                continue; // Left dirty (state unchanged) — retried next pass (§9/§34); OneDrive never part of the finalization transaction itself.
            }

            await stateRepository.UpsertAsync(
                (state ?? SyncEntityState.CreateNew(SyncEntityType.FinalizedPassdown, entityId, now)) with
                {
                    SyncedRevision = 1,
                    SyncedContentHash = record.ContentHash,
                    UpdatedAtUtc = now,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            counters.HistoryExported++;
            counters.Exported++;
        }
    }

    // ----- Tombstone export (shared by drafts and notes) -----

    /// <summary>
    /// Exports a tombstone for every entity this device knows about (has a <see cref="SyncEntityState"/>
    /// row) but no longer has a domain row for (Phase 16D §13) — independent of whether that domain row
    /// still exists, since it may already be gone by the time this runs. Skips anything with a domain
    /// row (<paramref name="liveEntityIds"/>, handled by the normal content-export loop above), anything
    /// mid-conflict (never export over an unresolved conflict), and anything already recorded as
    /// synced-as-deleted (<see cref="SyncContentHash.TombstoneMarker"/> — idempotence, §21).
    /// </summary>
    private async Task ExportPendingTombstonesAsync(
        string root, Guid deviceId, DateTimeOffset now, IReadOnlySet<string> liveEntityIds, SyncEntityType entityType, Counters counters, CancellationToken cancellationToken)
    {
        var states = await stateRepository.ListByTypeAsync(entityType, cancellationToken).ConfigureAwait(false);

        foreach (var state in states)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (liveEntityIds.Contains(state.EntityId))
            {
                continue;
            }

            if (state.ConflictState == SyncConflictState.Conflicting)
            {
                continue;
            }

            if (string.Equals(state.SyncedContentHash, SyncContentHash.TombstoneMarker, StringComparison.Ordinal))
            {
                continue; // Already synced as deleted — nothing to export (§21 idempotence).
            }

            string targetPath;
            if (entityType == SyncEntityType.AssociateDraft)
            {
                if (!SyncEntityIdentity.TryParseDraftId(state.EntityId, out var operatingDate))
                {
                    continue; // Defensive: a malformed entity id can never have reached this table via normal code paths.
                }

                targetPath = SyncPathService.GetDraftFilePath(root, operatingDate);
            }
            else
            {
                if (!SyncEntityIdentity.TryParseNoteId(state.EntityId, out var noteId))
                {
                    continue;
                }

                targetPath = SyncPathService.GetOperationalNoteFilePath(root, noteId);
            }

            var revision = state.LocalRevision;
            var envelope = new SyncEnvelope
            {
                SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
                EntityType = entityType,
                EntityId = state.EntityId,
                DeviceId = deviceId,
                Revision = revision,
                ModifiedUtc = now,
                IsDeleted = true,
                PayloadSchemaVersion = entityType == SyncEntityType.AssociateDraft ? AssociateDraftPayload.CurrentSchemaVersion : OperationalNoteSyncPayload.CurrentSchemaVersion,
                Payload = JsonSerializer.SerializeToElement<object?>(null),
            };

            try
            {
                await AtomicFileWriter.WriteTextAsync(targetPath, SyncEnvelopeSerializer.Serialize(envelope), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                counters.IoFailures++;
                continue; // Deletion remains local and pending — retried next pass (§13).
            }

            await stateRepository.UpsertAsync(
                state with { SyncedRevision = revision, SyncedContentHash = SyncContentHash.TombstoneMarker, UpdatedAtUtc = now },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            counters.Exported++;
        }
    }

    // ----- Conflict persistence (shared by drafts and notes, Phase 16D §2/§10) -----

    /// <summary>
    /// Persists a newly-detected conflict's full remote envelope (so 16D resolution can act on it after
    /// a restart, §2), or — if this entity is already known to be conflicting — leaves the originally
    /// stored conflict untouched and only records that newer remote activity exists (§10: never
    /// silently replace an unresolved conflict's stored payload).
    /// </summary>
    private async Task RecordConflictAsync(
        SyncEntityType entityType, string entityId, SyncEnvelope envelope,
        string? remotePayloadJson, string? remoteHash, bool isDeleted,
        SyncEntityState? state, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existingConflict = await conflictRepository.GetAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);

        if (existingConflict is null)
        {
            await conflictRepository.UpsertAsync(
                new SyncConflictRecord(
                    entityType, entityId, envelope.DeviceId, envelope.Revision, envelope.ModifiedUtc, isDeleted,
                    isDeleted ? null : envelope.PayloadSchemaVersion,
                    remotePayloadJson, remoteHash, now, null, null, null),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await stateRepository.UpsertAsync(
                (state ?? SyncEntityState.CreateNew(entityType, entityId, now)) with
                {
                    ConflictState = SyncConflictState.Conflicting,
                    ConflictRemoteDeviceId = envelope.DeviceId,
                    ConflictRemoteRevision = envelope.Revision,
                    ConflictRemoteContentHash = remoteHash,
                    ConflictDetectedAtUtc = now,
                    UpdatedAtUtc = now,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        if (existingConflict.RemoteDeviceId == envelope.DeviceId && existingConflict.RemoteRevision == envelope.Revision)
        {
            // The exact same remote revision we already recorded — consistent re-detection, not new activity.
            return;
        }

        await conflictRepository.UpsertAsync(
            existingConflict with
            {
                NewerRemoteDeviceId = envelope.DeviceId,
                NewerRemoteRevision = envelope.Revision,
                NewerRemoteDetectedAtUtc = now,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private sealed class Counters
    {
        public int Imported;
        public int Exported;
        public int Unchanged;
        public int Conflicts;
        public int Malformed;
        public int IoFailures;
        public int HistoryImported;
        public int HistoryExported;
        public int HistoryUnchanged;
        public int HistoryConflicts;
        public int HistoryIncomplete;

        public PersonalSyncResult ToResult() => new(
            PersonalSyncOutcome.Completed, Imported, Exported, Unchanged, Conflicts, Malformed, IoFailures, null,
            HistoryImported, HistoryExported, HistoryUnchanged, HistoryConflicts, HistoryIncomplete);
    }
}
