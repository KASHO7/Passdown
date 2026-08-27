using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Display-only text for the Operations Calendar's header and selected-date summary panel (Phase 12A
/// Part G/H; extended Phase 12B Part K) — built once from <see cref="OperationsCalendarSnapshot"/>,
/// never recomputing any business rule (finalization/relevance/draft/hours status all come straight
/// from the snapshot). Park-hours text reuses <see cref="ParkHoursPresentation.Build"/> directly — the
/// exact same formatter Associate Overview and the Dashboard already use — rather than a second hours
/// formatter (spec Part K/U).
/// </summary>
public sealed record OperationsCalendarPresentation(
    string DisplayedMonthText,
    string SelectedDateText,
    bool SelectedDateIsToday,
    string PassdownStatusText,
    string? FinalizedText,
    string OperationalNotesCountText,
    string ParkStatusText,
    string? EffectiveHoursText,
    string? ParkHoursVerificationText,
    string? ParkHoursStaleNoteText)
{
    public static OperationsCalendarPresentation Build(OperationsCalendarSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var selected = snapshot.Selected;

        var passdownStatusText = selected.PassdownStatus switch
        {
            OperationsCalendarPassdownStatus.NoDraft => "No draft",
            OperationsCalendarPassdownStatus.DraftInProgress => "Draft in progress",
            OperationsCalendarPassdownStatus.Finalized => "Finalized",
            _ => "Unknown",
        };

        var finalizedText = selected.FinalizedAt is { } finalizedAt
            ? $"Finalized {EasternTimeZone.ConvertFromUtc(finalizedAt):MMM d, yyyy 'at' h:mm tt}"
            : null;

        var notesCountText = selected.RelevantOperationalNoteCount switch
        {
            0 => "No operational notes for this date.",
            1 => "1 operational note for this date.",
            _ => $"{selected.RelevantOperationalNoteCount} operational notes for this date.",
        };

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
            passdownStatusText,
            finalizedText,
            notesCountText,
            parkStatusText,
            showsHours ? parkHoursPresentation.HeadlineText : null,
            parkHoursPresentation.VerificationText,
            parkHoursPresentation.StaleNoteText);
    }
}
