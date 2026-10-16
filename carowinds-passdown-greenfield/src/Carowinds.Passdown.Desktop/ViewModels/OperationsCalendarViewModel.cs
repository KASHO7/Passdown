using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.HistoricalBackfill;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 12A's Operations Calendar foundation — a planning/navigation surface, not a second Associate
/// editor (spec Part A/H): it summarizes a month and routes into the existing Associate Passdown/
/// History/Operational Notes screens. Depends only on <see cref="IOperationsCalendarService"/> and
/// <see cref="IClock"/> — no repository, HTTP, or PDF dependency exists on this type, mirroring
/// <see cref="OperationsDashboardViewModel"/>'s own no-network-dependency shape. Navigation to another
/// top-level section is exposed as plain events (never a direct <see cref="MainViewModel"/> reference),
/// so this view model stays independent of app-shell navigation concerns — <see cref="MainViewModel"/>
/// is the one place that wires these events to an actual section switch and to
/// <see cref="AssociateViewModel.SelectedDate"/>.
/// </summary>
public partial class OperationsCalendarViewModel : ViewModelBase
{
    private readonly IOperationsCalendarService _service;
    private readonly IClock _clock;
    private readonly ILogger<OperationsCalendarViewModel>? _logger;
    private readonly IHistoricalBackfillService? _historicalBackfill;

    private int _loadVersion;
    private bool _quietReloadInFlight;
    private bool _quietReloadRequested;
    private Task _pendingLoad = Task.CompletedTask;

    /// <summary>
    /// True only while the constructor is setting <see cref="DisplayedMonth"/>/<see cref="SelectedDate"/>
    /// for the first time. Without this, each property's own <c>OnXChanged</c> partial method would
    /// start its own load the instant it's set — the first one running against whichever of the two
    /// properties hasn't been assigned yet (still <c>default(DateOnly)</c>) — instead of one load
    /// against both fully-initialized values.
    /// </summary>
    private bool _suppressAutoLoad = true;

    [ObservableProperty]
    public partial DateOnly DisplayedMonth { get; set; }

    /// <summary>
    /// Independent of <see cref="AssociateViewModel.SelectedDate"/> unless the user explicitly opens
    /// Associate from here (spec Part C) — nothing in this type ever reads or writes that property.
    /// </summary>
    [ObservableProperty]
    public partial DateOnly SelectedDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnapshot))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyOrErrorState))]
    [NotifyPropertyChangedFor(nameof(OpenHistoryHelpText))]
    [NotifyCanExecuteChangedFor(nameof(OpenHistoryCommand))]
    public partial OperationsCalendarSnapshot? Snapshot { get; set; }

    [ObservableProperty]
    public partial OperationsCalendarPresentation? Presentation { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<OperationsCalendarDayViewModel> Days { get; set; } = [];

    /// <summary>
    /// Phase 15B: <see cref="Days"/> chunked into Sunday-through-Saturday rows of 7 for the view's
    /// per-week <c>Grid</c> layout (spec Part 1) — a pure presentation grouping over the same list,
    /// never a second load or reordering.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<OperationsCalendarWeekViewModel> Weeks { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyOrErrorState))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Phase 20C: the subtle historical-data line under the month title (hidden when <see cref="HistoricalBackfillStatusPresentation.Text"/> is null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoryStatus))]
    public partial HistoricalBackfillStatusPresentation HistoryStatus { get; set; } = HistoricalBackfillStatusPresentation.Hidden;

    public bool HasHistoryStatus => HistoryStatus.Text is not null;

    /// <summary>Raised by "Open Passdown" (spec Part F/G) with the Calendar's currently selected date — <see cref="MainViewModel"/> is the one place that sets <see cref="AssociateViewModel.SelectedDate"/> from it.</summary>
    public event EventHandler<DateOnly>? OpenAssociateRequested;

    /// <summary>
    /// Raised by "View Date Pricing" (Phase 13D Part AI/AJ) — routes to the existing Associate
    /// Passdown Products section for the selected date, purely local-only navigation (never triggers
    /// Check Online just because the associate navigated). Deliberately a distinct event from
    /// <see cref="OpenAssociateRequested"/> (which targets Overview) so <see cref="MainViewModel"/>
    /// can route each to a different internal Associate section without this view model knowing
    /// <c>AssociateSection</c> exists.
    /// </summary>
    public event EventHandler<DateOnly>? ViewDatePricingRequested;

    /// <summary>
    /// Raised by "Open History" (Phase 12A Part F). Carries the Calendar's currently selected date
    /// (Phase 12D Part I/J) so <see cref="MainViewModel"/> can ask <see cref="PassdownHistoryViewModel"/>
    /// to preselect the matching finalized record via its own <c>SelectByDateAsync</c> seam — this
    /// command is only enabled once that date is Finalized (<see cref="CanOpenHistory"/>), so the date
    /// carried here always corresponds to a row History is expected to have. This view model still
    /// never reaches into <see cref="PassdownHistoryViewModel"/> itself.
    /// </summary>
    public event EventHandler<DateOnly>? OpenHistoryRequested;

    /// <summary>Raised by "Open Operational Notes" (spec Part F) — generic navigation only, mirroring Dashboard's own "Open Operational Notes" (Phase 11C).</summary>
    public event EventHandler? OpenOperationalNotesRequested;

    public OperationsCalendarViewModel(
        IOperationsCalendarService service,
        IClock clock,
        ILogger<OperationsCalendarViewModel>? logger = null,
        IDataChangeNotifier? dataChangeNotifier = null,
        IHistoricalBackfillService? historicalBackfill = null,
        IUiDispatcher? uiDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;
        _logger = logger;
        _historicalBackfill = historicalBackfill;

        var today = TodayEastern();
        DisplayedMonth = new DateOnly(today.Year, today.Month, 1);
        SelectedDate = today;
        _suppressAutoLoad = false;

        _pendingLoad = LoadAsync();

        var dispatcher = uiDispatcher ?? new AvaloniaUiDispatcher();

        // Phase 20C: the Calendar now follows committed local changes (official data from any refresh,
        // historical backfill, drafts, notes, sync) the same way the Dashboard does — a local re-read of
        // the displayed month, never a poll and never a network request.
        if (dataChangeNotifier is not null)
        {
            dataChangeNotifier.Changed += (_, change) => dispatcher.Post(() => HandleDataChange(change));
        }

        if (historicalBackfill is not null)
        {
            HistoryStatus = HistoricalBackfillStatusPresentation.Build(historicalBackfill.CurrentStatus);
            historicalBackfill.StatusChanged += (_, status) => dispatcher.Post(() => HistoryStatus = HistoricalBackfillStatusPresentation.Build(status));
        }
    }

    /// <summary>
    /// Phase 20C: reacts to one committed local change (on the UI thread). A change naming a date outside
    /// the displayed grid is ignored; anything else quietly re-reads the displayed month (no loading bar,
    /// the selection and month stay put). Internal so tests can drive it without a dispatcher.
    /// </summary>
    internal void HandleDataChange(DataChange change)
    {
        if (change.Kind != DataChangeKind.OperatingDay
            && change.OperatingDate is { } date
            && Snapshot is { Days.Count: > 0 } shown
            && (date < shown.Days[0].Date || date > shown.Days[^1].Date))
        {
            return;
        }

        _logger?.LogDebug("Operations Calendar local data invalidated ({ChangeKind}); reloading the displayed month from local data.", change.Kind);
        RequestQuietReload();
    }

    /// <summary>Coalesced quiet reload: changes arriving while one runs collapse into one follow-up.</summary>
    internal void RequestQuietReload()
    {
        if (_quietReloadInFlight || IsLoading)
        {
            _quietReloadRequested = true;
            return;
        }

        _pendingLoad = RunQuietReloadsAsync();
    }

    private async Task RunQuietReloadsAsync()
    {
        _quietReloadInFlight = true;
        try
        {
            do
            {
                _quietReloadRequested = false;
                await ReloadQuietlyAsync().ConfigureAwait(true);
            }
            while (_quietReloadRequested);
        }
        finally
        {
            _quietReloadInFlight = false;
        }
    }

    private async Task ReloadQuietlyAsync()
    {
        var version = ++_loadVersion;
        try
        {
            var snapshot = await _service.LoadAsync(DisplayedMonth, SelectedDate).ConfigureAwait(true);
            if (version == _loadVersion)
            {
                Snapshot = snapshot;
                ErrorMessage = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep showing the last good month; the next change or navigation reloads it.
            _logger?.LogWarning(ex, "Failed to reload the Operations Calendar after a local change.");
        }
    }

    public bool HasSnapshot => Snapshot is not null;

    public bool ShowEmptyOrErrorState => !IsLoading && Snapshot is null;

    /// <summary>The in-flight (or most recently finished) load, so a test can await it deterministically instead of racing the constructor's fire-and-forget load.</summary>
    public Task PendingLoad => _pendingLoad;

    partial void OnDisplayedMonthChanged(DateOnly value)
    {
        // Phase 20C: a viewed current-year month with missing history is checked next (still behind
        // every operational refresh). Local only — this never starts a request by itself.
        _historicalBackfill?.PrioritizeMonth(value);

        if (!_suppressAutoLoad)
        {
            _pendingLoad = LoadAsync();
        }
    }

    partial void OnSelectedDateChanged(DateOnly value)
    {
        if (!_suppressAutoLoad)
        {
            _pendingLoad = LoadAsync();
        }
    }

    partial void OnSnapshotChanged(OperationsCalendarSnapshot? value)
    {
        Presentation = value is null ? null : OperationsCalendarPresentation.Build(value);
        Days = value?.Days.Select(day => new OperationsCalendarDayViewModel(day, day.Date == value.SelectedDate, SelectDate)).ToList() ?? [];
        Weeks = Days.Chunk(7).Select(week => new OperationsCalendarWeekViewModel(week)).ToList();
    }

    /// <summary>
    /// Phase 12D Part D: preserves the selection's day-of-month across a month change, clamping to the
    /// target month's own last day when it's shorter (Jan 31 → Feb 28/29) — the deliberately chosen,
    /// documented alternative to always jumping the selection to the 1st.
    /// </summary>
    [RelayCommand]
    private void PreviousMonth() => NavigateMonth(-1);

    [RelayCommand]
    private void NextMonth() => NavigateMonth(1);

    private void NavigateMonth(int monthDelta)
    {
        var newDisplayedMonth = DisplayedMonth.AddMonths(monthDelta);
        var clampedDay = Math.Min(SelectedDate.Day, DateTime.DaysInMonth(newDisplayedMonth.Year, newDisplayedMonth.Month));
        var newSelectedDate = new DateOnly(newDisplayedMonth.Year, newDisplayedMonth.Month, clampedDay);
        SetDisplayedMonthAndSelectedDate(newDisplayedMonth, newSelectedDate);
    }

    /// <summary>Resets both the displayed month and the selection to the current Eastern date.</summary>
    [RelayCommand]
    private void GoToToday()
    {
        var today = TodayEastern();
        SetDisplayedMonthAndSelectedDate(new DateOnly(today.Year, today.Month, 1), today);
    }

    /// <summary>
    /// Phase 12D Part B: the chosen, documented behavior for an off-month cell is that selecting it
    /// moves the Calendar to that date's own month (never a silent no-op, and never leaving
    /// <see cref="DisplayedMonth"/> pointed somewhere that no longer contains the selection). A
    /// same-month selection is unaffected — setting <see cref="DisplayedMonth"/> to its own current
    /// value is a no-op on an <c>[ObservableProperty]</c>, so only <see cref="SelectedDate"/> actually
    /// changes and exactly one reload fires.
    /// </summary>
    [RelayCommand]
    private void SelectDate(DateOnly date) => SetDisplayedMonthAndSelectedDate(new DateOnly(date.Year, date.Month, 1), date);

    /// <summary>Phase 12D Part C: explicit day-stepping — supervisors scanning adjacent days without reaching for month navigation or a day cell.</summary>
    [RelayCommand]
    private void PreviousDay() => StepSelectedDate(-1);

    [RelayCommand]
    private void NextDay() => StepSelectedDate(1);

    private void StepSelectedDate(int dayDelta)
    {
        var newDate = SelectedDate.AddDays(dayDelta);
        SetDisplayedMonthAndSelectedDate(new DateOnly(newDate.Year, newDate.Month, 1), newDate);
    }

    /// <summary>
    /// Phase 12D Part AI: the single place both properties are ever changed together, so a caller that
    /// wants "move to this date's month and select it" (day stepping across a boundary, off-month
    /// selection, month navigation with day-of-month clamping, Go to Today) always resolves to exactly
    /// one <see cref="LoadAsync"/> call — never one load per property, matching 12A's own
    /// <c>_suppressAutoLoad</c> pattern already used for construction.
    /// </summary>
    private void SetDisplayedMonthAndSelectedDate(DateOnly displayedMonth, DateOnly selectedDate)
    {
        _suppressAutoLoad = true;
        DisplayedMonth = displayedMonth;
        SelectedDate = selectedDate;
        _suppressAutoLoad = false;
        _pendingLoad = LoadAsync();
    }

    [RelayCommand]
    private void OpenAssociate() => OpenAssociateRequested?.Invoke(this, SelectedDate);

    [RelayCommand]
    private void ViewDatePricing() => ViewDatePricingRequested?.Invoke(this, SelectedDate);

    [RelayCommand(CanExecute = nameof(CanOpenHistory))]
    private void OpenHistory() => OpenHistoryRequested?.Invoke(this, SelectedDate);

    private bool CanOpenHistory() => Snapshot?.Selected.PassdownStatus == OperationsCalendarPassdownStatus.Finalized;

    /// <summary>
    /// Phase 12E Part S: "Open History" is disabled (not hidden) until the selected date is
    /// Finalized (spec Phase 12A/12D "disable, don't hide"). Visual dimming alone is never the sole
    /// signal — this composed help text explains *why* the control is disabled, mirroring the plain-
    /// prose "Opens X. Enabled once &lt;condition&gt;." convention every other conditionally-enabled
    /// action in the app already uses (e.g. Dashboard's own action help text).
    /// </summary>
    public string OpenHistoryHelpText => CanOpenHistory()
        ? "Opens Passdown History for the selected date's finalized record."
        : "Opens Passdown History. No finalized passdown for this date yet.";

    [RelayCommand]
    private void OpenOperationalNotes() => OpenOperationalNotesRequested?.Invoke(this, EventArgs.Empty);

    private DateOnly TodayEastern() => DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(_clock.UtcNow).DateTime);

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var snapshot = await _service.LoadAsync(DisplayedMonth, SelectedDate).ConfigureAwait(true);
            if (version != _loadVersion)
            {
                return; // a newer month/selection change started before this one finished loading.
            }

            Snapshot = snapshot;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version != _loadVersion)
            {
                return;
            }

            _logger?.LogError(ex, "Failed to load the Operations Calendar for {DisplayedMonth}.", DisplayedMonth);
            Snapshot = null;
            ErrorMessage = "Unable to load the operations calendar right now.";
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;

                // A local change that arrived while this load ran may have committed after it read.
                if (_quietReloadRequested && !_quietReloadInFlight)
                {
                    _pendingLoad = RunQuietReloadsAsync();
                }
            }
        }
    }
}
