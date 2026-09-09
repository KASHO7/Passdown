namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Decorates the plain <see cref="SyncChangeTracker"/> with the one save-triggered automatic-sync hook
/// (Phase 16F §4): every local save of a syncable entity already calls
/// <see cref="ISyncChangeTracker.MarkLocalChangeAsync"/> (Phase 16C §7) — this wraps that same call to
/// also request a debounced sync pass, without touching <see cref="Associate.AssociatePassdownService"/>
/// or <see cref="OperationalNotes.OperationalNotesService"/> themselves and without any ViewModel owning
/// a timer. The dirty-metadata write always happens first, exactly as before; the coordinator request is
/// fire-and-forget and never delays or can fail the save it rides on.
/// </summary>
public sealed class AutomaticSyncChangeTracker(ISyncChangeTracker inner, IPersonalSyncCoordinator coordinator) : ISyncChangeTracker
{
    public async Task MarkLocalChangeAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
    {
        await inner.MarkLocalChangeAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);
        coordinator.RequestSync(SyncTriggerReason.LocalChange);
    }
}
