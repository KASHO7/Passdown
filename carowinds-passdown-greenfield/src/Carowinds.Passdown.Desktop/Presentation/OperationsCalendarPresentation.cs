using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Domain.Catalog;

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
    string? ParkHoursStaleNoteText,
    string? WaterparkHoursText = null,
    string? DailyAdmissionPriceText = null)
{
    /// <summary>Phase 13D Part G: omitted cleanly (no blank placeholder) whenever the waterpark's hours were not published for the selected date.</summary>
    public bool HasWaterparkHours => WaterparkHoursText is not null;


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
            parkHoursPresentation.StaleNoteText,
            parkHoursPresentation.WaterparkHeadlineText,
            BuildDailyAdmissionPriceText(selected.DailyAdmission));
    }

    /// <summary>
    /// Phase 13D Part AH: never fabricates a price — each status maps to text that says exactly what
    /// is known, mirroring the same Not Published/Unavailable vocabulary already used elsewhere on
    /// this screen (never a new one). <see cref="PriceKind.StartingFrom"/> is the one qualifier
    /// surfaced explicitly (spec's own "is this exact for the selected date" signal) so a floor price
    /// is never read as if it were exact.
    /// </summary>
    private static string BuildDailyAdmissionPriceText(OperationsCalendarDailyAdmissionPrice? dailyAdmission) => dailyAdmission?.Status switch
    {
        OperationsCalendarDailyAdmissionStatus.Available when dailyAdmission.PriceAmount is { } amount =>
            // Phase 18A BUG-001: PriceFormatting (invariant "$0.00"), never the machine culture's "C" —
            // a PC with e.g. an English (UK) region format otherwise showed "£54.99" for a USD price.
            dailyAdmission.PriceKind == PriceKind.StartingFrom
                ? $"Starting from {PriceFormatting.Format(amount)}"
                : PriceFormatting.Format(amount),
        OperationsCalendarDailyAdmissionStatus.NotPublished => "Not Published",
        OperationsCalendarDailyAdmissionStatus.Unavailable => "Unavailable",
        _ => "Not checked yet",
    };
}
