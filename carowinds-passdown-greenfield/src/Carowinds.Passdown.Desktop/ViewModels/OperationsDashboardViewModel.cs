using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 10A's Operations Dashboard foundation. Loads today's <see cref="OperationsDashboardSnapshot"/>
/// from <see cref="IOperationsDashboardService"/> only — no repository, HTTP, or PDF-generation
/// dependency exists on this type, so "the dashboard never triggers a network call" is true by
/// constructor signature, not just by test coverage (mirrors <see cref="PassdownHistoryViewModel"/>'s
/// own read-only-dependency shape). Owns no editable state and no <c>SelectedDate</c> of its own —
/// it always represents "today" (Part D) and never reads/writes <see cref="AssociateViewModel.SelectedDate"/>.
/// Navigation to another top-level section is exposed as plain events rather than a direct reference
/// to <see cref="MainViewModel"/>, so this view model stays independent of app-shell navigation
/// concerns (spec Part P) — <see cref="MainViewModel"/> is the one place that wires these events to
/// an actual section switch.
///
/// Phase 19A: stays current on its own. One long-lived instance listens to the app's local
/// <see cref="IDataChangeNotifier"/> — an Associate autosave, an ERT or Operational Notes save, a
/// Personal Sync import, a finished online refresh, the operating day rolling over — and re-reads
/// today's local projection (coalesced, never polled, never a network call). This replaced the header
/// "Refresh" button, which only ever did this same local reload.
/// </summary>
public partial class OperationsDashboardViewModel : ViewModelBase
{
    private readonly IOperationsDashboardService _service;
    private readonly ILogger<OperationsDashboardViewModel>? _logger;

    private Task _pendingLoad = Task.CompletedTask;
    private bool _quietReloadInFlight;
    private bool _quietReloadRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnapshot))]
    [NotifyCanExecuteChangedFor(nameof(OpenPassdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenNotesCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenOperationalNotesCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenPricingCommand))]
    public partial OperationsDashboardSnapshot? Snapshot { get; set; }

    [ObservableProperty]
    public partial OperationsDashboardPresentation? Presentation { get; set; }

    /// <summary>Phase 21C: "Today's Shows" — rebuilt with every snapshot and, for the upcoming/started split, on the view's minute tick (<see cref="RefreshShowsForTime"/>).</summary>
    [ObservableProperty]
    public partial ShowSchedulePresentation? ShowsPresentation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyOrErrorState))]
    [NotifyCanExecuteChangedFor(nameof(CheckOnlineCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunDeepRefreshCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyOrErrorState))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Busy state for the explicit Check Online action (Phase 10D Part N) — distinct from <see cref="IsLoading"/> (the local-only load/reload), so the two never appear as the same "loading" state to a screen reader.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckOnlineCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunDeepRefreshCommand))]
    public partial bool IsCheckingOnline { get; set; }

    /// <summary>"Checking official sources…" / per-source progress text, shown only while <see cref="IsCheckingOnline"/> is true (Part N).</summary>
    [ObservableProperty]
    public partial string? CheckOnlineStatusMessage { get; set; }

    /// <summary>The plain-language result of the most recent Check Online action (Part N/T) — reuses <see cref="RefreshMessages"/>, never a raw exception message.</summary>
    [ObservableProperty]
    public partial string? CheckOnlineResultMessage { get; set; }

    [ObservableProperty]
    public partial bool CheckOnlineHadIssues { get; set; }

    /// <summary>Current Deep Refresh status (Phase 13D Part U/V) — loaded alongside the snapshot; refreshed again after a manual run.</summary>
    [ObservableProperty]
    public partial DeepRefreshStatusView? DeepRefreshStatus { get; set; }

    [ObservableProperty]
    public partial DeepRefreshStatusPresentation? DeepRefreshPresentation { get; set; }

    /// <summary>Busy state for the explicit "Run Deep Refresh" action (Phase 13D Part O) — distinct from <see cref="IsCheckingOnline"/>/<see cref="IsLoading"/>, mirroring the same pattern.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunDeepRefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckOnlineCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    public partial bool IsRunningDeepRefresh { get; set; }

    [ObservableProperty]
    public partial string? DeepRefreshStatusMessage { get; set; }

    [ObservableProperty]
    public partial string? DeepRefreshResultMessage { get; set; }

    [ObservableProperty]
    public partial bool DeepRefreshHadIssues { get; set; }

    /// <summary>
    /// Phase 22C: whether the Official Data source breakdown (per-source rows, Deep Refresh) is
    /// expanded. Collapsed by default; purely presentational — toggling it never reads data, calls the
    /// network or touches refresh scheduling. Genuine problems stay visible while collapsed through
    /// <see cref="DashboardDataHealthPresentation.AttentionText"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceDetailsToggleText))]
    public partial bool IsSourceDetailsExpanded { get; set; }

    /// <summary>Phase 22C: the disclosure's label states what activating it will do.</summary>
    public string SourceDetailsToggleText => IsSourceDetailsExpanded ? "Hide Source Details" : "View Source Details";

    /// <summary>
    /// Raised when the "Open Passdown" action is used (Phase 10A Part O; Phase 10C Part G) — routes to
    /// the existing Associate Passdown screen's Overview section specifically, rather than duplicating
    /// any of its editing UI here or leaving whatever internal section Associate was last on.
    /// </summary>
    public event EventHandler? OpenAssociateOverviewRequested;

    /// <summary>
    /// Raised when the "Open Notes" action is used (Phase 10B Part J/T; Phase 10C Part F) — a true
    /// deep link to Associate Passdown's Notes section, replacing Phase 10B's routing to Associate as a
    /// whole (there was no Notes-specific section to target yet).
    /// </summary>
    public event EventHandler? OpenAssociateNotesRequested;

    /// <summary>Raised when the "Open History" action is used (Phase 10A Part O) — routes to the existing read-only History screen.</summary>
    public event EventHandler? OpenHistoryRequested;

    /// <summary>
    /// Raised when "Open Operational Notes" is used (Phase 11C Part O/P) — routes to the existing
    /// top-level Operational Notes section as a whole. Deliberately generic navigation, not a
    /// "View Today's Active Notes" deep link that would force <see cref="OperationalNotesViewModel.LifecycleFilter"/>
    /// to a specific value (Part P/Q evaluated and deferred that variant) — a supervisor's existing
    /// filter/selection state in that screen is preserved exactly as it was.
    /// </summary>
    public event EventHandler? OpenOperationalNotesRequested;

    /// <summary>
    /// Phase 22C: raised by "View Pricing" on the Daily Admission tile — routes to the existing detailed
    /// pricing reference (Associate Passdown → Products) for today's Eastern date, like Open Passdown.
    /// </summary>
    public event EventHandler? OpenPricingRequested;

    public OperationsDashboardViewModel(
        IOperationsDashboardService service,
        ILogger<OperationsDashboardViewModel>? logger = null,
        IDataChangeNotifier? dataChangeNotifier = null,
        OfficialDataStatusViewModel? officialDataStatus = null,
        IUiDispatcher? uiDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
        _logger = logger;
        OfficialDataStatus = officialDataStatus;

        _pendingLoad = LoadAsync();

        if (dataChangeNotifier is not null)
        {
            var dispatcher = uiDispatcher ?? new AvaloniaUiDispatcher();
            dataChangeNotifier.Changed += (_, change) => dispatcher.Post(() => HandleDataChange(change));
        }
    }

    /// <summary>Phase 19A: the shared official-data freshness line shown in the Dashboard header (null only in tests that do not supply one).</summary>
    public OfficialDataStatusViewModel? OfficialDataStatus { get; }

    /// <summary>
    /// Phase 19A: reacts to one committed local change (on the UI thread). A change that names a
    /// different operating date than the one shown is ignored; anything else re-reads today's local
    /// projection. Internal so tests can drive it without a dispatcher.
    /// </summary>
    internal void HandleDataChange(DataChange change)
    {
        if (change.Kind != DataChangeKind.OperatingDay && Snapshot is { } shown && !change.MayAffect(shown.OperatingDate))
        {
            return;
        }

        _logger?.LogInformation("Dashboard local data invalidated ({ChangeKind}); reloading today's summary from local data.", change.Kind);
        RequestLocalReload();
    }

    /// <summary>
    /// Re-reads today's local projection without the visible loading state. Coalesced: requests that
    /// arrive while a load is running collapse into one follow-up reload, so a burst of saves never
    /// queues a burst of reads.
    /// </summary>
    internal void RequestLocalReload()
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
        try
        {
            var snapshot = await _service.GetTodayAsync().ConfigureAwait(true);
            Snapshot = snapshot;
            Presentation = OperationsDashboardPresentation.Build(snapshot);
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep showing the last good summary rather than blanking the screen over a transient
            // read failure (e.g. SQLite busy); the next change, or "Try again", reloads it.
            _logger?.LogWarning(ex, "Failed to reload today's operations summary after a local change.");
            if (Snapshot is null)
            {
                ErrorMessage = "Unable to load today's operations summary.";
            }
        }

        await LoadDeepRefreshStatusAsync().ConfigureAwait(true);
    }

    public bool HasSnapshot => Snapshot is not null;

    partial void OnSnapshotChanged(OperationsDashboardSnapshot? value) =>
        ShowsPresentation = value is null ? null : ShowSchedulePresentation.Build(value.TodayShows, value.GeneratedAt, isDashboard: true);

    /// <summary>
    /// Phase 21C: re-evaluates which of today's performances are still upcoming at <paramref name="nowUtc"/>
    /// from the snapshot already shown — local text only, never a data read or network call. Called by
    /// the view once a minute while it is displayed.
    /// </summary>
    public void RefreshShowsForTime(DateTimeOffset nowUtc)
    {
        if (Snapshot is { } snapshot)
        {
            ShowsPresentation = ShowSchedulePresentation.Build(snapshot.TodayShows, nowUtc, isDashboard: true);
        }
    }

    /// <summary>Shown only once loading has finished and produced neither a snapshot nor an error is impossible in practice (every load path sets one or the other) — kept for a graceful first-frame render before the initial load completes.</summary>
    public bool ShowEmptyOrErrorState => !IsLoading && Snapshot is null;

    /// <summary>The in-flight (or most recently finished) load, so a test can await it deterministically instead of racing the constructor's fire-and-forget load.</summary>
    public Task PendingLoad => _pendingLoad;

    /// <summary>
    /// Local reload (Phase 10A Part R). Phase 19A: no longer a header "Refresh" button — the Dashboard
    /// updates itself from local change notifications — but kept as the "Try again" action shown only
    /// when today's summary could not be loaded at all, the one case with no snapshot to keep showing.
    /// Never checks the website.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync() => await LoadAsync().ConfigureAwait(true);

    private bool CanReload() => !IsCheckingOnline && !IsRunningDeepRefresh;

    /// <summary>
    /// Explicit "Check Online" (Phase 10D Part L/M/N): reuses the exact same manual-date refresh
    /// workflow the Associate screen's own Check Online action calls (via
    /// <see cref="IOperationsDashboardService.CheckOnlineAsync"/>, which itself reuses
    /// <c>IAssociatePassdownService.CheckAssociateDateOnlineAsync</c> — no second refresh
    /// implementation exists here). <see cref="CanCheckOnline"/> disables re-entry while either this
    /// or a local reload is already running, so repeated clicks can never start a concurrent refresh
    /// (Part N).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckOnline))]
    private async Task CheckOnlineAsync()
    {
        IsCheckingOnline = true;
        CheckOnlineResultMessage = null;
        CheckOnlineHadIssues = false;
        CheckOnlineStatusMessage = RefreshMessages.InProgressDefault;

        var progress = new Progress<RefreshProgress>(p => CheckOnlineStatusMessage = RefreshMessages.Progress(p));

        try
        {
            var result = await _service.CheckOnlineAsync(progress).ConfigureAwait(true);
            Snapshot = result.Snapshot;
            Presentation = OperationsDashboardPresentation.Build(result.Snapshot);
            CheckOnlineResultMessage = RefreshMessages.Summary(result.Summary);
            CheckOnlineHadIssues = result.Summary.Failed > 0 || result.Summary.Cancelled > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mirrors AssociateViewModel.CheckOnlineAsync's own handling: each source already fails
            // independently inside the refresh orchestrator, so reaching here means the check itself
            // could not run at all — the Dashboard's existing local values are left untouched.
            _logger?.LogError(ex, "Check Online failed unexpectedly from the dashboard.");
            CheckOnlineResultMessage = "Could not check online right now. Locally saved information is unchanged.";
            CheckOnlineHadIssues = true;
        }
        finally
        {
            IsCheckingOnline = false;
            CheckOnlineStatusMessage = null;
        }
    }

    private bool CanCheckOnline() => !IsLoading && !IsCheckingOnline && !IsRunningDeepRefresh;

    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var snapshot = await _service.GetTodayAsync().ConfigureAwait(true);
            Snapshot = snapshot;
            Presentation = OperationsDashboardPresentation.Build(snapshot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load today's operations dashboard summary.");
            Snapshot = null;
            Presentation = null;
            ErrorMessage = "Unable to load today's operations summary.";
        }
        finally
        {
            IsLoading = false;
        }

        await LoadDeepRefreshStatusAsync().ConfigureAwait(true);

        // A local change arrived while this load was running — read again so it is not missed.
        if (_quietReloadRequested)
        {
            RequestLocalReload();
        }
    }

    private async Task LoadDeepRefreshStatusAsync()
    {
        try
        {
            var status = await _service.GetDeepRefreshStatusAsync().ConfigureAwait(true);
            DeepRefreshStatus = status;
            DeepRefreshPresentation = DeepRefreshStatusPresentation.Build(status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load Deep Refresh status.");
        }
    }

    /// <summary>
    /// Explicit manual "Run Deep Refresh" (Phase 13D Part O/P) — always available (never hidden based
    /// on scheduler status), runs regardless of coverage health, and reuses the exact same
    /// <see cref="IOperationsDashboardService.RunDeepRefreshAsync"/> operation scheduled/startup-
    /// catch-up maintenance uses. Mutually exclusive with Check Online/Reload at the UI level (their
    /// own <c>Can...</c> gates check <see cref="IsRunningDeepRefresh"/> too) — the shared overlap lock
    /// is still the authoritative guard, this is just responsive UX on top of it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunDeepRefresh))]
    private async Task RunDeepRefreshAsync()
    {
        IsRunningDeepRefresh = true;
        DeepRefreshResultMessage = null;
        DeepRefreshHadIssues = false;
        DeepRefreshStatusMessage = "Checking online and preparing upcoming dates…";

        var progress = new Progress<RefreshProgress>(p => DeepRefreshStatusMessage = RefreshMessages.Progress(p));

        try
        {
            var result = await _service.RunDeepRefreshAsync(progress).ConfigureAwait(true);
            Snapshot = result.Snapshot;
            Presentation = OperationsDashboardPresentation.Build(result.Snapshot);
            DeepRefreshStatus = result.Status;
            DeepRefreshPresentation = DeepRefreshStatusPresentation.Build(result.Status);
            DeepRefreshResultMessage = RefreshMessages.Summary(result.Summary);
            DeepRefreshHadIssues = result.Summary.Failed > 0 || result.Summary.Cancelled > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Run Deep Refresh failed unexpectedly.");
            DeepRefreshResultMessage = "Could not run Deep Refresh right now. Locally saved information is unchanged.";
            DeepRefreshHadIssues = true;
        }
        finally
        {
            IsRunningDeepRefresh = false;
            DeepRefreshStatusMessage = null;
        }
    }

    private bool CanRunDeepRefresh() => !IsLoading && !IsCheckingOnline && !IsRunningDeepRefresh;

    [RelayCommand(CanExecute = nameof(CanOpenPassdown))]
    private void OpenPassdown() => OpenAssociateOverviewRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// "Open Notes" (Phase 10B Part J/T; Phase 10C Part F) — a true deep link, now that Associate
    /// Passdown's internal section is view-model-owned (<see cref="AssociateViewModel.SelectedSection"/>)
    /// rather than only view-layer <c>ToggleButton</c> state. Raising a distinct event from
    /// <see cref="OpenPassdownCommand"/> lets <see cref="MainViewModel"/> — the one place that
    /// coordinates both the top-level section switch and Associate's internal section — route this
    /// specifically to Notes without this view model needing to know <see cref="AssociateViewModel"/>
    /// or <c>AppSection</c> exist (spec Part P).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenPassdown))]
    private void OpenNotes() => OpenAssociateNotesRequested?.Invoke(this, EventArgs.Empty);

    private bool CanOpenPassdown() => HasSnapshot;

    [RelayCommand(CanExecute = nameof(CanOpenPassdown))]
    private void OpenPricing() => OpenPricingRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenHistory() => OpenHistoryRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>"Open Operational Notes" (Phase 11C Part O) — gated the same way as Open Passdown/Open Notes (a snapshot must exist), even though the target screen itself has no dependency on the Dashboard snapshot.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenPassdown))]
    private void OpenOperationalNotes() => OpenOperationalNotesRequested?.Invoke(this, EventArgs.Empty);
}
