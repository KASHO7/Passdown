using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Presentation;
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

    private int _loadVersion;
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
    [NotifyCanExecuteChangedFor(nameof(OpenHistoryCommand))]
    public partial OperationsCalendarSnapshot? Snapshot { get; set; }

    [ObservableProperty]
    public partial OperationsCalendarPresentation? Presentation { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<OperationsCalendarDayViewModel> Days { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyOrErrorState))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Raised by "Open Passdown" (spec Part F/G) with the Calendar's currently selected date — <see cref="MainViewModel"/> is the one place that sets <see cref="AssociateViewModel.SelectedDate"/> from it.</summary>
    public event EventHandler<DateOnly>? OpenAssociateRequested;

    /// <summary>Raised by "Open History" (spec Part F) — deliberately generic navigation, not a date pre-selection, matching the same choice already made for Dashboard's History integration (Phase 10C "Open History routing").</summary>
    public event EventHandler? OpenHistoryRequested;

    /// <summary>Raised by "Open Operational Notes" (spec Part F) — generic navigation only, mirroring Dashboard's own "Open Operational Notes" (Phase 11C).</summary>
    public event EventHandler? OpenOperationalNotesRequested;

    public OperationsCalendarViewModel(IOperationsCalendarService service, IClock clock, ILogger<OperationsCalendarViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;
        _logger = logger;

        var today = TodayEastern();
        DisplayedMonth = new DateOnly(today.Year, today.Month, 1);
        SelectedDate = today;
        _suppressAutoLoad = false;

        _pendingLoad = LoadAsync();
    }

    public bool HasSnapshot => Snapshot is not null;

    public bool ShowEmptyOrErrorState => !IsLoading && Snapshot is null;

    /// <summary>The in-flight (or most recently finished) load, so a test can await it deterministically instead of racing the constructor's fire-and-forget load.</summary>
    public Task PendingLoad => _pendingLoad;

    partial void OnDisplayedMonthChanged(DateOnly value)
    {
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
    }

    [RelayCommand]
    private void PreviousMonth() => DisplayedMonth = DisplayedMonth.AddMonths(-1);

    [RelayCommand]
    private void NextMonth() => DisplayedMonth = DisplayedMonth.AddMonths(1);

    /// <summary>Resets both the displayed month and the selection to the current Eastern date — the two property changes each start their own load, but the version guard in <see cref="LoadAsync"/> ensures only the final one wins.</summary>
    [RelayCommand]
    private void GoToToday()
    {
        var today = TodayEastern();
        DisplayedMonth = new DateOnly(today.Year, today.Month, 1);
        SelectedDate = today;
    }

    [RelayCommand]
    private void SelectDate(DateOnly date) => SelectedDate = date;

    [RelayCommand]
    private void OpenAssociate() => OpenAssociateRequested?.Invoke(this, SelectedDate);

    [RelayCommand(CanExecute = nameof(CanOpenHistory))]
    private void OpenHistory() => OpenHistoryRequested?.Invoke(this, EventArgs.Empty);

    private bool CanOpenHistory() => Snapshot?.Selected.PassdownStatus == OperationsCalendarPassdownStatus.Finalized;

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
            }
        }
    }
}
