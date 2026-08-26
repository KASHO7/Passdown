using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.Presentation;
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
/// </summary>
public partial class OperationsDashboardViewModel : ViewModelBase
{
    private readonly IOperationsDashboardService _service;
    private readonly ILogger<OperationsDashboardViewModel>? _logger;

    private Task _pendingLoad = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnapshot))]
    [NotifyCanExecuteChangedFor(nameof(OpenPassdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenNotesCommand))]
    public partial OperationsDashboardSnapshot? Snapshot { get; set; }

    [ObservableProperty]
    public partial OperationsDashboardPresentation? Presentation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyOrErrorState))]
    [NotifyCanExecuteChangedFor(nameof(CheckOnlineCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyOrErrorState))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Busy state for the explicit Check Online action (Phase 10D Part N) — distinct from <see cref="IsLoading"/> (the local-only load/reload), so the two never appear as the same "loading" state to a screen reader.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckOnlineCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    public partial bool IsCheckingOnline { get; set; }

    /// <summary>"Checking official sources…" / per-source progress text, shown only while <see cref="IsCheckingOnline"/> is true (Part N).</summary>
    [ObservableProperty]
    public partial string? CheckOnlineStatusMessage { get; set; }

    /// <summary>The plain-language result of the most recent Check Online action (Part N/T) — reuses <see cref="RefreshMessages"/>, never a raw exception message.</summary>
    [ObservableProperty]
    public partial string? CheckOnlineResultMessage { get; set; }

    [ObservableProperty]
    public partial bool CheckOnlineHadIssues { get; set; }

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

    public OperationsDashboardViewModel(IOperationsDashboardService service, ILogger<OperationsDashboardViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
        _logger = logger;

        _pendingLoad = LoadAsync();
    }

    public bool HasSnapshot => Snapshot is not null;

    /// <summary>Shown only once loading has finished and produced neither a snapshot nor an error is impossible in practice (every load path sets one or the other) — kept for a graceful first-frame render before the initial load completes.</summary>
    public bool ShowEmptyOrErrorState => !IsLoading && Snapshot is null;

    /// <summary>The in-flight (or most recently finished) load, so a test can await it deterministically instead of racing the constructor's fire-and-forget load.</summary>
    public Task PendingLoad => _pendingLoad;

    /// <summary>
    /// Manual reload (Phase 10A Part R) — the dashboard does not poll or subscribe to an automatic
    /// refresh event; a user revisits/reopens it to see persisted state that changed elsewhere
    /// (e.g. after the background <c>StartupCatchUpRefresh</c> completes).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync() => await LoadAsync().ConfigureAwait(true);

    private bool CanReload() => !IsCheckingOnline;

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

    private bool CanCheckOnline() => !IsLoading && !IsCheckingOnline;

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
    }

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

    [RelayCommand]
    private void OpenHistory() => OpenHistoryRequested?.Invoke(this, EventArgs.Empty);
}
