using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.OperationalNotes;

/// <summary>
/// Persistence contract for persistent Operational Notes (spec Phase 11A Part M). Deliberately no hard
/// delete exposed to normal application use — a resolved note remains a historical record (spec Part Q);
/// correcting/removing a bad row, if ever necessary, is a support/database concern, matching the same
/// convention <see cref="Persistence.IFinalizedPassdownRepository"/> already established for finalized
/// passdowns. <see cref="DeleteAsync"/> (Phase 16D §13) is the one exception — a repository-level
/// primitive the sync engine's tombstone import/resolution needs, not a product feature; no ViewModel
/// calls it (§12).
/// </summary>
public interface IOperationalNoteRepository
{
    Task<OperationalNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every persistent note (Active and Resolved alike), in no particular guaranteed order — callers apply <see cref="OperationalNoteOrdering"/> for deterministic presentation order.</summary>
    Task<IReadOnlyList<OperationalNote>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(OperationalNote note, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing note's stored row in place. <see cref="OperationalNote.Id"/> identifies the row; the caller (the Application-layer service) is responsible for preserving identity/<see cref="OperationalNote.CreatedAtUtc"/> semantics — this method is a plain overwrite of the other columns.</summary>
    Task UpdateAsync(OperationalNote note, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes the note row for <paramref name="id"/>, if one exists (Phase 16D §13/§14). A no-op when no row exists.</summary>
    Task DeleteAsync(Guid id, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notes whose <see cref="OperationalNote.Status"/> is Active and whose effective date range could
    /// include <paramref name="operatingDate"/> (spec Part M's optional relevance query, to avoid
    /// loading/filtering every note on every call) — callers still apply
    /// <see cref="OperationalNoteRelevance.IsRelevantForDate"/> as the authoritative re-check.
    /// </summary>
    Task<IReadOnlyList<OperationalNote>> ListRelevantForDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);
}
