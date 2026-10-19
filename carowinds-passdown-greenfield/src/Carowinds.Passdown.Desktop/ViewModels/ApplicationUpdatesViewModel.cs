using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Domain.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>Phase 23D: the states of Settings → Application Updates.</summary>
public enum ApplicationUpdatePanelState
{
    ManualUpdateRequired,
    NotChecked,
    Checking,
    UpToDate,
    CheckFailed,
    UpdateAvailable,
    Downloading,
    ReadyToInstall,
    Restarting,
}

/// <summary>
/// Phase 23D: Settings → Application Updates. Shows the installed version and the last successful check;
/// Check for Updates (disabled while a check runs); an approved update's version, date, size and notes with
/// Install Update / Remind Me Later; download progress with Cancel; and Ready to install with an explicit
/// Restart and Install. Nothing is ever downloaded or installed without a button press, and nothing closes
/// the app until the Associate draft is confirmed saved through the Phase 23A path — a failed save postpones
/// the update and keeps the edits on screen. All network and file work runs off the UI thread; results are
/// marshalled back through <see cref="IUiDispatcher"/>.
/// </summary>
public partial class ApplicationUpdatesViewModel : ViewModelBase
{
    private readonly IApplicationUpdateCoordinator? _coordinator;
    private readonly IUpdatePackageStager? _stager;
    private readonly IInstallationInspector? _inspector;
    private readonly IUpdateInstallLauncher? _launcher;
    private readonly IUpdateRestartHost? _restartHost;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<ApplicationUpdatesViewModel>? _logger;
    private readonly SemanticVersion? _installedVersion;
    private CancellationTokenSource? _downloadCancellation;
    private StagedUpdate? _staged;

    public ApplicationUpdatesViewModel(
        IApplicationUpdateCoordinator? coordinator = null,
        IUpdatePackageStager? stager = null,
        IInstallationInspector? inspector = null,
        IUpdateInstallLauncher? launcher = null,
        IUpdateRestartHost? restartHost = null,
        IUpdateOutcomeStore? outcomes = null,
        WhatsNewViewModel? whatsNew = null,
        IUiDispatcher? dispatcher = null,
        string? installedVersion = null,
        ILogger<ApplicationUpdatesViewModel>? logger = null)
    {
        _coordinator = coordinator;
        _stager = stager;
        _inspector = inspector;
        _launcher = launcher;
        _restartHost = restartHost;
        _dispatcher = dispatcher ?? new InlineDispatcher();
        _logger = logger;
        WhatsNew = whatsNew ?? new WhatsNewViewModel();
        InstalledVersionText = installedVersion ?? "unknown";
        _installedVersion = SemanticVersion.TryParse(installedVersion, out var parsed) ? parsed : null;
        PreviousAttemptText = ApplicationUpdatePresentation.PreviousAttemptText(outcomes?.LoadLatest(), InstalledVersionText);

        if (_coordinator is not null)
        {
            _coordinator.Changed += (_, snapshot) => _dispatcher.Post(() => Apply(snapshot));
            Apply(_coordinator.Current);
        }
        else
        {
            SetState(ApplicationUpdatePanelState.ManualUpdateRequired);
        }
    }

    public WhatsNewViewModel WhatsNew { get; }

    public string InstalledVersionText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualUpdateRequired), nameof(IsChecking), nameof(IsUpdateAvailable), nameof(IsDownloading), nameof(IsReadyToInstall), nameof(IsRestarting), nameof(ShowCheckButton))]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(InstallUpdateCommand), nameof(RestartAndInstallCommand), nameof(CancelDownloadCommand))]
    public partial ApplicationUpdatePanelState State { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? StatusDetail { get; set; }

    [ObservableProperty]
    public partial string LastCheckedText { get; set; } = ApplicationUpdatePresentation.LastCheckedText(null);

    [ObservableProperty]
    public partial string? AvailableVersionText { get; set; }

    [ObservableProperty]
    public partial string? ReleasedText { get; set; }

    [ObservableProperty]
    public partial string? DownloadSizeText { get; set; }

    [ObservableProperty]
    public partial string? ReleaseTitle { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ReleaseNotesBlock> ReleaseNotesBlocks { get; set; } = [];

    /// <summary>Why installation cannot happen here (folder not writable, not a release folder, not Windows). Null when it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallHere))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand), nameof(RestartAndInstallCommand))]
    public partial string? ReadinessText { get; set; }

    [ObservableProperty]
    public partial double DownloadPercent { get; set; }

    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    /// <summary>A problem with the last action (download, verification, save, launch). Cleared by the next action.</summary>
    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    public partial string? InfoText { get; set; }

    /// <summary>The previous install attempt, when it did not succeed (rolled back, not installed, recovery required).</summary>
    [ObservableProperty]
    public partial string? PreviousAttemptText { get; set; }

    /// <summary>The subtle Settings-gear indicator: an approved update the user has not snoozed.</summary>
    [ObservableProperty]
    public partial bool HasUpdateNotification { get; set; }

    public bool IsManualUpdateRequired => State == ApplicationUpdatePanelState.ManualUpdateRequired;

    public bool IsChecking => State == ApplicationUpdatePanelState.Checking;

    public bool IsUpdateAvailable => State == ApplicationUpdatePanelState.UpdateAvailable;

    public bool IsDownloading => State == ApplicationUpdatePanelState.Downloading;

    public bool IsReadyToInstall => State == ApplicationUpdatePanelState.ReadyToInstall;

    public bool IsRestarting => State == ApplicationUpdatePanelState.Restarting;

    public bool ShowCheckButton => State is not (ApplicationUpdatePanelState.ManualUpdateRequired or ApplicationUpdatePanelState.Downloading or ApplicationUpdatePanelState.ReadyToInstall or ApplicationUpdatePanelState.Restarting);

    public bool CanInstallHere => ReadinessText is null;

    /// <summary>The download/stage started by Install Update; tests await it.</summary>
    public Task PendingInstall { get; private set; } = Task.CompletedTask;

    private UpdateOffer? _offer;

    /// <summary>Stops a running download when the app closes. Safe to call at any time.</summary>
    public void CancelBackgroundWork() => _downloadCancellation?.Cancel();

    private bool CanCheck() => _coordinator is not null && State is ApplicationUpdatePanelState.NotChecked or ApplicationUpdatePanelState.UpToDate or ApplicationUpdatePanelState.CheckFailed or ApplicationUpdatePanelState.UpdateAvailable;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckForUpdatesAsync()
    {
        ErrorText = null;
        InfoText = null;
        try
        {
            await Task.Run(() => _coordinator!.CheckNowAsync()).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool CanInstall() => State == ApplicationUpdatePanelState.UpdateAvailable && _offer is not null && _stager is not null && CanInstallHere;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallUpdateAsync()
    {
        PendingInstall = StageAsync(_offer!);
        return PendingInstall;
    }

    private async Task StageAsync(UpdateOffer offer)
    {
        ErrorText = null;
        InfoText = null;
        var readiness = Inspect();
        if (readiness is not null)
        {
            ReadinessText = readiness;
            return;
        }

        _downloadCancellation = new CancellationTokenSource();
        DownloadPercent = 0;
        ProgressText = ApplicationUpdatePresentation.DownloadingText(new UpdateDownloadProgress(0, offer.Manifest.Package.Size));
        SetState(ApplicationUpdatePanelState.Downloading);
        var progress = new Progress<UpdateDownloadProgress>(value => _dispatcher.Post(() =>
        {
            if (State == ApplicationUpdatePanelState.Downloading)
            {
                DownloadPercent = value.Fraction * 100;
                ProgressText = ApplicationUpdatePresentation.DownloadingText(value);
            }
        }));

        try
        {
            var token = _downloadCancellation.Token;
            _staged = await Task.Run(() => _stager!.StageAsync(offer, progress, token), token).ConfigureAwait(true);
            ProgressText = null;
            SetState(ApplicationUpdatePanelState.ReadyToInstall);
        }
        catch (OperationCanceledException)
        {
            ProgressText = null;
            InfoText = "Download cancelled. Nothing was changed.";
            ShowOffer(offer);
        }
        catch (UpdateStagingException ex)
        {
            _logger?.LogWarning("Update staging failed ({Kind}).", ex.Kind);
            ProgressText = null;
            ErrorText = ApplicationUpdatePresentation.StagingFailedText(ex.Kind);
            ShowOffer(offer);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Update staging failed unexpectedly.");
            ProgressText = null;
            ErrorText = ApplicationUpdatePresentation.StagingFailedText(StagingFailureKind.StorageError);
            ShowOffer(offer);
        }
        finally
        {
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
        }
    }

    private bool CanCancelDownload() => State == ApplicationUpdatePanelState.Downloading;

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload() => _downloadCancellation?.Cancel();

    private bool CanRestart() => State == ApplicationUpdatePanelState.ReadyToInstall && _staged is not null && _launcher is not null && CanInstallHere;

    /// <summary>
    /// Restart and Install: save the draft (Phase 23A rules) → re-check the folder → start the staged updater →
    /// close. A failed save, a folder that became unwritable, or an updater that did not start leaves the app
    /// open with an explanation; the staged update stays ready for a later retry.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartAndInstallAsync()
    {
        ErrorText = null;
        InfoText = null;
        var staged = _staged!;
        SetState(ApplicationUpdatePanelState.Restarting);

        var saved = _restartHost is not null && await _restartHost.SaveWorkBeforeRestartAsync().ConfigureAwait(true);
        if (!saved)
        {
            _logger?.LogWarning("Update postponed: the Associate draft could not be saved.");
            ErrorText = ApplicationUpdatePresentation.SavePostponedText;
            SetState(ApplicationUpdatePanelState.ReadyToInstall);
            return;
        }

        var readiness = Inspect();
        if (readiness is not null)
        {
            ReadinessText = readiness;
            SetState(ApplicationUpdatePanelState.ReadyToInstall);
            return;
        }

        var launch = _launcher!.Launch(staged, _installedVersion ?? staged.Offer.Version);
        if (!launch.Started)
        {
            ErrorText = ApplicationUpdatePresentation.LaunchFailedText(launch.Failure);
            if (launch.Failure == UpdateLaunchFailureKind.StagedUpdaterMissing)
            {
                _staged = null;
                ShowOffer(staged.Offer);
            }
            else
            {
                SetState(ApplicationUpdatePanelState.ReadyToInstall);
            }

            return;
        }

        _restartHost!.CloseForUpdate();
    }

    [RelayCommand]
    private void NotNow()
    {
        if (State == ApplicationUpdatePanelState.ReadyToInstall && _offer is not null)
        {
            ShowOffer(_offer);
        }
    }

    [RelayCommand]
    private async Task RemindMeLaterAsync()
    {
        if (_offer is null || _coordinator is null)
        {
            return;
        }

        await _coordinator.SnoozeAsync(_offer.Version).ConfigureAwait(true);
        InfoText = ApplicationUpdatePresentation.RemindLaterText;
    }

    [RelayCommand]
    private void ShowWhatsNew() => WhatsNew.OpenCurrent(InstalledVersionText);

    private string? Inspect()
    {
        if (_inspector is null)
        {
            return ApplicationUpdatePresentation.ReadinessText(new InstallationReadiness(InstallationReadinessKind.NotPortableInstallation, string.Empty));
        }

        return ApplicationUpdatePresentation.ReadinessText(_inspector.Inspect());
    }

    private void Apply(UpdateAvailabilitySnapshot snapshot)
    {
        LastCheckedText = ApplicationUpdatePresentation.LastCheckedText(snapshot.LastSuccessfulCheckUtc);
        HasUpdateNotification = snapshot.ShouldNotify;

        // A check finishing in the background never interrupts a download or a pending restart.
        if (State is ApplicationUpdatePanelState.Downloading or ApplicationUpdatePanelState.ReadyToInstall or ApplicationUpdatePanelState.Restarting)
        {
            return;
        }

        switch (snapshot.Status)
        {
            case UpdateAvailabilityStatus.NotConfigured:
                SetState(ApplicationUpdatePanelState.ManualUpdateRequired);
                break;
            case UpdateAvailabilityStatus.NotChecked:
                SetState(ApplicationUpdatePanelState.NotChecked);
                break;
            case UpdateAvailabilityStatus.Checking:
                SetState(ApplicationUpdatePanelState.Checking);
                break;
            case UpdateAvailabilityStatus.UpToDate:
                _offer = null;
                SetState(ApplicationUpdatePanelState.UpToDate);
                break;
            case UpdateAvailabilityStatus.UpdateAvailable when snapshot.Offer is not null:
                ShowOffer(snapshot.Offer);
                break;
            case UpdateAvailabilityStatus.CheckFailed when snapshot.Offer is not null:
                // The verified offer from an earlier check is still valid; just say the latest check failed.
                ShowOffer(snapshot.Offer);
                InfoText = ApplicationUpdatePresentation.CheckFailedText(snapshot.FailureKind, snapshot.RetryAfterUtc);
                break;
            default:
                SetState(ApplicationUpdatePanelState.CheckFailed, ApplicationUpdatePresentation.CheckFailedText(snapshot.FailureKind, snapshot.RetryAfterUtc));
                break;
        }
    }

    private void ShowOffer(UpdateOffer offer)
    {
        _offer = offer;
        AvailableVersionText = offer.Version.Normalized;
        ReleaseTitle = offer.Manifest.Title;
        ReleasedText = ApplicationUpdatePresentation.ReleasedText(offer.Manifest.PublishedAtUtc);
        DownloadSizeText = ApplicationUpdatePresentation.DownloadSizeText(offer.DownloadBytes);
        ReleaseNotesBlocks = ReleaseNotesDocument.Parse(offer.Manifest.ReleaseNotes).Blocks;
        ReadinessText = Inspect();
        SetState(ApplicationUpdatePanelState.UpdateAvailable);
    }

    private void SetState(ApplicationUpdatePanelState state, string? failure = null)
    {
        State = state;
        (StatusTitle, StatusDetail) = state switch
        {
            ApplicationUpdatePanelState.ManualUpdateRequired => (ApplicationUpdatePresentation.NotConfiguredTitle, ApplicationUpdatePresentation.NotConfiguredDetail),
            ApplicationUpdatePanelState.NotChecked => (ApplicationUpdatePresentation.NotCheckedTitle, null),
            ApplicationUpdatePanelState.Checking => (ApplicationUpdatePresentation.CheckingTitle, null),
            ApplicationUpdatePanelState.UpToDate => (ApplicationUpdatePresentation.UpToDateTitle, null),
            ApplicationUpdatePanelState.CheckFailed => ("Couldn't check for updates", failure),
            ApplicationUpdatePanelState.UpdateAvailable => (ApplicationUpdatePresentation.AvailableTitle(AvailableVersionText ?? string.Empty), null),
            ApplicationUpdatePanelState.Downloading => ($"Downloading version {AvailableVersionText}", null),
            ApplicationUpdatePanelState.ReadyToInstall => (ApplicationUpdatePresentation.ReadyTitle(AvailableVersionText ?? string.Empty), ApplicationUpdatePresentation.ReadyDetail),
            ApplicationUpdatePanelState.Restarting => ("Saving your work and starting the update…", null),
            _ => (string.Empty, null),
        };
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }
}
