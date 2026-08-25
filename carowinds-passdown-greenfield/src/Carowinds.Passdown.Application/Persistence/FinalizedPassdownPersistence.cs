using Carowinds.Passdown.Application.Finalization;

namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// One immutable finalized Associate Passdown, keyed by <see cref="PassdownDate"/> (spec Phase 9
/// Parts A/B). Distinct from <see cref="AssociateDraft"/>: a draft is the editable working state for
/// a date; a <see cref="FinalizedPassdown"/> is the historical record of exactly what was finalized
/// for it, and never changes after <see cref="IFinalizedPassdownRepository.AddAsync"/> returns.
/// <see cref="SnapshotJson"/> is opaque to persistence, exactly like <see cref="AssociateDraft.PayloadJson"/>
/// — <see cref="FinalizedPassdownSnapshotSerializer"/> is the only code that knows its shape,
/// gated by <see cref="SnapshotSchemaVersion"/>. <see cref="Id"/> is a surrogate key independent of
/// <see cref="PassdownDate"/> so a future revision design (explicitly not built yet — spec Part B)
/// would not require redefining this table's identity.
/// </summary>
public sealed record FinalizedPassdown(
    Guid Id,
    DateOnly PassdownDate,
    DateTimeOffset FinalizedAtUtc,
    int SnapshotSchemaVersion,
    string SnapshotJson,
    string ContentHash,
    FinalizationOrigin Origin);

/// <summary>Lightweight metadata for one finalized passdown (spec Phase 9 Part T) — enough for a future History UI list without deserializing every row's <see cref="FinalizedPassdown.SnapshotJson"/>.</summary>
public sealed record FinalizedPassdownSummary(
    DateOnly PassdownDate,
    DateTimeOffset FinalizedAtUtc,
    FinalizationOrigin Origin);

/// <summary>
/// Narrow, insert-only persistence contract for finalized passdowns (spec Phase 9 Part M). Exposes
/// no update or delete — the model is immutable by convention; correcting a bad historical row, if
/// ever necessary, is a support/database concern, not a normal application operation.
/// </summary>
public interface IFinalizedPassdownRepository
{
    Task<FinalizedPassdown?> GetByDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default);

    Task<bool> ExistsForDateAsync(DateOnly passdownDate, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new finalized passdown. Throws if <paramref name="finalizedPassdown"/>'s <see cref="FinalizedPassdown.PassdownDate"/> is already finalized (spec Part B's one-record-per-date uniqueness) — callers should check <see cref="ExistsForDateAsync"/> first when idempotent no-op behavior is wanted.</summary>
    Task AddAsync(FinalizedPassdown finalizedPassdown, CancellationToken cancellationToken = default);

    /// <summary>All finalized passdowns, most recent <see cref="FinalizedPassdown.PassdownDate"/> first (spec Part T's future History UI listing).</summary>
    Task<IReadOnlyList<FinalizedPassdownSummary>> ListAsync(CancellationToken cancellationToken = default);
}
