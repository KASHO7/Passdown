using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>Which top-level app-shell section is currently shown (spec Phase 9D Part A; extended Phase 10A Part A, Phase 11B Part A, Phase 16B). Manager stays deferred per CLAUDE.md — there is no such value.</summary>
public enum AppSection
{
    Dashboard,
    OperationsCalendar,
    AssociatePassdown,
    OperationalNotes,
    History,
    Settings,
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
    public MainViewModel(OperationsDashboardViewModel dashboard, OperationsCalendarViewModel calendar, AssociateViewModel associate, OperationalNotesViewModel notes, PassdownHistoryViewModel history, PersonalSyncSettingsViewModel settings, PriceHistoryViewModel? priceHistory = null)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(associate);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(settings);

        Dashboard = dashboard;
        Calendar = calendar;
        Associate = associate;
        Notes = notes;
        History = history;
        Settings = settings;
        PriceHistory = priceHistory;

        // Phase 20A: Associate → Products asks for the Season Price History screen; it opens as an
        // overlay on top of whatever section is showing, so no Associate state is touched.
        if (PriceHistory is not null)
        {
            Associate.PriceHistoryRequested += (_, productKey) => _ = PriceHistory.OpenAsync(productKey);
            PriceHistory.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PriceHistoryViewModel.IsOpen))
                {
                    OnPropertyChanged(nameof(IsPriceHistoryOpen));
                }
            };
        }

        // "Open Passdown"/"Open Notes" (spec Phase 10C Parts F/G/H): switch the top-level section and
        // set Associate's own internal section explicitly. Phase 21A: Dashboard is today's screen, so
        // "Open Passdown" always opens today's Eastern date (AssociateViewModel.SelectEasternToday,
        // which flushes any unsaved edit on the previous date first). Phase 22E: "Open Notes" sits on the
        // same today's-passdown tile (it reports today's draft note lines), so it opens today's Eastern
        // date too instead of whichever date Associate last showed. Calendar/History routes keep their
        // explicit dates; "View Notes →" (the Operational Notes manager) is not date-scoped and is unchanged.
        Dashboard.OpenAssociateOverviewRequested += (_, _) =>
        {
            Associate.SelectEasternToday();
            Associate.SelectedSection = AssociateSection.Overview;
            ShowAssociateSection();
        };
        Dashboard.OpenAssociateNotesRequested += (_, _) =>
        {
            Associate.SelectEasternToday();
            Associate.SelectedSection = AssociateSection.Notes;
            ShowAssociateSection();
        };
        Dashboard.OpenHistoryRequested += (_, _) => ShowHistorySection();

        // Phase 22C: "View Pricing" on the Dashboard's Daily Admission tile opens the existing detailed
        // pricing reference — Associate → Products — for today's Eastern date (the Dashboard is today's
        // screen), through the same flush-then-load path as Open Passdown.
        Dashboard.OpenPricingRequested += (_, _) =>
        {
            Associate.SelectEasternToday();
            Associate.SelectedSection = AssociateSection.Products;
            ShowAssociateSection();
        };

        // Phase 11C Part O/P: generic navigation to the existing Operational Notes section — never
        // resets Notes.LifecycleFilter/SelectedNote, so a supervisor's existing filter/selection state
        // there is preserved exactly as it was before Dashboard routed here.
        Dashboard.OpenOperationalNotesRequested += (_, _) => ShowOperationalNotesSection();

        // Phase 12A Part F: "Open Passdown" from Calendar explicitly sets Associate's selected date
        // (unlike Dashboard's own deep links, which deliberately never touch it) — the Calendar's
        // whole purpose is routing to a specific date. Setting AssociateViewModel.SelectedDate runs
        // through its own existing SwitchDateAsync path (flush-pending-autosave-then-load), so a
        // supervisor mid-edit on another date never loses unsaved work just because Calendar routed
        // here (Phase 12D Part G/H) — no second dirty-state framework is introduced.
        Calendar.OpenAssociateRequested += (_, date) =>
        {
            Associate.SelectedDate = date;
            Associate.SelectedSection = AssociateSection.Overview;
            ShowAssociateSection();
        };

        // Phase 12D Part I/J/K: Calendar's explicit "Open History" now asks PassdownHistoryViewModel to
        // preselect the matching finalized record via its own date-based seam — never by constructing a
        // PassdownHistorySummaryPresentation here or reaching into History's internals. This is
        // deliberately different from Dashboard's own generic "Open History" (unchanged above), which
        // must keep preserving whatever History already had selected; Calendar's action is
        // date-specific, so intentionally replacing the selection here is correct. If no finalized row
        // matches, SelectByDateAsync leaves History's current selection alone — no crash, no fake
        // selection. Fire-and-forget is safe: PassdownHistoryViewModel.PendingSelectByDate lets a test
        // await it deterministically.
        Calendar.OpenHistoryRequested += (_, date) =>
        {
            _ = History.SelectByDateAsync(date);
            ShowHistorySection();
        };
        Calendar.OpenOperationalNotesRequested += (_, _) => ShowOperationalNotesSection();

        // Phase 13D Part AI/AJ: "View Date Pricing" routes to Associate's Products section for the
        // selected date — same date-preservation/flush-then-load path as "Open Passdown" above, just
        // targeting a different internal section, so an in-progress edit elsewhere is never lost and
        // Calendar's own SelectedDate is left untouched by the navigation itself.
        Calendar.ViewDatePricingRequested += (_, date) =>
        {
            Associate.SelectedDate = date;
            Associate.SelectedSection = AssociateSection.Products;
            ShowAssociateSection();
        };

        // Phase 11B Part X: the smallest Notes-specific unsaved-changes guard, not a generic
        // navigation-guard framework. OperationalNotesViewModel.IsEditing is the one signal this type
        // observes; whenever it changes, the other section buttons' enabled state is re-evaluated so a
        // supervisor mid-create/edit cannot silently navigate away and lose the in-progress note (spec
        // Part W's "disable changing selection while dirty" applied one level up, to top-level
        // navigation).
        Notes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperationalNotesViewModel.IsEditing))
            {
                ShowDashboardSectionCommand.NotifyCanExecuteChanged();
                ShowOperationsCalendarSectionCommand.NotifyCanExecuteChanged();
                ShowAssociateSectionCommand.NotifyCanExecuteChanged();
                ShowHistorySectionCommand.NotifyCanExecuteChanged();
                ShowSettingsSectionCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public OperationsDashboardViewModel Dashboard { get; }

    public OperationsCalendarViewModel Calendar { get; }

    public AssociateViewModel Associate { get; }

    public OperationalNotesViewModel Notes { get; }

    public PassdownHistoryViewModel History { get; }

    /// <summary>Phase 16B: the local Personal Sync configuration/status screen — never touches any Associate draft, note, or finalized passdown data itself.</summary>
    public PersonalSyncSettingsViewModel Settings { get; }

    /// <summary>Phase 20A: the read-only Season Price History overlay; null only in composition-free tests.</summary>
    public PriceHistoryViewModel? PriceHistory { get; }

    public bool IsPriceHistoryOpen => PriceHistory?.IsOpen == true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsOperationsCalendarSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsAssociateSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsOperationalNotesSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsHistorySectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSectionSelected))]
    [NotifyCanExecuteChangedFor(nameof(ShowDashboardSectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowOperationsCalendarSectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowAssociateSectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowHistorySectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowSettingsSectionCommand))]
    public partial AppSection SelectedSection { get; set; } = AppSection.Dashboard;

    public bool IsDashboardSectionSelected => SelectedSection == AppSection.Dashboard;

    public bool IsOperationsCalendarSectionSelected => SelectedSection == AppSection.OperationsCalendar;

    public bool IsAssociateSectionSelected => SelectedSection == AppSection.AssociatePassdown;

    public bool IsOperationalNotesSectionSelected => SelectedSection == AppSection.OperationalNotes;

    public bool IsHistorySectionSelected => SelectedSection == AppSection.History;

    public bool IsSettingsSectionSelected => SelectedSection == AppSection.Settings;

    [RelayCommand(CanExecute = nameof(CanLeaveOperationalNotesSection))]
    private void ShowDashboardSection() => SelectedSection = AppSection.Dashboard;

    [RelayCommand(CanExecute = nameof(CanLeaveOperationalNotesSection))]
    private void ShowOperationsCalendarSection() => SelectedSection = AppSection.OperationsCalendar;

    [RelayCommand(CanExecute = nameof(CanLeaveOperationalNotesSection))]
    private void ShowAssociateSection() => SelectedSection = AppSection.AssociatePassdown;

    [RelayCommand]
    private void ShowOperationalNotesSection() => SelectedSection = AppSection.OperationalNotes;

    [RelayCommand(CanExecute = nameof(CanLeaveOperationalNotesSection))]
    private void ShowHistorySection() => SelectedSection = AppSection.History;

    [RelayCommand(CanExecute = nameof(CanLeaveOperationalNotesSection))]
    private void ShowSettingsSection() => SelectedSection = AppSection.Settings;

    /// <summary>Spec Part X: true unless the Operational Notes section is both currently shown and mid-create/edit — in which case a supervisor must Save or Cancel before any other top-level section becomes reachable.</summary>
    private bool CanLeaveOperationalNotesSection() => !(SelectedSection == AppSection.OperationalNotes && Notes.IsEditing);
}
