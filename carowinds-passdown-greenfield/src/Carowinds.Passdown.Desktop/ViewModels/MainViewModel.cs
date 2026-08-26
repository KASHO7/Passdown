using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>Which top-level app-shell section is currently shown (spec Phase 9D Part A; extended Phase 10A Part A, Phase 11B Part A). Manager stays deferred per CLAUDE.md — there is no such value.</summary>
public enum AppSection
{
    Dashboard,
    AssociatePassdown,
    OperationalNotes,
    History,
}

/// <summary>
/// The application shell's state (Phase 4B Part A; extended by Phase 9D Part A, Phase 10A Part A/P).
/// Associate Passdown remains the only editable workflow (CLAUDE.md "current scope decision" —
/// Manager is deferred); Phase 9D added History as a second, read-only top-level section; Phase 10A
/// adds Dashboard as a third — a summary/control surface, not another editing form — and makes it
/// the default startup section (spec Phase 10A Part Q). All three child view models are constructed
/// once and kept alive for the whole app lifetime, so switching <see cref="SelectedSection"/> only
/// toggles which one is visible (spec Part T): it never recreates <see cref="Associate"/>'s
/// in-progress edits/autosave state, and it never touches <see cref="AssociateViewModel.SelectedDate"/>.
/// <see cref="Dashboard"/>'s own navigation actions are plain events (see
/// <see cref="OperationsDashboardViewModel.OpenAssociateOverviewRequested"/>/
/// <see cref="OperationsDashboardViewModel.OpenAssociateNotesRequested"/>/
/// <see cref="OperationsDashboardViewModel.OpenHistoryRequested"/>) that this type — the one owner of
/// top-level navigation (spec Part P) — subscribes to and turns into a section switch, so the
/// Dashboard view model itself never needs to know about <see cref="AppSection"/> or
/// <see cref="MainViewModel"/>. Phase 10C extends this to a true Notes deep link: this type also sets
/// <see cref="AssociateViewModel.SelectedSection"/> — Associate's own internal Overview/Products/Notes
/// rail — so "Open Passdown"/"Open Notes" land on the right internal section without either view model
/// reaching into the other's control/visual tree.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    public MainViewModel(OperationsDashboardViewModel dashboard, AssociateViewModel associate, OperationalNotesViewModel notes, PassdownHistoryViewModel history)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(associate);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(history);

        Dashboard = dashboard;
        Associate = associate;
        Notes = notes;
        History = history;

        // "Open Passdown"/"Open Notes" (spec Phase 10C Parts F/G/H): switch the top-level section and
        // set Associate's own internal section explicitly — never Associate.SelectedDate, so an
        // associate mid-edit on another date is never silently switched to today's record.
        Dashboard.OpenAssociateOverviewRequested += (_, _) =>
        {
            Associate.SelectedSection = AssociateSection.Overview;
            ShowAssociateSection();
        };
        Dashboard.OpenAssociateNotesRequested += (_, _) =>
        {
            Associate.SelectedSection = AssociateSection.Notes;
            ShowAssociateSection();
        };
        Dashboard.OpenHistoryRequested += (_, _) => ShowHistorySection();

        // Phase 11B Part X: the smallest Notes-specific unsaved-changes guard, not a generic
        // navigation-guard framework. OperationalNotesViewModel.IsEditing is the one signal this type
        // observes; whenever it changes, the three other section buttons' enabled state is
        // re-evaluated so a supervisor mid-create/edit cannot silently navigate away and lose the
        // in-progress note (spec Part W's "disable changing selection while dirty" applied one level
        // up, to top-level navigation).
        Notes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperationalNotesViewModel.IsEditing))
            {
                ShowDashboardSectionCommand.NotifyCanExecuteChanged();
                ShowAssociateSectionCommand.NotifyCanExecuteChanged();
                ShowHistorySectionCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public OperationsDashboardViewModel Dashboard { get; }

    public AssociateViewModel Associate { get; }

    public OperationalNotesViewModel Notes { get; }

    public PassdownHistoryViewModel History { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsAssociateSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsOperationalNotesSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsHistorySectionSelected))]
    [NotifyCanExecuteChangedFor(nameof(ShowDashboardSectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowAssociateSectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowHistorySectionCommand))]
    public partial AppSection SelectedSection { get; set; } = AppSection.Dashboard;

    public bool IsDashboardSectionSelected => SelectedSection == AppSection.Dashboard;

    public bool IsAssociateSectionSelected => SelectedSection == AppSection.AssociatePassdown;

    public bool IsOperationalNotesSectionSelected => SelectedSection == AppSection.OperationalNotes;

    public bool IsHistorySectionSelected => SelectedSection == AppSection.History;

    [RelayCommand(CanExecute = nameof(CanLeaveOperationalNotesSection))]
    private void ShowDashboardSection() => SelectedSection = AppSection.Dashboard;

    [RelayCommand(CanExecute = nameof(CanLeaveOperationalNotesSection))]
    private void ShowAssociateSection() => SelectedSection = AppSection.AssociatePassdown;

    [RelayCommand]
    private void ShowOperationalNotesSection() => SelectedSection = AppSection.OperationalNotes;

    [RelayCommand(CanExecute = nameof(CanLeaveOperationalNotesSection))]
    private void ShowHistorySection() => SelectedSection = AppSection.History;

    /// <summary>Spec Part X: true unless the Operational Notes section is both currently shown and mid-create/edit — in which case a supervisor must Save or Cancel before any other top-level section becomes reachable.</summary>
    private bool CanLeaveOperationalNotesSection() => !(SelectedSection == AppSection.OperationalNotes && Notes.IsEditing);
}
