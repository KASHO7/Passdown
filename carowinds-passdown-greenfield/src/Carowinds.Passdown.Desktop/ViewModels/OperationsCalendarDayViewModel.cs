using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Carowinds.Passdown.Domain;

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
    private readonly OperationsCalendarDay _day;
    private readonly OperationsCalendarOperatingStatePresentation _operatingState;
    private readonly OperationsCalendarPassdownPresentation _passdownPresentation;
    private readonly OperationsCalendarNotesPresentation _notesPresentation;

    public DateOnly Date { get; }

    public int DayNumber => Date.Day;

    /// <summary>
    /// Phase 15B: this date's Sunday(0)–Saturday(6) column within its week row — matches the
    /// Sun..Sat weekday-header order the grid has always used. Bound onto each generated day-cell
    /// container's <c>Grid.Column</c> attached property (see <c>OperationsCalendarView.axaml</c>'s
    /// per-week <see cref="OperationsCalendarWeekViewModel"/> row), which is what makes the real
    /// 7-column <c>Grid</c> row layout place each day in its correct column — never inferred from
    /// list position alone.
    /// </summary>
    public int ColumnIndex => (int)Date.DayOfWeek;

    public bool IsInDisplayedMonth { get; }

    public bool IsToday { get; }

    /// <summary>
    /// Phase 22F: settable by the Calendar so a newly chosen date is highlighted the moment it is chosen
    /// (before its detail load completes); <see cref="AutomationName"/> follows it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial bool IsSelected { get; set; }

    public bool HasFinalizedPassdown { get; }

    public bool HasAssociateDraft { get; }

    public int RelevantOperationalNoteCount { get; }

    public int ImportantOperationalNoteCount { get; }

    public bool HasOperationalNotes => RelevantOperationalNoteCount > 0;

    /// <summary>
    /// Phase 12E Part H: drives the day-cell notes line's restrained accent treatment — the same
    /// bold + <c>PassdownChipAccentForegroundBrush</c> "Important" convention already established by
    /// <c>TextBlock.priorityText.important</c> on Dashboard/Associate/Operational Notes, never a
    /// red/alarm color.
    /// </summary>
    public bool HasImportantOperationalNotes => ImportantOperationalNoteCount > 0;

    /// <summary>
    /// "No Draft"/"Draft"/"Finalized" (spec Phase 12C Part A/B/C/D) — Finalized always wins over an
    /// existing saved draft; see <see cref="OperationsCalendarPassdownPresentation.DeriveStatus"/> for
    /// the one place that precedence is decided.
    /// </summary>
    public string PassdownStatusLabel { get; }

    /// <summary>"2 Notes" / "2 Notes · 1 Important" (spec Part E/F/Z), or <see langword="null"/> when no relevant notes exist for this date — a day cell shows no line at all rather than a meaningless "0 Notes".</summary>
    public string? NotesSummaryText { get; }

    /// <summary>
    /// "CLOSED"/"CUSTOM"/"BUYOUT"/"NOT PUBLISHED"/"UNAVAILABLE", or <see langword="null"/> for a
    /// normal open day (spec Part D: hours alone communicate that state, so a redundant "OPEN" label
    /// is never shown) or a never-checked day (nothing to compactly label yet).
    /// </summary>
    public string? OperatingStateLabel { get; }

    /// <summary>"10 AM–10 PM", or <see langword="null"/> when the date has no effective hours to show (Closed/Not Published/Unavailable/Not checked yet).</summary>
    public string? EffectiveHoursLine { get; }

    public bool HasEffectiveHoursLine => EffectiveHoursLine is not null;

    /// <summary>
    /// Phase 22F: <see cref="EffectiveHoursLine"/> with non-breaking spaces before AM/PM, so a narrow cell wraps
    /// only after the en dash ("12 PM–" / "9 PM") and never strands "PM" on its own line. Display only.
    /// </summary>
    public string? EffectiveHoursLineDisplay => KeepMeridiemsAttached(EffectiveHoursLine);

    /// <summary>"Harbor 11–6", or <see langword="null"/> when Carolina Harbor's hours were not published for this date (Phase 13D Part D/E/F) — shown in the same cell as <see cref="EffectiveHoursLine"/>, never a separate badge.</summary>
    public string? WaterparkLine { get; }

    public bool HasWaterparkLine => WaterparkLine is not null;

    /// <summary>Phase 22F: <see cref="WaterparkLine"/> wrapped the same way as <see cref="EffectiveHoursLineDisplay"/>.</summary>
    public string? WaterparkLineDisplay => KeepMeridiemsAttached(WaterparkLine);

    private static string? KeepMeridiemsAttached(string? text) =>
        text?.Replace(" AM", "\u00A0AM", StringComparison.Ordinal).Replace(" PM", "\u00A0PM", StringComparison.Ordinal);

    public bool HasOperatingStateLabel => OperatingStateLabel is not null;

    /// <summary>Spec Part J: a compact, reused-not-recomputed staleness indicator — see <see cref="OperationsCalendarOperatingStatePresentation.IsStale"/>.</summary>
    public bool IsHoursStale { get; }

    public string AutomationName => BuildAutomationName(_day, IsSelected, _operatingState, _passdownPresentation, _notesPresentation)
        + (PassPerkAccessibilityText is { } perks ? ". " + perks : string.Empty);

    /// <summary>
    /// Phase 32A: this date's Pass Perks markers — at most one per marker type, always in the order qualifying
    /// visit, redemption, deadline, so the same type sits in the same place every time. Empty when none apply.
    /// </summary>
    public IReadOnlyList<PassPerkDayMarker> PassPerkMarkers { get; }

    public bool HasPassPerks => PassPerkMarkers.Count > 0;

    /// <summary>Tooltip and Narrator text for every marker on the date — never icon-only.</summary>
    public string? PassPerkAccessibilityText => PassPerkMarkers.Count == 0
        ? null
        : string.Join(". ", PassPerkMarkers.Select(m => m.AccessibilityText));

    public OperationsCalendarDayViewModel(OperationsCalendarDay day, bool isSelected, Action<DateOnly> onSelect, IReadOnlyList<PassPerkDayMarker>? passPerkMarkers = null)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(onSelect);
        _onSelect = onSelect;
        PassPerkMarkers = passPerkMarkers ?? [];

        Date = day.Date;
        IsInDisplayedMonth = day.IsInDisplayedMonth;
        IsToday = day.IsToday;
        IsSelected = isSelected;
        HasFinalizedPassdown = day.HasFinalizedPassdown;
        HasAssociateDraft = day.HasAssociateDraft;
        RelevantOperationalNoteCount = day.RelevantOperationalNoteCount;
        ImportantOperationalNoteCount = day.ImportantOperationalNoteCount;

        var operatingStatePresentation = OperationsCalendarOperatingStatePresentation.Build(day.OperatingState, day.ParkHours);
        OperatingStateLabel = operatingStatePresentation.CompactStateLabel;
        EffectiveHoursLine = operatingStatePresentation.CompactHoursLine;
        IsHoursStale = operatingStatePresentation.IsStale;
        WaterparkLine = operatingStatePresentation.CompactWaterparkLine;

        var passdownStatus = OperationsCalendarPassdownPresentation.DeriveStatus(day.HasFinalizedPassdown, day.HasAssociateDraft);
        var passdownPresentation = OperationsCalendarPassdownPresentation.Build(passdownStatus);
        PassdownStatusLabel = passdownPresentation.CellLabel;

        var notesPresentation = OperationsCalendarNotesPresentation.Build(day.RelevantOperationalNoteCount, day.ImportantOperationalNoteCount);
        NotesSummaryText = notesPresentation.CellText;

        _day = day;
        _operatingState = operatingStatePresentation;
        _passdownPresentation = passdownPresentation;
        _notesPresentation = notesPresentation;
    }

    [RelayCommand]
    private void Select() => _onSelect(Date);

    /// <summary>
    /// Every visual indicator (operating state/hours/today/selected/passdown status/notes) has a
    /// textual equivalent here (spec Phase 12B Part X; Phase 12C Part AB) — status is never
    /// color-only. Phase 12C Part B: the passdown clause reflects the same Finalized-over-Draft
    /// precedence the visible cell label uses — a finalized date's automation name says only
    /// "Finalized," never also "Draft in progress."
    /// </summary>
    private static string BuildAutomationName(
        OperationsCalendarDay day,
        bool isSelected,
        OperationsCalendarOperatingStatePresentation operatingState,
        OperationsCalendarPassdownPresentation passdownPresentation,
        OperationsCalendarNotesPresentation notesPresentation)
    {
        var parts = new List<string>
        {
            day.Date.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates),
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

        parts.Add(passdownPresentation.AccessibilityText);
        parts.Add(notesPresentation.AccessibilityText);

        return string.Join(". ", parts);
    }
}

/// <summary>
/// Phase 32A: one Pass Perks marker in a day cell — its type (shape), the perks it stands for, a small count
/// badge when several perks share the type, and its accessible name/tooltip.
/// </summary>
public sealed record PassPerkDayMarker(Domain.PassPerks.PassPerkMarkerKind Kind, IReadOnlyList<string> Titles)
{
    public bool IsQualifyingVisit => Kind == Domain.PassPerks.PassPerkMarkerKind.QualifyingVisit;

    public bool IsRedemption => Kind == Domain.PassPerks.PassPerkMarkerKind.Redemption;

    public bool IsDeadline => Kind == Domain.PassPerks.PassPerkMarkerKind.Deadline;

    /// <summary>"2" when more than one perk shares this marker type on the date; empty otherwise.</summary>
    public string CountText => Titles.Count > 1 ? Titles.Count.ToString(PresentationCulture.UnitedStates) : string.Empty;

    public bool HasCount => Titles.Count > 1;

    /// <summary>"Pass perk redemption date", "Pass perk qualifying visit date" or "Pass perk booking deadline".</summary>
    public string Label => LabelFor(Kind);

    /// <summary>"Pass perk redemption date: Buy Two, Get One FREE Hot Chocolate Mug" (tooltip and Narrator).</summary>
    public string AccessibilityText => Titles.Count switch
    {
        1 => $"{Label}: {Titles[0]}",
        _ => $"{Label}s ({Titles.Count}): {string.Join(", ", Titles)}",
    };

    public static string LabelFor(Domain.PassPerks.PassPerkMarkerKind kind) => kind switch
    {
        Domain.PassPerks.PassPerkMarkerKind.QualifyingVisit => "Pass perk qualifying visit date",
        Domain.PassPerks.PassPerkMarkerKind.Deadline => "Pass perk booking deadline",
        _ => "Pass perk redemption date",
    };
}
