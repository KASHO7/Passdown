using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// The Settings / Personal Sync screen (Phase 16B §4/§14): On/Off, the configured folder, a Choose
/// Folder action, and a small status line — deliberately nothing more (no last-sync time, no "Sync
/// now", no conflict count; those belong to a later phase). Loads eagerly in its constructor,
/// mirroring <see cref="OperationsDashboardViewModel"/>'s own fire-and-forget-with-awaitable-handle
/// pattern so a test can await <see cref="PendingLoad"/> instead of racing it.
/// </summary>
public partial class PersonalSyncSettingsViewModel : ViewModelBase
{
    private readonly IPersonalSyncSettingsService _service;
    private readonly ISyncFolderPicker _folderPicker;
    private readonly ILogger<PersonalSyncSettingsViewModel>? _logger;

    private Task _pendingLoad = Task.CompletedTask;
    private bool _isApplyingSnapshot;

    [ObservableProperty]
    public partial bool PersonalSyncEnabled { get; set; }

    [ObservableProperty]
    public partial string? SyncRootPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusPresentation))]
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
    public partial bool IsBusy { get; set; }

    public PersonalSyncSettingsViewModel(
        IPersonalSyncSettingsService service,
        ISyncFolderPicker folderPicker,
        ILogger<PersonalSyncSettingsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(folderPicker);

        _service = service;
        _folderPicker = folderPicker;
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
