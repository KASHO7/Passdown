using Carowinds.Passdown.Application.Calendar;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Display-only text for the Operations Calendar's header and selected-date summary panel (Phase 12A
/// Part G/H; extended Phase 12B Part K; extended Phase 12C Part J/T/Y/Z) — built once from
/// <see cref="OperationsCalendarSnapshot"/>, never recomputing any business rule (finalization/
/// relevance/draft/hours status all come straight from the snapshot). Park-hours text reuses
/// <see cref="ParkHoursPresentation.Build"/> directly, passdown-status text reuses
/// <see cref="OperationsCalendarPassdownPresentation.Build"/>, and notes-count text reuses
/// <see cref="OperationsCalendarNotesPresentation.Build"/> — never a second formatter for any of the
/// three.
/// </summary>
public sealed record OperationsCalendarPresentation(
    string DisplayedMonthText,
    string SelectedDateText,
    bool SelectedDateIsToday,
    string PassdownStatusText,
    string? PassdownDetailNoteText,
    string OperationalNotesCountText,
    string? OperationalNotesImportantText,
    string ParkStatusText,
    string? EffectiveHoursText,
    string? ParkHoursVerificationText,
    string? ParkHoursStaleNoteText)
{
    public static OperationsCalendarPresentation Build(OperationsCalendarSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var selected = snapshot.Selected;

        // Phase 12C Part Y: the sole source of the "No Draft"/"Draft In Progress"/"Finalized" detail
        // label and its accompanying note (last-saved/finalized timestamp, or an empty-state sentence).
        var passdownPresentation = OperationsCalendarPassdownPresentation.Build(selected.PassdownStatus, selected.LastSavedAt, selected.FinalizedAt);

        // Phase 12C Part T: labeled "Active Operational Notes" throughout this panel — the persistent
        // Operational Notes concept, never the daily Associate draft's own Notes entry.
        var notesPresentation = OperationsCalendarNotesPresentation.Build(selected.RelevantOperationalNoteCount, selected.ImportantOperationalNoteCount);

        var parkStatusText = selected.OperatingState switch
        {
            OperationsCalendarOperatingState.Open => "Open",
            OperationsCalendarOperatingState.Closed => "Closed",
            OperationsCalendarOperatingState.CustomHours => "Custom Hours",
            OperationsCalendarOperatingState.PrivateBuyout => "Private Buyout",
            OperationsCalendarOperatingState.NotPublished => "Not Published",
            OperationsCalendarOperatingState.Unavailable => "Unavailable",
            _ => "Not checked yet",
        };

        // EffectiveHoursStatus is Open for all three of Open/CustomHours/PrivateBuyout (they differ
        // only by which override was applied — spec §8.2), so ParkHoursPresentation's own headline
        // ("10:00 AM – 10:00 PM") is already correct for every one of them without a second formatter.
        var parkHoursPresentation = ParkHoursPresentation.Build(selected.ParkHours);
        var showsHours = selected.OperatingState is OperationsCalendarOperatingState.Open
            or OperationsCalendarOperatingState.CustomHours
            or OperationsCalendarOperatingState.PrivateBuyout;

        return new OperationsCalendarPresentation(
            snapshot.DisplayedMonth.ToString("MMMM yyyy"),
            selected.Date.ToString("dddd, MMMM d, yyyy"),
            selected.IsToday,
            passdownPresentation.DetailLabel,
            passdownPresentation.DetailNoteText,
            notesPresentation.DetailCountText,
            notesPresentation.DetailImportantText,
            parkStatusText,
            showsHours ? parkHoursPresentation.HeadlineText : null,
            parkHoursPresentation.VerificationText,
            parkHoursPresentation.StaleNoteText);
    }
}
