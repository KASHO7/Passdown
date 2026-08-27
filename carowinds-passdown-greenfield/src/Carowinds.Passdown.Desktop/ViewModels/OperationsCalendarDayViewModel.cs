using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// One month-grid day cell (Phase 12A Part G/H; extended Phase 12B Part B/C/U), wrapping an
/// <see cref="OperationsCalendarDay"/> with the presentation-only fields the view binds to plus a
/// select callback — the same small-per-item-view-model-with-a-callback pattern
/// <see cref="CustomProductRowViewModel"/> already establishes, rather than a complex relative binding
/// from inside an <c>ItemsControl</c> template back up to the parent view model's command.
/// <see cref="OperatingState"/> presentation reuses <see cref="OperationsCalendarOperatingStatePresentation.Build"/>
/// — this type never recomputes hours or classifies operating state itself.
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

    /// <summary>
    /// "CLOSED"/"CUSTOM"/"BUYOUT"/"NOT PUBLISHED"/"UNAVAILABLE", or <see langword="null"/> for a
    /// normal open day (spec Part D: hours alone communicate that state, so a redundant "OPEN" label
    /// is never shown) or a never-checked day (nothing to compactly label yet).
    /// </summary>
    public string? OperatingStateLabel { get; }

    /// <summary>"10 AM–10 PM", or <see langword="null"/> when the date has no effective hours to show (Closed/Not Published/Unavailable/Not checked yet).</summary>
    public string? EffectiveHoursLine { get; }

    public bool HasEffectiveHoursLine => EffectiveHoursLine is not null;

    public bool HasOperatingStateLabel => OperatingStateLabel is not null;

    /// <summary>Spec Part J: a compact, reused-not-recomputed staleness indicator — see <see cref="OperationsCalendarOperatingStatePresentation.IsStale"/>.</summary>
    public bool IsHoursStale { get; }

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

        var operatingStatePresentation = OperationsCalendarOperatingStatePresentation.Build(day.OperatingState, day.ParkHours);
        OperatingStateLabel = operatingStatePresentation.CompactStateLabel;
        EffectiveHoursLine = operatingStatePresentation.CompactHoursLine;
        IsHoursStale = operatingStatePresentation.IsStale;

        AutomationName = BuildAutomationName(day, isSelected, operatingStatePresentation);
    }

    [RelayCommand]
    private void Select() => _onSelect(Date);

    /// <summary>Every visual indicator (operating state/hours/today/selected/finalized/draft/notes) has a textual equivalent here (spec Phase 12B Part X) — status is never color-only.</summary>
    private static string BuildAutomationName(OperationsCalendarDay day, bool isSelected, OperationsCalendarOperatingStatePresentation operatingState)
    {
        var parts = new List<string>
        {
            day.Date.ToString("MMMM d, yyyy"),
            operatingState.AccessibilityText,
        };

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
