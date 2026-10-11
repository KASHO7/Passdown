using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Display-only text for one date's <see cref="OperationsCalendarPassdownStatus"/> (Phase 12C Part Y) —
/// the single place a compact day-cell label, the selected-date detail label/note, and the
/// accessibility clause are all derived from. Never queries a repository, never decides a persistence
/// rule, and never creates a draft; it only maps an already-computed status (plus, for the detail note,
/// an already-composed timestamp) to text.
/// </summary>
public sealed record OperationsCalendarPassdownPresentation(
    string CellLabel,
    string DetailLabel,
    string? DetailNoteText,
    string AccessibilityText)
{
    /// <summary>
    /// Finalized always wins over an existing saved draft (spec Phase 12C Part B) — a date's underlying
    /// draft row may still exist per current app semantics even after finalization, but the Calendar's
    /// compact status represents the date's *operational lifecycle*, not raw persisted rows. This is
    /// the one place that precedence is decided, reused by both the day-cell grid (from its two raw
    /// flags) and the selected-date summary (which already carries the equivalent
    /// <see cref="OperationsCalendarPassdownStatus"/> computed the same way in
    /// <see cref="OperationsCalendarService"/>).
    /// </summary>
    public static OperationsCalendarPassdownStatus DeriveStatus(bool hasFinalizedPassdown, bool hasAssociateDraft) =>
        hasFinalizedPassdown ? OperationsCalendarPassdownStatus.Finalized
        : hasAssociateDraft ? OperationsCalendarPassdownStatus.DraftInProgress
        : OperationsCalendarPassdownStatus.NoDraft;

    /// <summary>
    /// <paramref name="lastSavedAt"/>/<paramref name="finalizedAt"/> are optional — a day cell has
    /// neither (it only needs <see cref="CellLabel"/>/<see cref="AccessibilityText"/>), while the
    /// selected-date summary supplies both from <see cref="OperationsCalendarSelectedDateSummary"/> for
    /// its richer <see cref="DetailNoteText"/> line. Wording deliberately mirrors
    /// <c>Desktop.Presentation.OperationsDashboardPresentation</c>'s own "No Draft"/"Draft In
    /// Progress"/"Finalized" vocabulary (spec Part D: "use consistent wording with existing Dashboard
    /// where possible").
    /// </summary>
    public static OperationsCalendarPassdownPresentation Build(
        OperationsCalendarPassdownStatus status, DateTimeOffset? lastSavedAt = null, DateTimeOffset? finalizedAt = null) =>
        status switch
        {
            OperationsCalendarPassdownStatus.Finalized => new(
                "Finalized",
                "Finalized",
                finalizedAt is { } f ? $"Finalized {FormatTimestamp(f)}" : "Finalized passdown available in History.",
                "Finalized"),
            OperationsCalendarPassdownStatus.DraftInProgress => new(
                "Draft",
                "Draft In Progress",
                lastSavedAt is { } s ? $"Last saved {FormatTimestamp(s)}" : null,
                "Draft in progress"),
            OperationsCalendarPassdownStatus.NoDraft => new(
                "No Draft",
                "No Draft",
                "No passdown started.",
                "No draft"),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    private static string FormatTimestamp(DateTimeOffset value) =>
        EasternTimeZone.ConvertFromUtc(value).ToString("MMM d, yyyy 'at' h:mm tt", PresentationCulture.UnitedStates);
}
