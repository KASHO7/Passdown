using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 23B: one show in the Dashboard's "Today's Shows" — name, then its performance times, then its
/// venue, each on its own line so times can never run under the venue. A long time list starts collapsed
/// (<see cref="ShowTimesDisclosure"/>) and expands in place. <see cref="IsExpanded"/> is presentation-only
/// state: changing it reports to the parent (which keeps it in memory, keyed by official show id) and never
/// reads data, calls the network or saves anything.
/// </summary>
public partial class DashboardShowRowViewModel : ObservableObject
{
    private readonly Action<string, bool> _expansionChanged;

    public DashboardShowRowViewModel(ShowRowPresentation row, bool isExpanded, Action<string, bool> expansionChanged)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        _expansionChanged = expansionChanged ?? throw new ArgumentNullException(nameof(expansionChanged));
        _isExpanded = isExpanded && CanExpand;
    }

    public ShowRowPresentation Row { get; }

    public string Key => Row.Key;

    public string Name => Row.Name;

    public string? Venue => Row.Venue;

    public bool HasVenue => Row.HasVenue;

    public string? EarlierText => Row.EarlierText;

    public bool HasEarlierText => Row.HasEarlierText;

    /// <summary>True when the list is long enough to collapse; shorter lists show every time and no control.</summary>
    public bool CanExpand => ShowTimesDisclosure.CanCollapse(Row.Times.Count);

    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            var effective = value && CanExpand;
            if (SetProperty(ref _isExpanded, effective))
            {
                OnPropertyChanged(nameof(VisibleTimesText));
                OnPropertyChanged(nameof(HiddenCount));
                OnPropertyChanged(nameof(ToggleText));
                OnPropertyChanged(nameof(ToggleAccessibleName));
                _expansionChanged(Key, effective);
            }
        }
    }

    public int HiddenCount => ShowTimesDisclosure.HiddenCount(Row.Times.Count, IsExpanded);

    /// <summary>"11:15 AM · 11:30 AM · 11:45 AM · 12:00 PM" collapsed, every time expanded; a trailing " ·" leads into "+N more".</summary>
    public string VisibleTimesText
    {
        get
        {
            var text = ShowTimesDisclosure.Join(Row.Times.Take(ShowTimesDisclosure.VisibleCount(Row.Times.Count, IsExpanded)));
            return HiddenCount > 0 ? text + " ·" : text;
        }
    }

    public string ToggleText => IsExpanded ? "Show fewer" : $"+{HiddenCount} more";

    /// <summary>Names the show, so a screen reader never hears an unexplained "+10 more".</summary>
    public string ToggleAccessibleName => IsExpanded
        ? $"Show fewer performance times for {Name}"
        : $"Show all {Row.Times.Count} performance times for {Name}";

    /// <summary>The whole schedule in one phrase, so the times region is never an unexplained hidden fragment.</summary>
    public string TimesAccessibleName => $"Performance times for {Name}: {string.Join(", ", Row.Times)}";
}
