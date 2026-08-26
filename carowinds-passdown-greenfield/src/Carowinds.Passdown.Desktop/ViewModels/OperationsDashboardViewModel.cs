using Carowinds.Passdown.Application.Dashboard;
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
    public partial OperationsDashboardSnapshot? Snapshot { get; set; }

    [ObservableProperty]
    public partial OperationsDashboardPresentation? Presentation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyOrErrorState))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyOrErrorState))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Raised when the "Open Passdown" action is used (Phase 10A Part O) — routes to the existing Associate Passdown screen rather than duplicating any of its editing UI here.</summary>
    public event EventHandler? OpenAssociateRequested;

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
    [RelayCommand]
    private async Task ReloadAsync() => await LoadAsync().ConfigureAwait(true);

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
    private void OpenPassdown() => OpenAssociateRequested?.Invoke(this, EventArgs.Empty);

    private bool CanOpenPassdown() => HasSnapshot;

    [RelayCommand]
    private void OpenHistory() => OpenHistoryRequested?.Invoke(this, EventArgs.Empty);
}
