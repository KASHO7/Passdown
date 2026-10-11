using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The version an editor's save found in storage when it differed from the version that editor
/// originally loaded — i.e. something (in practice, a Personal Sync import or conflict resolution)
/// changed it while the editor was open. <see cref="PayloadJson"/> is the entity's sync payload JSON
/// (null when the entity had been deleted underneath the editor).
/// </summary>
public sealed record DivergedStoredVersion(
    string? PayloadJson,
    int? PayloadSchemaVersion,
    string? ContentHash,
    DateTimeOffset ModifiedUtc);

/// <summary>
/// Phase 18B REL-004 product rule: "auto-reload clean editors; preserve dirty editors and require
/// explicit conflict resolution; never silently overwrite a synced change from an already-open editor."
///
/// When an editor save finds that the stored version is no longer the one the editor loaded, the
/// caller (inside the same write-locked unit of work) saves the editor's content as this device's
/// local version and calls <see cref="RecordAsync"/>, which preserves the version found in storage as
/// the "other device" side of an ordinary Personal Sync conflict and marks the entity Conflicting. That
/// reuses the existing, tested conflict machinery end to end: export of the entity pauses, the conflict
/// appears in Personal Sync settings, and "Keep This Device" / "Use Other Device" / "Accept Deletion"
/// resolve it explicitly. Nothing is ever discarded without a user decision.
/// </summary>
/// <remarks>Must be called inside the same unit of work as the editor's write, before commit.</remarks>
public sealed class OpenEditorConflictRecorder(
    ISyncEntityStateRepository stateRepository,
    ISyncConflictRepository conflictRepository,
    IClock clock)
{
    public async Task RecordAsync(
        SyncEntityType entityType,
        string entityId,
        DivergedStoredVersion storedVersion,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storedVersion);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        var now = clock.UtcNow;
        var state = await stateRepository.GetAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);

        // Provenance of the version that changed underneath the editor: the remote device/revision the
        // last import recorded. Guid.Empty/0 only if the change did not come from sync at all, which no
        // current code path does — still preserved and still requires an explicit decision.
        var remoteDeviceId = state?.LastRemoteDeviceId ?? Guid.Empty;
        var remoteRevision = state?.LastRemoteRevision ?? 0;
        var isDeleted = storedVersion.PayloadJson is null;

        await conflictRepository.UpsertAsync(
            new SyncConflictRecord(
                entityType, entityId, remoteDeviceId, remoteRevision, storedVersion.ModifiedUtc, isDeleted,
                isDeleted ? null : storedVersion.PayloadSchemaVersion,
                storedVersion.PayloadJson,
                isDeleted ? null : storedVersion.ContentHash,
                now, null, null, null),
            unitOfWork, cancellationToken).ConfigureAwait(false);

        await stateRepository.UpsertAsync(
            (state ?? SyncEntityState.CreateNew(entityType, entityId, now)) with
            {
                // The caller still records the editor's save as a local change (ISyncChangeTracker) after
                // committing — exactly like any other save — which advances the local revision and asks
                // for a sync pass so the Personal Sync screen's conflict count updates promptly. Export of
                // this entity stays paused while it is Conflicting.
                ConflictState = SyncConflictState.Conflicting,
                ConflictRemoteDeviceId = remoteDeviceId,
                ConflictRemoteRevision = remoteRevision,
                ConflictRemoteContentHash = isDeleted ? null : storedVersion.ContentHash,
                ConflictDetectedAtUtc = now,
                UpdatedAtUtc = now,
            },
            unitOfWork, cancellationToken).ConfigureAwait(false);
    }
}
