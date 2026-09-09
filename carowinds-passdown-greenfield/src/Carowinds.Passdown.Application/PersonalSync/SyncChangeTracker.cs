using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The one hook every local save of a syncable entity goes through to mark it dirty for sync (Phase
/// 16C §7): "save SQLite first, then update sync metadata" — never the other way around, and never
/// part of the same SQLite transaction as the domain save itself (a sync-metadata write failing must
/// never roll back, or be rolled back by, the domain save that already succeeded). Bumping
/// <see cref="SyncEntityState.LocalRevision"/> here does not by itself decide whether an export is
/// needed — <see cref="SyncReconciliation"/>/the sync pass compares canonical content hashes for that,
/// so even a missed call to this tracker (e.g. a direct repository write bypassing the Application
/// service) cannot cause a change to go unsynced forever; it only means the exported
/// <see cref="SyncEnvelope.Revision"/> undercounts how many local edits actually happened.
/// </summary>
public interface ISyncChangeTracker
{
    Task MarkLocalChangeAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISyncChangeTracker"/>
public sealed class SyncChangeTracker(ISyncEntityStateRepository repository, IClock clock) : ISyncChangeTracker
{
    public async Task MarkLocalChangeAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(entityType, entityId, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;

        var updated = existing is null
            ? SyncEntityState.CreateNew(entityType, entityId, now)
            : existing with { LocalRevision = existing.LocalRevision + 1, UpdatedAtUtc = now };

        await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
    }
}
