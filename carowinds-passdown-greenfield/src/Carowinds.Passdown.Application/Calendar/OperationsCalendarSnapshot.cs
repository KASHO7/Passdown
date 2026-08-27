namespace Carowinds.Passdown.Application.Calendar;

/// <summary>
/// One day cell in the Operations Calendar's month grid (Phase 12A Part D/E) — intentionally modest:
/// enough for a day cell to show a number plus a small set of indicators, never a second projection
/// of Associate/finalization/notes business logic. <see cref="HasAssociateDraft"/> reflects only
/// whether a draft row was ever saved for the date (the same signal
/// <see cref="Finalization.ScheduledPassdownFinalizer"/> already uses to decide finalization
/// eligibility) — not whether it currently has any particular content.
/// </summary>
public sealed record OperationsCalendarDay(
    DateOnly Date,
    bool IsInDisplayedMonth,
    bool IsToday,
    bool HasFinalizedPassdown,
    bool HasAssociateDraft,
    int RelevantOperationalNoteCount);

/// <summary>
/// A date's passdown status for the Operations Calendar's selected-date summary (Phase 12A Part D) —
/// the same three states <c>Dashboard.DashboardPassdownStatus</c> already models for "today", defined
/// independently here since the Calendar classifies an arbitrary selected date, not only today.
/// </summary>
public enum OperationsCalendarPassdownStatus
{
    NoDraft,
    DraftInProgress,
    Finalized,
}

/// <summary>The concise detail panel content for whichever date is currently selected (Phase 12A Part E).</summary>
public sealed record OperationsCalendarSelectedDateSummary(
    DateOnly Date,
    bool IsToday,
    OperationsCalendarPassdownStatus PassdownStatus,
    DateTimeOffset? LastSavedAt,
    DateTimeOffset? FinalizedAt,
    int RelevantOperationalNoteCount);

/// <summary>
/// The Application-layer read model an Operations Calendar screen is built from (Phase 12A Part D) —
/// a month grid plus one selected date's detail, projected entirely from existing local state
/// (<see cref="Associate.IAssociatePassdownService"/>, <see cref="Persistence.IFinalizedPassdownRepository"/>,
/// <see cref="OperationalNotes.IOperationalNotesService"/>, <see cref="Persistence.IAssociateDraftRepository"/>).
/// Never a second business-rule implementation of finalization eligibility, note relevance, or draft
/// composition. <see cref="DisplayedMonth"/> is always normalized to the first day of the month it
/// represents, independent of whatever day-of-month was passed to
/// <see cref="IOperationsCalendarService.LoadAsync"/>.
/// </summary>
public sealed record OperationsCalendarSnapshot(
    DateOnly DisplayedMonth,
    DateOnly Today,
    DateOnly SelectedDate,
    IReadOnlyList<OperationsCalendarDay> Days,
    OperationsCalendarSelectedDateSummary Selected,
    DateTimeOffset GeneratedAt);
