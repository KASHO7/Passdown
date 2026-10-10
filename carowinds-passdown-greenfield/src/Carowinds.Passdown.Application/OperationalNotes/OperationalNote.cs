namespace Carowinds.Passdown.Application.OperationalNotes;

/// <summary>
/// Controlled category set for a persistent <see cref="OperationalNote"/> (spec Phase 11A Part D).
/// Deliberately a small closed enum, not free text — existing Carowinds operational workflow areas
/// only. Do not add more without a documented product decision; this list is authoritative.
/// </summary>
public enum OperationalNoteCategory
{
    General,
    Staffing,
    Admissions,
    GuestServices,
    Equipment,
    Events,
    Operations,
}

/// <summary>
/// A persistent note's presentation/sort priority (spec Phase 11A Part E) — not the Alerts system, and
/// never triggers a notification. Deliberately two values: <c>Critical</c> was considered and rejected
/// for 11A, since no operational use case distinct from <see cref="Important"/> was identified.
/// </summary>
public enum OperationalNotePriority
{
    Normal,
    Important,
}

/// <summary>
/// A persistent note's persisted lifecycle (spec Phase 11A Part G). Deliberately two values — no
/// draft/archive/deleted workflow. <see cref="Resolved"/> is a manual, explicit action
/// (<c>IOperationalNotesService.ResolveAsync</c>); a note whose <see cref="OperationalNote.EffectiveThrough"/>
/// has passed stays <see cref="Active"/> here and is only "expired" as a derived
/// <see cref="OperationalNoteLifecycleState"/> — the two concepts (manually resolved vs. no longer
/// date-applicable) are deliberately kept distinct (spec Part X).
/// </summary>
public enum OperationalNoteStatus
{
    Active,
    Resolved,
}

/// <summary>
/// A persistent, supervisor-managed operational note that may span multiple days (spec Phase 11A Part
/// A) — architecturally separate from a daily <see cref="Associate.AssociateDraftPayload.OperationalNotes"/>
/// entry, which stays scoped to one operating date's draft and is frozen into History at finalization.
/// A persistent note is never automatically included in any daily passdown (spec Part S) — inclusion is
/// an explicit future opt-in selection, not built in this phase. <see cref="Id"/> is a stable identity
/// independent of content, so a note can be edited over time without becoming "a different note".
/// </summary>
public sealed record OperationalNote(
    Guid Id,
    string Title,
    string? Body,
    OperationalNoteCategory Category,
    OperationalNotePriority Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    OperationalNoteStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ResolvedAtUtc)
{
    /// <summary>Validation bound (spec Part C) — generous enough for a concise operational summary, tight enough to catch an accidental paste of unrelated content.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Validation bound (spec Part C) — generous for genuine operational detail (e.g. a multi-step workaround), not a hard UX-breaking limit.</summary>
    public const int MaxBodyLength = 4000;
}

/// <summary>
/// The derived, presentation-facing lifecycle a persistent note occupies for a given operating date
/// (spec Phase 11A Part Y) — layered on top of the simpler persisted <see cref="OperationalNoteStatus"/>
/// rather than replacing it, so "manually resolved" and "date range ended" remain distinguishable.
/// </summary>
public enum OperationalNoteLifecycleState
{
    Upcoming,
    Active,
    Expired,
    Resolved,
}

/// <summary>
/// Derives <see cref="OperationalNoteLifecycleState"/> for a note as of a given operating date, without
/// ever mutating the note's persisted <see cref="OperationalNoteStatus"/> (spec Part X — no scheduled
/// job silently flips a note to Resolved when its date range ends).
/// </summary>
public static class OperationalNoteLifecycle
{
    public static OperationalNoteLifecycleState Derive(OperationalNote note, DateOnly operatingDate)
    {
        if (note.Status == OperationalNoteStatus.Resolved)
        {
            return OperationalNoteLifecycleState.Resolved;
        }

        if (operatingDate < note.EffectiveFrom)
        {
            return OperationalNoteLifecycleState.Upcoming;
        }

        if (note.EffectiveThrough is { } effectiveThrough && operatingDate > effectiveThrough)
        {
            return OperationalNoteLifecycleState.Expired;
        }

        return OperationalNoteLifecycleState.Active;
    }
}

/// <summary>
/// The single authoritative rule for whether a persistent note applies to a given operating date (spec
/// Phase 11A Part J) — used both to build the future "relevant notes" suggestion list (Phase 11D) and
/// by <see cref="IOperationalNoteRepository.ListRelevantForDateAsync"/>'s SQL filter as a defense-in-depth
/// re-check after the database query, so correctness never depends solely on the SQL translation being
/// right.
/// </summary>
public static class OperationalNoteRelevance
{
    public static bool IsRelevantForDate(OperationalNote note, DateOnly operatingDate) =>
        note.Status == OperationalNoteStatus.Active
        && note.EffectiveFrom <= operatingDate
        && (note.EffectiveThrough is null || operatingDate <= note.EffectiveThrough);
}

/// <summary>
/// Deterministic list ordering for persistent notes (spec Phase 11A Part W) — two distinct orderings
/// for two distinct purposes, both documented here rather than left to callers to reinvent.
/// </summary>
public static class OperationalNoteOrdering
{
    /// <summary>
    /// The general "browse all notes" order: Active before Resolved; within Active, Important before
    /// Normal; then newest-created first; then <see cref="OperationalNote.Id"/> as a final deterministic
    /// tie-breaker.
    /// </summary>
    public static IReadOnlyList<OperationalNote> ByDefaultListOrder(IEnumerable<OperationalNote> notes) =>
        notes
            .OrderBy(note => note.Status == OperationalNoteStatus.Resolved ? 1 : 0)
            .ThenBy(note => note.Priority == OperationalNotePriority.Important ? 0 : 1)
            .ThenByDescending(note => note.CreatedAtUtc)
            .ThenBy(note => note.Id)
            .ToList();

    /// <summary>
    /// The "relevant for this operating date" order: Important first; then <see cref="OperationalNote.EffectiveFrom"/>
    /// ascending; then <see cref="OperationalNote.CreatedAtUtc"/> ascending; then <see cref="OperationalNote.Id"/>
    /// as a final deterministic tie-breaker.
    /// </summary>
    public static IReadOnlyList<OperationalNote> ByRelevance(IEnumerable<OperationalNote> notes) =>
        notes
            .OrderBy(note => note.Priority == OperationalNotePriority.Important ? 0 : 1)
            .ThenBy(note => note.EffectiveFrom)
            .ThenBy(note => note.CreatedAtUtc)
            .ThenBy(note => note.Id)
            .ToList();
}
