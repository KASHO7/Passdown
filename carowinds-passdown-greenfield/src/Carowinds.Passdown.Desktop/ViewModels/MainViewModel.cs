using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>Which top-level app-shell section is currently shown (spec Phase 9D Part A). Manager stays deferred per CLAUDE.md — there is no third value.</summary>
public enum AppSection
{
    AssociatePassdown,
    History,
}

/// <summary>
/// The application shell's state (Phase 4B Part A; extended by Phase 9D Part A). Associate Passdown
/// remains the only editable workflow (CLAUDE.md "current scope decision" — Manager is deferred); Phase
/// 9D adds History as a second, read-only top-level section alongside it — never a Manager placeholder.
/// Both child view models are constructed once and kept alive for the whole app lifetime, so switching
/// <see cref="SelectedSection"/> only toggles which one is visible (spec Part T): it never recreates
/// <see cref="Associate"/>'s in-progress edits/autosave state, and it never touches
/// <see cref="AssociateViewModel.SelectedDate"/>.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    public MainViewModel(AssociateViewModel associate, PassdownHistoryViewModel history)
    {
        ArgumentNullException.ThrowIfNull(associate);
        ArgumentNullException.ThrowIfNull(history);

        Associate = associate;
        History = history;
    }

    public AssociateViewModel Associate { get; }

    public PassdownHistoryViewModel History { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssociateSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsHistorySectionSelected))]
    public partial AppSection SelectedSection { get; set; } = AppSection.AssociatePassdown;

    public bool IsAssociateSectionSelected => SelectedSection == AppSection.AssociatePassdown;

    public bool IsHistorySectionSelected => SelectedSection == AppSection.History;

    [RelayCommand]
    private void ShowAssociateSection() => SelectedSection = AppSection.AssociatePassdown;

    [RelayCommand]
    private void ShowHistorySection() => SelectedSection = AppSection.History;
}
