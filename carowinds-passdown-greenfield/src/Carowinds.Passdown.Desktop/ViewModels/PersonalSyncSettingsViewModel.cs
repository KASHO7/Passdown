using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// The Settings / Personal Sync screen (Phase 16B §4/§14, Phase 16C §25/§26): On/Off, the configured
/// folder, a Choose Folder action, a small status line, and (16C) a manual "Sync Now" action with a
/// simple result line — deliberately no last-sync-time, no automatic/background cadence, and no
/// conflict-resolution controls; those belong to 16D/16F. Loads eagerly in its constructor, mirroring
/// <see cref="OperationsDashboardViewModel"/>'s own fire-and-forget-with-awaitable-handle pattern so a
/// test can await <see cref="PendingLoad"/> instead of racing it.
/// </summary>
public partial class PersonalSyncSettingsViewModel : ViewModelBase
{
    private readonly IPersonalSyncSettingsService _service;
    private readonly ISyncFolderPicker _folderPicker;
    private readonly IPersonalSyncService _syncService;
    private readonly ILogger<PersonalSyncSettingsViewModel>? _logger;

    private Task _pendingLoad = Task.CompletedTask;
    private bool _isApplyingSnapshot;

    [ObservableProperty]
    public partial bool PersonalSyncEnabled { get; set; }

    [ObservableProperty]
    public partial string? SyncRootPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusPresentation))]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    public partial PersonalSyncStatus Status { get; set; } = PersonalSyncStatus.Off;

    [ObservableProperty]
    public partial string? StatusDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestedRoots))]
    [NotifyPropertyChangedFor(nameof(FirstSuggestedRoot))]
    public partial IReadOnlyList<string> SuggestedRoots { get; set; } = [];

    [ObservableProperty]
    public partial Guid DeviceId { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? SyncResultSummary { get; set; }

    public PersonalSyncSettingsViewModel(
        IPersonalSyncSettingsService service,
        ISyncFolderPicker folderPicker,
        IPersonalSyncService syncService,
        ILogger<PersonalSyncSettingsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(syncService);

        _service = service;
        _folderPicker = folderPicker;
        _syncService = syncService;
        _logger = logger;

        _pendingLoad = LoadAsync();
    }

    /// <summary>The in-flight (or most recently finished) load/save, so a test can await it deterministically instead of racing the constructor's fire-and-forget load.</summary>
    public Task PendingLoad => _pendingLoad;

    public PersonalSyncStatusPresentation StatusPresentation => PersonalSyncStatusPresentation.Build(Status);

    public bool HasSuggestedRoots => SuggestedRoots.Count > 0;

    public string? FirstSuggestedRoot => SuggestedRoots.Count > 0 ? SuggestedRoots[0] : null;

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Apply(await _service.LoadAsync().ConfigureAwait(true));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load Personal Sync settings.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Bound two-way to the On/Off control; a user-driven toggle persists immediately. Guarded against re-entrancy: <see cref="Apply"/> setting this same property while replaying a loaded snapshot must never itself trigger a second save.</summary>
    partial void OnPersonalSyncEnabledChanged(bool value)
    {
        if (_isApplyingSnapshot)
        {
            return;
        }

        _pendingLoad = SetEnabledAsync(value);
    }

    private async Task SetEnabledAsync(bool enabled)
    {
        IsBusy = true;
        try
        {
            Apply(await _service.SetEnabledAsync(enabled).ConfigureAwait(true));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to update Personal Sync enabled state.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanChooseFolder))]
    private async Task ChooseFolderAsync()
    {
        var picked = await _folderPicker.PickFolderAsync().ConfigureAwait(true);
        if (picked is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Apply(await _service.SetSyncRootAsync(picked).ConfigureAwait(true));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to set Personal Sync folder.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanChooseFolder() => !IsBusy;

    /// <summary>Phase 16C §25/§26: the one manual trigger this phase adds — no automatic/background cadence. Only meaningful once the folder/manifest are actually <see cref="PersonalSyncStatus.Ready"/>; <see cref="Carowinds.Passdown.Application.PersonalSync.IPersonalSyncService"/> itself already no-ops safely otherwise, but disabling the button avoids a confusing "nothing happened" click.</summary>
    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _syncService.RunAsync().ConfigureAwait(true);
            SyncResultSummary = PersonalSyncResultPresentation.Format(result);
            Apply(await _service.LoadAsync().ConfigureAwait(true));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to run a Personal Sync pass.");
            SyncResultSummary = "Sync failed unexpectedly. See the log for details.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSyncNow() => !IsBusy && Status == PersonalSyncStatus.Ready;

    private void Apply(PersonalSyncSettingsSnapshot snapshot)
    {
        _isApplyingSnapshot = true;
        try
        {
            PersonalSyncEnabled = snapshot.PersonalSyncEnabled;
            SyncRootPath = snapshot.SyncRootPath;
            Status = snapshot.Status;
            StatusDetail = snapshot.StatusDetail;
            SuggestedRoots = snapshot.SuggestedRoots;
            DeviceId = snapshot.DeviceId;
        }
        finally
        {
            _isApplyingSnapshot = false;
        }
    }
}
