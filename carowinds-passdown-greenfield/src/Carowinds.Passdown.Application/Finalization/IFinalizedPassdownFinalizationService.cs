using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Finalization;

/// <summary>Result of one <see cref="IFinalizedPassdownFinalizationService.FinalizeAsync"/> call (spec Phase 9 Part B's idempotent "if already finalized, do nothing").</summary>
public enum FinalizationOutcome
{
    /// <summary>A new <see cref="FinalizedPassdown"/> row was inserted.</summary>
    Created,

    /// <summary><paramref name="passdownDate"/> already had a finalized record; nothing was written.</summary>
    AlreadyFinalized,
}

public sealed record FinalizationResult(FinalizationOutcome Outcome, FinalizedPassdown FinalizedPassdown);

/// <summary>
/// Application-layer use case that turns a composed <see cref="AssociateWorkspace"/> into a stored,
/// immutable <see cref="FinalizedPassdown"/> row (spec Phase 9 Parts B/J/N/O) — the single place that
/// enforces "one finalized record per date" and atomic all-or-nothing insertion at the application
/// level, on top of the database's own unique constraint. Not called from anywhere in the app yet
/// (spec Part R); Phase 9B's scheduler is the intended future caller, and Phase 9A's tests exercise
/// it directly.
/// </summary>
public interface IFinalizedPassdownFinalizationService
{
    /// <summary>
    /// Finalizes <paramref name="passdownDate"/> from <paramref name="workspace"/> if it has not
    /// already been finalized. <paramref name="workspace"/> must already be composed from local
    /// storage (spec Part J: "no website/network request should happen while building a historical
    /// snapshot") — this method performs no retrieval itself.
    /// </summary>
    Task<FinalizationResult> FinalizeAsync(
        DateOnly passdownDate,
        AssociateWorkspace workspace,
        FinalizationOrigin origin,
        DateTimeOffset finalizedAt,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IFinalizedPassdownFinalizationService"/>
public sealed class FinalizedPassdownFinalizationService(
    IFinalizedPassdownRepository repository,
    IFinalizedPassdownSnapshotBuilder snapshotBuilder) : IFinalizedPassdownFinalizationService
{
    public async Task<FinalizationResult> FinalizeAsync(
        DateOnly passdownDate,
        AssociateWorkspace workspace,
        FinalizationOrigin origin,
        DateTimeOffset finalizedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        // Part B idempotency: a second finalization attempt for an already-finalized date is a
        // no-op, never a second row or an overwrite.
        var existing = await repository.GetByDateAsync(passdownDate, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new FinalizationResult(FinalizationOutcome.AlreadyFinalized, existing);
        }

        // Building/serializing/hashing all happen before any write (Part O: "if serialization/hash
        // creation fails: no row should be inserted"). A single subsequent AddAsync is itself one
        // atomic SQLite statement, so there is no window where a partially-written row could exist.
        var snapshot = snapshotBuilder.Build(workspace, finalizedAt);
        var snapshotJson = FinalizedPassdownSnapshotSerializer.Serialize(snapshot);
        var contentHash = FinalizedPassdownSnapshotSerializer.ComputeContentHash(snapshotJson);

        var finalizedPassdown = new FinalizedPassdown(
            Guid.NewGuid(),
            passdownDate,
            finalizedAt,
            snapshot.SchemaVersion,
            snapshotJson,
            contentHash,
            origin);

        await repository.AddAsync(finalizedPassdown, cancellationToken).ConfigureAwait(false);
        return new FinalizationResult(FinalizationOutcome.Created, finalizedPassdown);
    }
}
