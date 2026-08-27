using Carowinds.Passdown.Application.Calendar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// One month-grid day cell (Phase 12A Part G/H), wrapping an <see cref="OperationsCalendarDay"/> with
/// the presentation-only fields the view binds to plus a select callback — the same
/// small-per-item-view-model-with-a-callback pattern <see cref="CustomProductRowViewModel"/> already
/// establishes, rather than a complex relative binding from inside an <c>ItemsControl</c> template back
/// up to the parent view model's command.
/// </summary>
public sealed partial class OperationsCalendarDayViewModel : ObservableObject
{
    private readonly Action<DateOnly> _onSelect;

    public DateOnly Date { get; }

    public int DayNumber => Date.Day;

    public bool IsInDisplayedMonth { get; }

    public bool IsToday { get; }

    public bool IsSelected { get; }

    public bool HasFinalizedPassdown { get; }

    public bool HasAssociateDraft { get; }

    public int RelevantOperationalNoteCount { get; }

    public bool HasOperationalNotes => RelevantOperationalNoteCount > 0;

    public string NoteCountText => RelevantOperationalNoteCount == 1 ? "1 note" : $"{RelevantOperationalNoteCount} notes";

    public string AutomationName { get; }

    public OperationsCalendarDayViewModel(OperationsCalendarDay day, bool isSelected, Action<DateOnly> onSelect)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(onSelect);
        _onSelect = onSelect;

        Date = day.Date;
        IsInDisplayedMonth = day.IsInDisplayedMonth;
        IsToday = day.IsToday;
        IsSelected = isSelected;
        HasFinalizedPassdown = day.HasFinalizedPassdown;
        HasAssociateDraft = day.HasAssociateDraft;
        RelevantOperationalNoteCount = day.RelevantOperationalNoteCount;
        AutomationName = BuildAutomationName(day, isSelected);
    }

    [RelayCommand]
    private void Select() => _onSelect(Date);

    /// <summary>Every visual indicator (today/selected/finalized/draft/notes) has a textual equivalent here (spec Part I) — status is never color-only.</summary>
    private static string BuildAutomationName(OperationsCalendarDay day, bool isSelected)
    {
        var parts = new List<string> { day.Date.ToString("MMMM d, yyyy") };

        if (day.IsToday)
        {
            parts.Add("Today");
        }

        if (isSelected)
        {
            parts.Add("Selected");
        }

        if (day.HasFinalizedPassdown)
        {
            parts.Add("Finalized");
        }

        if (day.HasAssociateDraft)
        {
            parts.Add("Draft in progress");
        }

        if (day.RelevantOperationalNoteCount > 0)
        {
            parts.Add(day.RelevantOperationalNoteCount == 1 ? "1 operational note" : $"{day.RelevantOperationalNoteCount} operational notes");
        }

        return string.Join(". ", parts);
    }
}
