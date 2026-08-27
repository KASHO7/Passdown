using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Display-only text for the Operations Calendar's header and selected-date summary panel (Phase 12A
/// Part G/H) — built once from <see cref="OperationsCalendarSnapshot"/>, never recomputing any
/// business rule (finalization/relevance/draft status all come straight from the snapshot).
/// </summary>
public sealed record OperationsCalendarPresentation(
    string DisplayedMonthText,
    string SelectedDateText,
    bool SelectedDateIsToday,
    string PassdownStatusText,
    string? FinalizedText,
    string OperationalNotesCountText)
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

        return new OperationsCalendarPresentation(
            snapshot.DisplayedMonth.ToString("MMMM yyyy"),
            selected.Date.ToString("dddd, MMMM d, yyyy"),
            selected.IsToday,
            passdownStatusText,
            finalizedText,
            notesCountText);
    }
}
