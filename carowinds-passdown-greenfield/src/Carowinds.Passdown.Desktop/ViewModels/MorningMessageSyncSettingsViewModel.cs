using Carowinds.Passdown.Application.MorningMessage.Sync;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 30: Settings → Personal Sync → Sync Morning Messages — the per-computer On/Off choice (off by
/// default), its status line, and review of Morning Message conflicts with Keep This Device / Use Other
/// Device. Every decision lives in <see cref="IMorningMessageSyncService"/>; this type only shows state and
/// passes the user's choice on. <see cref="PersonalSyncSettingsViewModel"/> calls <see cref="RefreshAsync"/>
/// whenever the Personal Sync folder status changes or a pass finishes.
/// </summary>
public partial class MorningMessageSyncSettingsViewModel : ViewModelBase
{
    private readonly IMorningMessageSyncService _service;
    private readonly IPersonalSyncCoordinator? _coordinator;
    private readonly ILogger<MorningMessageSyncSettingsViewModel>? _logger;
    private bool _isApplying;
    private PersonalSyncStatus _personalSyncStatus = PersonalSyncStatus.Off;

    public MorningMessageSyncSettingsViewModel(
        IMorningMessageSyncService service,
        IPersonalSyncCoordinator? coordinator = null,
        ILogger<MorningMessageSyncSettingsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _coordinator = coordinator;
        _logger = logger;
    }

    public static string Description => MorningMessageSyncPresentation.Description;

    public static string DisableExplanation => MorningMessageSyncPresentation.DisableExplanation;

    public static string NeedsFolderText => MorningMessageSyncPresentation.NeedsFolderText;

    public static string ScopeNote => MorningMessageSyncPresentation.ScopeNote;

    /// <summary>The in-flight (or most recent) refresh/save, so a test can await it.</summary>
    public Task PendingWork { get; private set; } = Task.CompletedTask;

    /// <summary>Personal Sync is on with a folder chosen — the only time the option can be changed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNeedsFolder))]
    public partial bool IsAvailable { get; set; }

    public bool ShowNeedsFolder => !IsAvailable;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = MorningMessageSyncPresentation.Status(MorningMessageSyncStatus.NotSyncing).Text;

    [ObservableProperty]
    public partial bool StatusIsProblem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    public partial IReadOnlyList<MorningMessageConflictPresentation> Conflicts { get; set; } = [];

    public bool HasConflicts => Conflicts.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedConflict))]
    [NotifyCanExecuteChangedFor(nameof(KeepThisDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseOtherDeviceCommand))]
    public partial MorningMessageConflictPresentation? SelectedConflict { get; set; }

    public bool HasSelectedConflict => SelectedConflict is not null;

    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>Re-reads the status and conflicts for the given Personal Sync folder state.</summary>
    public Task RefreshAsync(PersonalSyncStatus personalSyncStatus, bool syncFolderConfigured)
    {
        _personalSyncStatus = personalSyncStatus;
        IsAvailable = syncFolderConfigured;
        return PendingWork = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var overview = await _service.GetOverviewAsync(_personalSyncStatus).ConfigureAwait(true);
            _isApplying = true;
            try
            {
                IsEnabled = overview.Enabled;
            }
            finally
            {
                _isApplying = false;
            }

            (StatusText, StatusIsProblem) = MorningMessageSyncPresentation.Status(overview.Status);

            var conflicts = await _service.GetConflictsAsync().ConfigureAwait(true);
            Conflicts = conflicts.Select(conflict => new MorningMessageConflictPresentation(conflict)).ToList();
            if (SelectedConflict is { } selected)
            {
                SelectedConflict = Conflicts.FirstOrDefault(conflict => conflict.OperatingDate == selected.OperatingDate);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Morning Message sync status could not be loaded ({ExceptionType}).", ex.GetType().Name);
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_isApplying)
        {
            PendingWork = SetEnabledAsync(value);
        }
    }

    private async Task SetEnabledAsync(bool enabled)
    {
        try
        {
            await _service.SetEnabledAsync(enabled).ConfigureAwait(true);
            Message = null;
            if (enabled)
            {
                _coordinator?.RequestSync(SyncTriggerReason.LocalChange);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Sync Morning Messages could not be saved ({ExceptionType}).", ex.GetType().Name);
            Message = "Couldn't save this setting on this computer. Try again.";
        }

        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ReviewConflict(MorningMessageConflictPresentation conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        Message = null;
        SelectedConflict = conflict;
    }

    [RelayCommand]
    private void CloseConflict()
    {
        SelectedConflict = null;
        Message = null;
    }

    /// <summary>"Keep This Device": this computer's text wins for every conflicting part of the record, and is sent to the others.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedConflict))]
    private Task KeepThisDeviceAsync() => ResolveAsync(MorningMessageConflictResolution.KeepThisDevice);

    /// <summary>"Use Other Device": the other computer's text replaces this computer's for every conflicting part.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedConflict))]
    private Task UseOtherDeviceAsync() => ResolveAsync(MorningMessageConflictResolution.UseOtherDevice);

    private async Task ResolveAsync(MorningMessageConflictResolution resolution)
    {
        if (SelectedConflict is not { } selected)
        {
            return;
        }

        try
        {
            var outcome = await _service.ResolveAsync(selected.OperatingDate, resolution).ConfigureAwait(true);
            Message = outcome switch
            {
                MorningMessageResolveOutcome.ComposerOpen => "Close the Morning Message window first, so nothing you are typing is replaced.",
                MorningMessageResolveOutcome.Failed => "Couldn't save your choice on this computer. Nothing was changed.",
                _ => null,
            };

            if (outcome is MorningMessageResolveOutcome.Resolved or MorningMessageResolveOutcome.NoConflictFound)
            {
                SelectedConflict = null;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("A Morning Message conflict could not be resolved ({ExceptionType}).", ex.GetType().Name);
            Message = "Something went wrong saving your choice. Nothing was changed.";
        }

        await LoadAsync().ConfigureAwait(true);
    }
}
