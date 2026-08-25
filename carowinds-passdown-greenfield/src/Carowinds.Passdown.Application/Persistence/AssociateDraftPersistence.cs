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
    Task SaveAsync(AssociateDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every <see cref="AssociateDraft.OperatingDate"/> with a persisted row, ascending. Used by
    /// Phase 9B's scheduled finalizer to find finalization candidates without deserializing every
    /// draft's <see cref="AssociateDraft.PayloadJson"/> (spec Phase 9B Part L) — "a saved draft row
    /// exists" is the eligibility signal (Part D), not "a workspace can be composed for this date"
    /// (which is true for every date, saved or not).
    /// </summary>
    Task<IReadOnlyList<DateOnly>> ListSavedDraftDatesAsync(CancellationToken cancellationToken = default);
}
