namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// One Associate Passdown manual draft, keyed by operating date (spec §6.2).
/// <see cref="PayloadJson"/> is opaque to persistence — the Associate form's own
/// schema evolves independently of storage; <see cref="SchemaVersion"/> lets a
/// future reader detect and migrate older payload shapes.
/// </summary>
public sealed record AssociateDraft(
    DateOnly OperatingDate,
    int SchemaVersion,
    string PayloadJson,
    DateTimeOffset UpdatedAt);

public interface IAssociateDraftRepository
{
    Task<AssociateDraft?> GetAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);

    /// <summary>Inserts or fully replaces the draft for <paramref name="draft"/>'s operating date.</summary>
    Task SaveAsync(AssociateDraft draft, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the draft row for <paramref name="operatingDate"/>, if one exists (Phase 16D §13) — the
    /// repository-level primitive a genuine local-delete action or a remote-tombstone-import "SAFE"
    /// apply (§14) needs; the product has no user-facing delete action for a draft today (§12), so
    /// nothing in the UI calls this yet. A no-op (never throws) when no row exists, matching this
    /// repository's other idempotent-by-nature methods.
    /// </summary>
    Task DeleteAsync(DateOnly operatingDate, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every <see cref="AssociateDraft.OperatingDate"/> with a persisted row, ascending. Used by
    /// Phase 9B's scheduled finalizer to find finalization candidates without deserializing every
    /// draft's <see cref="AssociateDraft.PayloadJson"/> (spec Phase 9B Part L) — "a saved draft row
    /// exists" is the eligibility signal (Part D), not "a workspace can be composed for this date"
    /// (which is true for every date, saved or not).
    /// </summary>
    Task<IReadOnlyList<DateOnly>> ListSavedDraftDatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every saved draft row (payload included) with <see cref="AssociateDraft.OperatingDate"/> in
    /// <paramref name="startDate"/>..<paramref name="endDateInclusive"/> (spec Phase 12B Part N/O/Q)
    /// — one query for an entire Operations Calendar month grid's visible range, so resolving each
    /// visible date's saved manual-hours/private-buyout override never costs one
    /// <see cref="GetAsync"/> call per day.
    /// </summary>
    Task<IReadOnlyList<AssociateDraft>> GetByDateRangeAsync(DateOnly startDate, DateOnly endDateInclusive, CancellationToken cancellationToken = default);
}
