using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Application.Calendar;

/// <summary>
/// A date's park-hours operating state for the Operations Calendar (Phase 12B Part B) — its own
/// vocabulary for Calendar's compact overlays, structurally identical to
/// <c>Dashboard.DashboardParkStatus</c> (Phase 10A) and computed the same way (a pure classification
/// over an already-composed <see cref="AssociateParkHoursView"/>'s <c>Effective.Status</c>/
/// <c>IsManualOverrideApplied</c>/<c>IsPrivateBuyoutApplied</c>/<c>Verification.State</c>), but defined
/// independently rather than reused directly — Calendar and Dashboard are peer top-level features
/// (spec Phase 12A precedent: <c>OperationsCalendarPassdownStatus</c> was kept independent of
/// <c>DashboardPassdownStatus</c> for the same reason). Every value here reuses the exact same
/// <see cref="Domain.Hours.EffectiveHours"/>/<see cref="Domain.Hours.ParkHoursCalculator"/> output —
/// never a second hours engine (spec Phase 12B Part A).
/// </summary>
public enum OperationsCalendarOperatingState
{
    NotChecked,
    Closed,
    Open,
    CustomHours,
    PrivateBuyout,
    NotPublished,
    Unavailable,
}

/// <summary>
/// One day cell in the Operations Calendar's month grid (Phase 12A Part D/E; extended Phase 12B Part
/// B) — intentionally modest: enough for a day cell to show a number, a compact operating-state/hours
/// line, and a small set of indicators, never a second projection of Associate/finalization/notes
/// business logic. <see cref="HasAssociateDraft"/> reflects only whether a draft row was ever saved
/// for the date (the same signal <see cref="Finalization.ScheduledPassdownFinalizer"/> already uses to
/// decide finalization eligibility) — not whether it currently has any particular content.
/// <see cref="ParkHours"/> is the exact <see cref="AssociateParkHoursView"/>
/// <see cref="AssociateParkHoursComposer.Compose"/> produced for this date (spec Phase 12B Part A) —
/// carried alongside <see cref="OperatingState"/> so the Desktop presentation layer can reuse
/// <c>Desktop.Presentation.ParkHoursPresentation.Build</c> directly (the same formatter Associate
/// Overview and Dashboard already use) instead of a second hours-text formatter.
/// <see cref="ImportantOperationalNoteCount"/> (Phase 12C Part E/F) is a subset of
/// <see cref="RelevantOperationalNoteCount"/> — the same relevant notes, further filtered to
/// <c>OperationalNotePriority.Important</c> — computed from the exact same in-memory note list, never
/// a second query. Appended with a default so every pre-12C call site keeps compiling unchanged.
/// </summary>
public sealed record OperationsCalendarDay(
    DateOnly Date,
    bool IsInDisplayedMonth,
    bool IsToday,
    bool HasFinalizedPassdown,
    bool HasAssociateDraft,
    int RelevantOperationalNoteCount,
    OperationsCalendarOperatingState OperatingState,
    AssociateParkHoursView ParkHours,
    int ImportantOperationalNoteCount = 0);

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

/// <summary>
/// The concise detail panel content for whichever date is currently selected (Phase 12A Part E;
/// extended Phase 12B Part K). <see cref="ParkHours"/>/<see cref="OperatingState"/> mirror
/// <see cref="OperationsCalendarDay"/>'s own fields, computed by the exact same
/// <see cref="AssociateParkHoursComposer.Compose"/> call the composed <c>AssociateWorkspace</c> for
/// this date already used (Phase 12B Part A) — never a second composition.
/// </summary>
public sealed record OperationsCalendarSelectedDateSummary(
    DateOnly Date,
    bool IsToday,
    OperationsCalendarPassdownStatus PassdownStatus,
    DateTimeOffset? LastSavedAt,
    DateTimeOffset? FinalizedAt,
    int RelevantOperationalNoteCount,
    OperationsCalendarOperatingState OperatingState,
    AssociateParkHoursView ParkHours,
    int ImportantOperationalNoteCount = 0);

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
