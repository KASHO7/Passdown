using Avalonia.Threading;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// The Settings / Personal Sync screen (Phase 16B §4/§14, Phase 16C §25/§26, Phase 16D §27): On/Off, the
/// configured folder, a Choose Folder action, a small status line, a manual "Sync Now" action with a
/// simple result line, and (16D) a minimal conflict summary/review surface — "Review Conflicts" opens a
/// list, selecting one shows a local-vs-remote comparison with explicit Keep This Device/Use Other
/// Device/Accept Deletion actions. All conflict logic lives in <see cref="IPersonalSyncConflictResolutionService"/>;
/// this type only coordinates what the user sees and which action they picked (spec §19). Deliberately
/// still no last-sync-time or automatic/background cadence — those remain 16F. Loads eagerly in its
/// constructor, mirroring <see cref="OperationsDashboardViewModel"/>'s own fire-and-forget-with-awaitable-handle
/// pattern so a test can await <see cref="PendingLoad"/> instead of racing it.
/// </summary>
public partial class PersonalSyncSettingsViewModel : ViewModelBase
{
    private readonly IPersonalSyncSettingsService _service;
    private readonly ISyncFolderPicker _folderPicker;
    private readonly IPersonalSyncConflictResolutionService _conflictService;
    private readonly IPersonalSyncCoordinator _coordinator;
    private readonly ILogger<PersonalSyncSettingsViewModel>? _logger;

    private Task _pendingLoad = Task.CompletedTask;

    /// <summary>Phase 18D "About this build": which exact copy is running (support/stale-copy troubleshooting).</summary>
    public BuildIdentity Build { get; }

    /// <summary>Phase 21B: Settings → Diagnostics (Export Diagnostics moved here from the Associate toolbar).</summary>
    public DiagnosticsExportViewModel Diagnostics { get; }

    public string BuildProductName => Build.ProductName;

    /// <summary>"0.1.0-rc.1 (096bc4a)" — the release version plus the source commit it was built from.</summary>
    public string BuildVersionText => Build.DisplayVersion;

    /// <summary>
    /// The running executable's folder and file, with the Windows profile prefix shown as
    /// <c>%USERPROFILE%</c> so the user name is never displayed — the rest of the path is intact, which
    /// is what tells the current release folder apart from an older portable copy.
    /// </summary>
    public string BuildExecutablePathText =>
        Build.ExecutablePath is { } path ? DiagnosticPathRedactor.Redact(path, []) : "Unknown";
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    [NotifyPropertyChangedFor(nameof(ConflictStatusText))]
    public partial int ConflictCount { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<SyncConflictSummary> Conflicts { get; set; } = [];

    [ObservableProperty]
    public partial bool IsReviewingConflicts { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(KeepLocalCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseRemoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(AcceptDeletionCommand))]
    public partial ConflictDetailPresentation? SelectedConflictDetail { get; set; }

    [ObservableProperty]
    public partial string? ConflictActionMessage { get; set; }

    public bool HasConflicts => ConflictCount > 0;

    public string ConflictStatusText => PersonalSyncConflictStatusPresentation.Format(ConflictCount);

    /// <summary>Phase 16F §12: the automatic-sync status — layered on top of <see cref="Status"/>/<see cref="StatusPresentation"/>, never replacing it. Kept current for the lifetime of this (single, app-session-long — see <see cref="MainViewModel"/>) instance via <see cref="IPersonalSyncCoordinator.StatusChanged"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationStatusPresentation))]
    [NotifyPropertyChangedFor(nameof(LastSyncedText))]
    public partial PersonalSyncAutomationSnapshot AutomationStatus { get; set; } = PersonalSyncAutomationSnapshot.Initial;

    public PersonalSyncAutomationStatusPresentation AutomationStatusPresentation => PersonalSyncAutomationStatusPresentation.Build(AutomationStatus);

    public string? LastSyncedText => PersonalSyncAutomationStatusPresentation.BuildLastSyncedText(AutomationStatus.LastSuccessfulSyncUtc);

    public PersonalSyncSettingsViewModel(
        IPersonalSyncSettingsService service,
        ISyncFolderPicker folderPicker,
        IPersonalSyncConflictResolutionService conflictService,
        IPersonalSyncCoordinator coordinator,
        ILogger<PersonalSyncSettingsViewModel>? logger = null,
        BuildIdentity? buildIdentity = null,
        DiagnosticsExportViewModel? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(conflictService);
        ArgumentNullException.ThrowIfNull(coordinator);

        _service = service;
        _folderPicker = folderPicker;
        _conflictService = conflictService;
        _coordinator = coordinator;
        _logger = logger;
        Build = buildIdentity ?? BuildIdentity.Current();
        Diagnostics = diagnostics ?? new DiagnosticsExportViewModel();

        AutomationStatus = coordinator.CurrentStatus;
        // This ViewModel is resolved exactly once for the app's session (MainViewModel's own
        // constructor parameter, itself resolved once in App.axaml.cs) — so this subscription lives
        // exactly as long as _coordinator does, never leaking across repeated Settings navigations.
        // A periodic/retry-triggered pass raises this from a background thread pool thread (there is
        // no user-initiated UI-thread call on that path to preserve), so the property update is
        // marshalled onto the UI thread rather than assumed to already be there.
        coordinator.StatusChanged += (_, snapshot) => Dispatcher.UIThread.Post(() => AutomationStatus = snapshot);

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
            await RefreshConflictsAsync().ConfigureAwait(true);
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

    /// <summary>
    /// Phase 16C §25/§26, Phase 16F §27: the manual trigger — routed through <see cref="IPersonalSyncCoordinator.SyncNowAsync"/>
    /// rather than calling <see cref="Carowinds.Passdown.Application.PersonalSync.IPersonalSyncService"/>
    /// directly, so a click bypasses any pending debounce and still respects the coordinator's overlap
    /// protection (never runs in parallel with an automatic pass). Only meaningful once the
    /// folder/manifest are actually <see cref="PersonalSyncStatus.Ready"/>; the underlying service
    /// itself already no-ops safely otherwise, but disabling the button avoids a confusing "nothing
    /// happened" click.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _coordinator.SyncNowAsync().ConfigureAwait(true);
            SyncResultSummary = PersonalSyncResultPresentation.Format(result);
            Apply(await _service.LoadAsync().ConfigureAwait(true));
            // Sync Now must never block on, or be blocked by, unrelated records (§26) — an unresolved
            // conflict from a prior pass simply keeps showing here, alongside this pass's own results.
            await RefreshConflictsAsync().ConfigureAwait(true);
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

    // ----- Phase 16D: conflict review and resolution -----

    [RelayCommand]
    private async Task ReviewConflictsAsync()
    {
        await RefreshConflictsAsync().ConfigureAwait(true);
        IsReviewingConflicts = true;
    }

    [RelayCommand]
    private void CloseConflicts()
    {
        IsReviewingConflicts = false;
        SelectedConflictDetail = null;
        ConflictActionMessage = null;
    }

    [RelayCommand]
    private async Task OpenConflictAsync(SyncConflictSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ConflictActionMessage = null;

        try
        {
            if (summary.EntityType == SyncEntityType.AssociateDraft)
            {
                if (SyncEntityIdentity.TryParseDraftId(summary.EntityId, out var operatingDate))
                {
                    var detail = await _conflictService.GetDraftConflictDetailAsync(operatingDate).ConfigureAwait(true);
                    SelectedConflictDetail = detail is null ? null : ConflictDetailPresentation.ForDraft(summary, detail);
                }
            }
            else if (SyncEntityIdentity.TryParseNoteId(summary.EntityId, out var noteId))
            {
                var detail = await _conflictService.GetNoteConflictDetailAsync(noteId).ConfigureAwait(true);
                SelectedConflictDetail = detail is null ? null : ConflictDetailPresentation.ForNote(summary, detail);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load a Personal Sync conflict's detail.");
            ConflictActionMessage = "Could not load this conflict's details. See the log for details.";
        }
    }

    [RelayCommand]
    private void CloseConflictDetail()
    {
        SelectedConflictDetail = null;
        ConflictActionMessage = null;
    }

    /// <summary>"Keep This Device" (spec §7/§15).</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedConflict))]
    private Task KeepLocalAsync() => ResolveSelectedConflictAsync(_conflictService.ResolveKeepLocalAsync);

    /// <summary>"Use Other Device" (spec §8) — disabled entirely for a remote-deletion conflict (§15's "do not call this Use Other Device" wording), enforced again at the service layer.</summary>
    [RelayCommand(CanExecute = nameof(CanUseRemote))]
    private Task UseRemoteAsync() => ResolveSelectedConflictAsync(_conflictService.ResolveUseRemoteAsync);

    /// <summary>"Accept Deletion" (spec §15) — only offered for an actual remote-deletion conflict.</summary>
    [RelayCommand(CanExecute = nameof(CanAcceptDeletion))]
    private Task AcceptDeletionAsync() => ResolveSelectedConflictAsync(_conflictService.ResolveAcceptDeletionAsync);

    private bool HasSelectedConflict() => SelectedConflictDetail is not null;

    private bool CanUseRemote() => SelectedConflictDetail is { IsRemoteDeletion: false };

    private bool CanAcceptDeletion() => SelectedConflictDetail is { IsRemoteDeletion: true };

    private async Task ResolveSelectedConflictAsync(Func<SyncEntityType, string, CancellationToken, Task<ConflictResolutionResult>> resolve)
    {
        var selected = SelectedConflictDetail;
        if (selected is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await resolve(selected.EntityType, selected.EntityId, default).ConfigureAwait(true);
            if (result.Outcome is ConflictResolutionOutcome.Resolved or ConflictResolutionOutcome.NoConflictFound)
            {
                SelectedConflictDetail = null;
                ConflictActionMessage = null;
                await RefreshConflictsAsync().ConfigureAwait(true);
            }
            else
            {
                ConflictActionMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to resolve a Personal Sync conflict.");
            ConflictActionMessage = "Something went wrong resolving this conflict. See the log for details.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshConflictsAsync()
    {
        var conflicts = await _conflictService.GetConflictsAsync().ConfigureAwait(true);
        Conflicts = conflicts;
        ConflictCount = conflicts.Count;
    }

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
