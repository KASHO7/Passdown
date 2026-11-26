using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Domain.Shows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 21C: one selectable show in the Associate Shows section. Wraps the Application-reconciled
/// <see cref="AssociateShowRow"/>; the only thing the supervisor changes here is <see cref="IsSelected"/>
/// (all of a selected show's verified times print). Rebuilt only when the parent rebuilds from a newly
/// composed workspace; a click mutates this instance and schedules the parent's autosave.
/// <para>
/// Phase 35D: a long schedule shows its first times plus "+N more" (the Dashboard's
/// <see cref="ShowTimesDisclosure"/> rule) and expands in place to every time. <see cref="IsExpanded"/> is
/// presentation-only: it never touches <see cref="IsSelected"/>, never saves and never reads data — it only
/// reports to the parent, which remembers it by official show id for the date on screen.
/// </para>
/// </summary>
public partial class AssociateShowRowViewModel : ObservableObject
{
    private readonly Action _notifyChanged;
    private readonly Action<string, bool>? _expansionChanged;
    private bool _suppressNotify;

    public AssociateShowRowViewModel(AssociateShowRow row, Action notifyChanged, bool isExpanded = false, Action<string, bool>? expansionChanged = null)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        _notifyChanged = notifyChanged ?? throw new ArgumentNullException(nameof(notifyChanged));
        _expansionChanged = expansionChanged;
        AllTimes = ShowTimes.Normalize(row.Times).Select(ShowTimes.Format).ToList();
        _isExpanded = isExpanded && CanExpand;
        _suppressNotify = true;
        IsSelected = row.IsSelected;
        _suppressNotify = false;
    }

    public AssociateShowRow Row { get; }

    /// <summary>The official show id — what the parent keys expansion state on, never a list index.</summary>
    public string Key => Row.ShowId;

    public string Name => Row.Name;

    public string TimesText => ShowTimes.FormatList(Row.Times);

    public string? Venue => Row.Venue;

    public bool HasVenue => Row.Venue is not null;

    /// <summary>Phase 35D: every performance time, in operating-day order, one entry each — nothing dropped.</summary>
    public IReadOnlyList<string> AllTimes { get; }

    public bool HasTimes => AllTimes.Count > 0;

    /// <summary>Phase 35D: true when the schedule is long enough to collapse (more than five times); shorter lists show every time and no control.</summary>
    public bool CanExpand => ShowTimesDisclosure.CanCollapse(AllTimes.Count);

    /// <summary>
    /// Phase 35D: the times column — every time for a short list; the first three for a long one (the rest are
    /// counted by <see cref="MoreTimesText"/> and listed in full when expanded). Stays the same when expanded,
    /// so expanding never moves the row's first line.
    /// </summary>
    public string TimesPreviewText => ShowTimesDisclosure.Join(AllTimes.Take(PreviewCount));

    /// <summary>
    /// Phase 35D: a long schedule previews its first <see cref="CollapsedPreviewCount"/> times (the approved Shows
    /// reference; the times column is narrower than the Dashboard's). Whether a list is long enough to collapse is
    /// still the shared <see cref="ShowTimesDisclosure.CanCollapse"/> rule, so "+N more" always hides at least 3.
    /// </summary>
    public const int CollapsedPreviewCount = 3;

    private int PreviewCount => CanExpand ? CollapsedPreviewCount : AllTimes.Count;

    /// <summary>" · +13 more" for a long schedule; empty otherwise.</summary>
    public string MoreTimesText => AllTimes.Count > PreviewCount ? $" · +{AllTimes.Count - PreviewCount} more" : string.Empty;

    public bool HasMoreTimes => MoreTimesText.Length > 0;

    /// <summary>"All 16 scheduled performances" — the heading of the expanded list.</summary>
    public string AllTimesHeading => $"All {AllTimes.Count} scheduled performances";

    /// <summary>The whole schedule in one phrase, so the shortened times column is never an unexplained fragment.</summary>
    public string TimesAccessibleName => AllTimes.Count == 0
        ? $"No performance times for {Name}"
        : $"Performance times for {Name}: {string.Join(", ", AllTimes)}";

    public string ToggleText => IsExpanded ? "Less" : "View times";

    /// <summary>Names the show and the count, so a screen reader never hears an unexplained "View times".</summary>
    public string ToggleAccessibleName => IsExpanded
        ? $"Show fewer performance times for {Name}"
        : $"Show all {AllTimes.Count} performance times for {Name}";

    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            var effective = value && CanExpand;
            if (SetProperty(ref _isExpanded, effective))
            {
                OnPropertyChanged(nameof(ToggleText));
                OnPropertyChanged(nameof(ToggleAccessibleName));
                _expansionChanged?.Invoke(Key, effective);
            }
        }
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>Why a selected row is flagged — never shown for an ordinary current row.</summary>
    public string? StateNote => Row.State switch
    {
        AssociateShowRowState.TimesChanged => "Official times changed since this show was selected.",
        AssociateShowRowState.NoLongerListed => "No longer on the official schedule for this date — won't print.",
        AssociateShowRowState.NotOnCurrentSchedule => "Not on the current official schedule — won't print.",
        AssociateShowRowState.SavedCopy => "Official schedule unavailable — using the times saved when selected.",
        _ => null,
    };

    public bool HasStateNote => StateNote is not null;

    /// <summary>Phase 35D: the venue line — the published venue, or plainly that none is listed (never a guess).</summary>
    public string VenueText => Venue ?? "Location not listed";

    public string AccessibleName => $"Include {Name} on the passdown, {TimesText}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_suppressNotify)
        {
            _notifyChanged();
        }
    }

    /// <summary>Sets the selection as one edit (Select all / Clear).</summary>
    internal void SetSelected(bool value) => IsSelected = value;
}
