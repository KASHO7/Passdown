using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain;

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
    string? DailyAdmissionPriceText = null,
    string? DailyAdmissionPriceAccessibleText = null)
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
            snapshot.DisplayedMonth.ToString("MMMM yyyy", PresentationCulture.UnitedStates),
            selected.Date.ToString("dddd, MMMM d, yyyy", PresentationCulture.UnitedStates),
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
            BuildDailyAdmissionPriceText(selected.DailyAdmission),
            BuildDailyAdmissionPriceAccessibleText(selected.DailyAdmission));
    }

    /// <summary>
    /// Phase 37E D-1 (owner-approved 2026-11-28): a price the official calendar no longer lists for the date (its sales
    /// window ended or it was withdrawn) is the date's last verified price, not one currently on sale. The compact line
    /// says so in the 290 px panel ("$49.00 · last verified Nov 27"), using the product's original verification date on
    /// the Eastern calendar, the same evidence Products &amp; Pricing words as "Last verified … — sales availability not
    /// confirmed." Every other state keeps its existing text.
    /// </summary>
    private static string? PreviouslyVerifiedSuffix(OperationsCalendarDailyAdmissionPrice? dailyAdmission) =>
        dailyAdmission is { Status: OperationsCalendarDailyAdmissionStatus.Available, Verification.State: AssociateVerificationState.PreviouslyVerified } admission
            ? admission.Verification!.LastVerifiedAt is { } at
                ? $"last verified {EasternTimeZone.ConvertFromUtc(at).ToString("MMM d", PresentationCulture.UnitedStates)}"
                : "previously verified"
            : null;

    /// <summary>
    /// Phase 37E D-1: the screen-reader name for the Daily ticket line. For a previously verified price it adds what the
    /// compact text leaves out ("availability not confirmed"); otherwise it is the existing "Daily ticket price: …".
    /// </summary>
    private static string BuildDailyAdmissionPriceAccessibleText(OperationsCalendarDailyAdmissionPrice? dailyAdmission)
    {
        var text = BuildDailyAdmissionPriceText(dailyAdmission);
        if (dailyAdmission is { Status: OperationsCalendarDailyAdmissionStatus.Available, Verification.State: AssociateVerificationState.PreviouslyVerified } admission)
        {
            var amount = text.Split(" · ", 2)[0];
            var when = admission.Verification!.LastVerifiedAt is { } at
                ? $"last verified {EasternTimeZone.ConvertFromUtc(at).ToString("MMMM d", PresentationCulture.UnitedStates)}"
                : "previously verified";
            return $"Daily ticket price: {amount}, {when}, availability not confirmed";
        }

        return $"Daily ticket price: {text}";
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
            WithSuffix(
                dailyAdmission.PriceKind == PriceKind.StartingFrom
                    ? $"Starting from {PriceFormatting.Format(amount)}"
                    : PriceFormatting.Format(amount),
                PreviouslyVerifiedSuffix(dailyAdmission)),
        OperationsCalendarDailyAdmissionStatus.NotPublished => "Not Published",
        OperationsCalendarDailyAdmissionStatus.Unavailable => "Unavailable",
        _ => "Not checked yet",
    };

    private static string WithSuffix(string price, string? suffix) => suffix is null ? price : $"{price} · {suffix}";
}
