using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Updates;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 23D: Settings → Application Updates and What's New, view-model and markup level (no real window; mixed
/// partition). Covers user approval, staging progress/errors, the Phase 23A save-failure integration, the subtle
/// indicator, show-once What's New and accessibility/theme rules for the new markup.
/// </summary>
public sealed class Phase23DApplicationUpdatesTests
{
    private const string Installed = "0.1.0-rc.7";
    private const string Next = "0.1.0-rc.8";

    // ---------------------------------------------------------------- fakes

    private sealed class FakeCoordinator : IApplicationUpdateCoordinator
    {
        public UpdateAvailabilitySnapshot Current { get; private set; } = new(UpdateAvailabilityStatus.NotChecked, null, null, null, null, false);

        public event EventHandler<UpdateAvailabilitySnapshot>? Changed;

        public int Checks { get; private set; }

        public List<string> Snoozed { get; } = [];

        public List<string> Acknowledged { get; } = [];

        public UpdateAvailabilitySnapshot? NextResult { get; set; }

        public string? LastAcknowledgedReleaseNotesVersion => Acknowledged.LastOrDefault();

        public void Publish(UpdateAvailabilitySnapshot snapshot)
        {
            Current = snapshot;
            Changed?.Invoke(this, snapshot);
        }

        public void Start()
        {
        }

        public Task StopAsync() => Task.CompletedTask;

        public Task<UpdateAvailabilitySnapshot> CheckNowAsync(CancellationToken cancellationToken = default)
        {
            Checks++;
            Publish(Current with { Status = UpdateAvailabilityStatus.Checking });
            Publish(NextResult ?? Current with { Status = UpdateAvailabilityStatus.UpToDate, LastSuccessfulCheckUtc = DateTimeOffset.UnixEpoch });
            return Task.FromResult(Current);
        }

        public Task SnoozeAsync(SemanticVersion version)
        {
            Snoozed.Add(version.Normalized);
            Publish(Current with { IsSnoozed = true });
            return Task.CompletedTask;
        }

        public Task AcknowledgeReleaseNotesAsync(SemanticVersion version)
        {
            Acknowledged.Add(version.Normalized);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStager : IUpdatePackageStager
    {
        public UpdateStagingException? Failure { get; set; }

        public TaskCompletionSource? Gate { get; set; }

        public int Calls { get; private set; }

        public async Task<StagedUpdate> StageAsync(UpdateOffer offer, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken = default)
        {
            Calls++;
            progress?.Report(new UpdateDownloadProgress(offer.DownloadBytes / 2, offer.DownloadBytes));
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            return new StagedUpdate(offer, "/staging/" + offer.Version);
        }

        public void CleanObsolete()
        {
        }
    }

    private sealed class FakeInspector : IInstallationInspector
    {
        public InstallationReadinessKind Kind { get; set; } = InstallationReadinessKind.Ready;

        public InstallationReadiness Inspect() => new(Kind, "C:/Apps/Carowinds");
    }

    private sealed class FakeLauncher : IUpdateInstallLauncher
    {
        public UpdateLaunchFailureKind Result { get; set; } = UpdateLaunchFailureKind.None;

        public List<StagedUpdate> Launched { get; } = [];

        public UpdateLaunchResult Launch(StagedUpdate staged, SemanticVersion installedVersion)
        {
            Launched.Add(staged);
            return new UpdateLaunchResult(Result, Guid.NewGuid());
        }
    }

    private sealed class FakeRestartHost(Func<Task<bool>>? save = null) : IUpdateRestartHost
    {
        public int Closes { get; private set; }

        public int Saves { get; private set; }

        public Task<bool> SaveWorkBeforeRestartAsync()
        {
            Saves++;
            return save?.Invoke() ?? Task.FromResult(true);
        }

        public void CloseForUpdate() => Closes++;
    }

    private sealed class FakeOutcomes(UpdateInstallOutcome? latest) : IUpdateOutcomeStore
    {
        public UpdateInstallOutcome? LoadLatest() => latest;

        public bool ConfirmLaunch(Guid requestId, SemanticVersion runningVersion) => false;
    }

    private sealed class FakeNotes(ReleaseNotesContent? content) : IBundledReleaseNotesSource
    {
        public ReleaseNotesContent? LoadCurrent() => content;
    }

    private sealed record Harness(
        ApplicationUpdatesViewModel ViewModel,
        FakeCoordinator Coordinator,
        FakeStager Stager,
        FakeInspector Inspector,
        FakeLauncher Launcher,
        FakeRestartHost Restart);

    private static UpdateOffer Offer(string version = Next)
    {
        var parsed = SemanticVersion.Parse(version);
        var manifest = new UpdateManifest(1, UpdateManifest.CarowindsOperationsProductId, parsed, UpdateChannel.ReleaseCandidate, "win-x64",
            new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero), "Updates built in", "## Highlights\n- Update from Settings\n<script>x</script>",
            new UpdatePackageDescriptor(UpdateAssetNames.Package(parsed), 94_371_840, new string('a', 64), UpdateAssetNames.ReleaseName(parsed)), []);
        return new UpdateOffer(new ReleaseCandidate(UpdateAssetNames.Tag(parsed), parsed, null, null, false, []), manifest, 94_371_840);
    }

    private static Harness Create(FakeRestartHost? restart = null, UpdateInstallOutcome? previous = null, IBundledReleaseNotesSource? notes = null)
    {
        var coordinator = new FakeCoordinator();
        var stager = new FakeStager();
        var inspector = new FakeInspector();
        var launcher = new FakeLauncher();
        restart ??= new FakeRestartHost();
        var whatsNew = new WhatsNewViewModel(notes, coordinator);
        var vm = new ApplicationUpdatesViewModel(coordinator, stager, inspector, launcher, restart, new FakeOutcomes(previous), whatsNew, new ImmediateUiDispatcher(), Installed);
        return new Harness(vm, coordinator, stager, inspector, launcher, restart);
    }

    private static UpdateAvailabilitySnapshot Available(UpdateOffer? offer = null) =>
        new(UpdateAvailabilityStatus.UpdateAvailable, offer ?? Offer(), DateTimeOffset.UnixEpoch, null, null, false);

    // ---------------------------------------------------------------- states

    [Fact]
    public void WithoutUpdateServices_SettingsSaysManualUpdateRequired()
    {
        var vm = new ApplicationUpdatesViewModel(installedVersion: Installed);
        Assert.Equal(ApplicationUpdatePanelState.ManualUpdateRequired, vm.State);
        Assert.Equal(ApplicationUpdatePresentation.NotConfiguredTitle, vm.StatusTitle);
        Assert.False(vm.CheckForUpdatesCommand.CanExecute(null));
        Assert.False(vm.HasUpdateNotification);
    }

    [Fact]
    public void NotConfiguredCoordinator_IsManualUpdateRequired()
    {
        var h = Create();
        h.Coordinator.Publish(new UpdateAvailabilitySnapshot(UpdateAvailabilityStatus.NotConfigured, null, null, null, null, false));
        Assert.True(h.ViewModel.IsManualUpdateRequired);
        Assert.False(h.ViewModel.ShowCheckButton);
    }

    [Fact]
    public async Task CheckForUpdates_ShowsUpToDate_WithInstalledVersionAndLastCheck()
    {
        var h = Create();
        Assert.Equal(Installed, h.ViewModel.InstalledVersionText);
        await h.ViewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(1, h.Coordinator.Checks);
        Assert.Equal(ApplicationUpdatePanelState.UpToDate, h.ViewModel.State);
        Assert.Equal(ApplicationUpdatePresentation.UpToDateTitle, h.ViewModel.StatusTitle);
        Assert.StartsWith("Last checked: ", h.ViewModel.LastCheckedText);
        Assert.NotEqual("Last checked: never", h.ViewModel.LastCheckedText);
    }

    [Fact]
    public void WhileChecking_TheButtonCannotBePressedAgain()
    {
        var h = Create();
        h.Coordinator.Publish(h.Coordinator.Current with { Status = UpdateAvailabilityStatus.Checking });
        Assert.True(h.ViewModel.IsChecking);
        Assert.False(h.ViewModel.CheckForUpdatesCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(UpdateSourceFailureKind.Offline, "offline")]
    [InlineData(UpdateSourceFailureKind.Timeout, "did not respond")]
    [InlineData(UpdateSourceFailureKind.RateLimited, "wait before checking")]
    [InlineData(UpdateSourceFailureKind.HttpError, "right now")]
    public void CheckFailures_ArePlainLanguage_AndRetryIsAvailable(UpdateSourceFailureKind kind, string expected)
    {
        var h = Create();
        h.Coordinator.Publish(new UpdateAvailabilitySnapshot(UpdateAvailabilityStatus.CheckFailed, null, null, kind, null, false));
        Assert.Equal(ApplicationUpdatePanelState.CheckFailed, h.ViewModel.State);
        Assert.Contains(expected, h.ViewModel.StatusDetail, StringComparison.Ordinal);
        Assert.True(h.ViewModel.CheckForUpdatesCommand.CanExecute(null));
    }

    [Fact]
    public void UpdateAvailable_ShowsVersionDateSizeAndSafeNotes_AndTheIndicator()
    {
        var h = Create();
        h.Coordinator.Publish(Available());

        var vm = h.ViewModel;
        Assert.True(vm.IsUpdateAvailable);
        Assert.Equal("Version 0.1.0-rc.8 is available.", vm.StatusTitle);
        Assert.Equal("Released October 20, 2026", vm.ReleasedText);
        Assert.Equal("Download size: 90.0 MB", vm.DownloadSizeText);
        Assert.Equal("Updates built in", vm.ReleaseTitle);
        Assert.Equal(["Highlights", "Update from Settings", "x"], vm.ReleaseNotesBlocks.Select(b => b.Text));
        Assert.True(vm.HasUpdateNotification);
        Assert.True(vm.InstallUpdateCommand.CanExecute(null));
        Assert.Null(vm.ReadinessText);
    }

    [Fact]
    public void RemindMeLater_HidesTheIndicator_KeepsTheOffer()
    {
        var h = Create();
        h.Coordinator.Publish(Available());
        h.ViewModel.RemindMeLaterCommand.Execute(null);

        Assert.Equal([Next], h.Coordinator.Snoozed);
        Assert.False(h.ViewModel.HasUpdateNotification);
        Assert.True(h.ViewModel.IsUpdateAvailable);
        Assert.Equal(ApplicationUpdatePresentation.RemindLaterText, h.ViewModel.InfoText);
    }

    [Theory]
    [InlineData(InstallationReadinessKind.NotWritable, "No administrator access is requested")]
    [InlineData(InstallationReadinessKind.NotPortableInstallation, "isn't running from a standard release folder")]
    [InlineData(InstallationReadinessKind.UnsupportedPlatform, "only on Windows")]
    public void InstallationThatCannotWork_ExplainsTheManualPath_AndDisablesInstall(InstallationReadinessKind kind, string expected)
    {
        var h = Create();
        h.Inspector.Kind = kind;
        h.Coordinator.Publish(Available());

        Assert.Contains(expected, h.ViewModel.ReadinessText, StringComparison.Ordinal);
        Assert.False(h.ViewModel.InstallUpdateCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- download and approval

    [Fact]
    public async Task InstallUpdate_DownloadsWithProgress_ThenWaitsForExplicitRestart_NothingInstalledYet()
    {
        var h = Create();
        h.Coordinator.Publish(Available());
        h.Stager.Gate = new TaskCompletionSource();

        var install = h.ViewModel.InstallUpdateCommand.ExecuteAsync(null);
        Assert.True(h.ViewModel.IsDownloading);
        // Progress<T> reports on the thread pool; wait for the half-way report to arrive.
        for (var i = 0; i < 500 && h.ViewModel.DownloadPercent == 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(50, h.ViewModel.DownloadPercent, 1);
        Assert.Contains("50%", h.ViewModel.ProgressText, StringComparison.Ordinal);
        Assert.True(h.ViewModel.CancelDownloadCommand.CanExecute(null));
        Assert.False(h.ViewModel.CheckForUpdatesCommand.CanExecute(null));

        h.Stager.Gate.SetResult();
        await install;

        Assert.True(h.ViewModel.IsReadyToInstall);
        Assert.Equal(ApplicationUpdatePresentation.ReadyDetail, h.ViewModel.StatusDetail);
        Assert.Empty(h.Launcher.Launched);
        Assert.Equal(0, h.Restart.Closes);
    }

    [Fact]
    public async Task CancelDownload_ReturnsToTheOffer_NothingChanged()
    {
        var h = Create();
        h.Coordinator.Publish(Available());
        h.Stager.Gate = new TaskCompletionSource();

        var install = h.ViewModel.InstallUpdateCommand.ExecuteAsync(null);
        h.ViewModel.CancelDownloadCommand.Execute(null);
        await install;

        Assert.True(h.ViewModel.IsUpdateAvailable);
        Assert.Contains("cancelled", h.ViewModel.InfoText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StagingFailureKind.HashMismatch, "didn't pass verification")]
    [InlineData(StagingFailureKind.InvalidArchive, "didn't pass verification")]
    [InlineData(StagingFailureKind.Download, "didn't finish")]
    [InlineData(StagingFailureKind.InsufficientDiskSpace, "free disk space")]
    public async Task StagingFailures_AreExplained_AndRetryIsPossible(StagingFailureKind kind, string expected)
    {
        var h = Create();
        h.Coordinator.Publish(Available());
        h.Stager.Failure = new UpdateStagingException(kind, "synthetic");

        await h.ViewModel.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Contains(expected, h.ViewModel.ErrorText, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", h.ViewModel.ErrorText, StringComparison.Ordinal);
        Assert.True(h.ViewModel.InstallUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task BackgroundCheck_NeverInterruptsADownload()
    {
        var h = Create();
        h.Coordinator.Publish(Available());
        h.Stager.Gate = new TaskCompletionSource();
        var install = h.ViewModel.InstallUpdateCommand.ExecuteAsync(null);

        h.Coordinator.Publish(new UpdateAvailabilitySnapshot(UpdateAvailabilityStatus.UpToDate, null, DateTimeOffset.UnixEpoch, null, null, false));
        Assert.True(h.ViewModel.IsDownloading);

        h.Stager.Gate.SetResult();
        await install;
        Assert.True(h.ViewModel.IsReadyToInstall);
    }

    [Fact]
    public async Task RestartAndInstall_SavesFirst_StartsTheUpdater_ThenCloses()
    {
        var h = Create();
        h.Coordinator.Publish(Available());
        await h.ViewModel.InstallUpdateCommand.ExecuteAsync(null);

        await h.ViewModel.RestartAndInstallCommand.ExecuteAsync(null);

        Assert.Equal(1, h.Restart.Saves);
        Assert.Single(h.Launcher.Launched);
        Assert.Equal(1, h.Restart.Closes);
    }

    [Fact]
    public async Task NotNow_KeepsTheAppOpen_AndTheOfferAvailable()
    {
        var h = Create();
        h.Coordinator.Publish(Available());
        await h.ViewModel.InstallUpdateCommand.ExecuteAsync(null);

        h.ViewModel.NotNowCommand.Execute(null);

        Assert.True(h.ViewModel.IsUpdateAvailable);
        Assert.Equal(0, h.Restart.Closes);
        Assert.Empty(h.Launcher.Launched);
    }

    [Theory]
    [InlineData(UpdateLaunchFailureKind.UpdaterDidNotStart)]
    [InlineData(UpdateLaunchFailureKind.RequestNotWritten)]
    [InlineData(UpdateLaunchFailureKind.StagedUpdaterMissing)]
    public async Task UpdaterThatDoesNotStart_NeverClosesTheApp(UpdateLaunchFailureKind failure)
    {
        var h = Create();
        h.Coordinator.Publish(Available());
        await h.ViewModel.InstallUpdateCommand.ExecuteAsync(null);
        h.Launcher.Result = failure;

        await h.ViewModel.RestartAndInstallCommand.ExecuteAsync(null);

        Assert.Equal(0, h.Restart.Closes);
        Assert.Contains("Nothing was changed", h.ViewModel.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FolderThatBecameUnwritable_StopsBeforeTheUpdaterStarts()
    {
        var h = Create();
        h.Coordinator.Publish(Available());
        await h.ViewModel.InstallUpdateCommand.ExecuteAsync(null);
        h.Inspector.Kind = InstallationReadinessKind.NotWritable;

        await h.ViewModel.RestartAndInstallCommand.ExecuteAsync(null);

        Assert.Empty(h.Launcher.Launched);
        Assert.Equal(0, h.Restart.Closes);
        Assert.NotNull(h.ViewModel.ReadinessText);
    }

    // ---------------------------------------------------------------- Phase 23A integration

    [Fact]
    public async Task FailedDraftSave_PostponesTheUpdate_KeepsEdits_NoLoop_NoClose_AndAllowsRetry()
    {
        var service = new FakeAssociatePassdownService { SaveLoopGuardLimit = 500 };
        var scheduler = new ManualDebounceScheduler();
        var associate = new AssociateViewModel(service, new FakeClock(new DateTimeOffset(2026, 10, 17, 15, 0, 0, TimeSpan.Zero)), autosaveDelay: scheduler.Delay);
        await associate.PendingLoad;
        service.SaveException = new IOException("disk I/O error");
        associate.ProjectedAttendanceText = "38,500";
        scheduler.ElapseLatest();
        await associate.PendingAutosave;

        var main = CreateMain(associate);
        var restart = new FakeRestartHost(() => App.FlushPendingAutosaveBeforeShutdownAsync(main, NullLogger.Instance));
        var h = Create(restart);
        h.Coordinator.Publish(Available());
        await h.ViewModel.InstallUpdateCommand.ExecuteAsync(null);

        var restartTask = h.ViewModel.RestartAndInstallCommand.ExecuteAsync(null);
        Assert.Same(restartTask, await Task.WhenAny(restartTask, Task.Delay(TimeSpan.FromSeconds(10)))); // no freeze
        await restartTask;

        Assert.Equal(ApplicationUpdatePresentation.SavePostponedText, h.ViewModel.ErrorText);
        Assert.True(h.ViewModel.IsReadyToInstall);
        Assert.Empty(h.Launcher.Launched);
        Assert.Equal(0, restart.Closes);
        Assert.Equal(AssociateAutosaveState.SaveFailed, associate.AutosaveState); // never a false "saved"
        Assert.Equal("38,500", associate.ProjectedAttendanceText);
        Assert.Equal(2, service.SaveCalls.Count); // one final attempt, not a retry loop

        // The storage recovers: the same staged update can now be installed.
        service.SaveException = null;
        await h.ViewModel.RestartAndInstallCommand.ExecuteAsync(null);
        Assert.Single(h.Launcher.Launched);
        Assert.Equal(1, restart.Closes);
        Assert.Equal(38500, service.StoredDraft(new DateOnly(2026, 10, 17))!.ProjectedAttendance);
    }

    private static MainViewModel CreateMain(AssociateViewModel associate, PersonalSyncSettingsViewModel? settings = null)
    {
        var now = new DateTimeOffset(2026, 10, 17, 15, 0, 0, TimeSpan.Zero);
        return new MainViewModel(
            new OperationsDashboardViewModel(new FakeOperationsDashboardService()),
            new OperationsCalendarViewModel(new FakeOperationsCalendarService(), new FakeClock(now)),
            associate,
            new OperationalNotesViewModel(new FakeOperationalNotesService(), new FakeClock(now)),
            new PassdownHistoryViewModel(new FakePassdownHistoryService(), new FakeFinalizedPassdownPdfRetrievalService()),
            settings ?? new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator()));
    }

    // ---------------------------------------------------------------- previous attempts and indicator

    [Theory]
    [InlineData(UpdateInstallStatus.RolledBack, null, "was restored")]
    [InlineData(UpdateInstallStatus.NotInstalled, "installation-in-use", "6 AM scheduled refresh")]
    [InlineData(UpdateInstallStatus.NotInstalled, "not-writable", "didn't allow changes")]
    [InlineData(UpdateInstallStatus.RecoveryRequired, "rollback-failed", "UPDATE-RECOVERY.txt")]
    [InlineData(UpdateInstallStatus.NotInstalled, "database-backup-failed", "couldn't be backed up first. Nothing was changed.")]
    [InlineData(UpdateInstallStatus.NotInstalled, "database-in-use", "another program was using your database")]
    [InlineData(UpdateInstallStatus.NotInstalled, "database-damaged", "failed its safety check")]
    [InlineData(UpdateInstallStatus.RecoveryRequired, "database-upgraded-not-confirmed", "a backup of your database from before the update was kept. See UPDATE-RECOVERY.txt")]
    public void PreviousFailedAttempt_IsReportedHonestly(UpdateInstallStatus status, string? code, string expected)
    {
        var outcome = new UpdateInstallOutcome(Guid.NewGuid(), Installed, Next, status, code, DateTimeOffset.UnixEpoch, null);
        Assert.Contains(expected, Create(previous: outcome).ViewModel.PreviousAttemptText, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulOrUnrelatedAttempts_AreNotReportedAsProblems()
    {
        Assert.Null(Create(previous: new UpdateInstallOutcome(Guid.NewGuid(), "0.1.0-rc.6", Installed, UpdateInstallStatus.Succeeded, null, DateTimeOffset.UnixEpoch, null)).ViewModel.PreviousAttemptText);
        Assert.Null(Create(previous: new UpdateInstallOutcome(Guid.NewGuid(), "0.1.0-rc.5", "0.1.0-rc.6", UpdateInstallStatus.RolledBack, null, DateTimeOffset.UnixEpoch, null)).ViewModel.PreviousAttemptText);
        Assert.Null(Create().ViewModel.PreviousAttemptText);
    }

    [Fact]
    public async Task MainShell_ShowsTheIndicatorOnlyForAnUnsnoozedUpdate_AndNarratorHearsIt()
    {
        var h = Create();
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator(), updates: h.ViewModel);
        var associate = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(DateTimeOffset.UnixEpoch));
        await associate.PendingLoad;
        var main = CreateMain(associate, settings);

        Assert.False(main.HasUpdateNotification);
        Assert.Null(main.SettingsNavigationHelpText);

        h.Coordinator.Publish(Available());
        Assert.True(main.HasUpdateNotification);
        Assert.Equal("Update available", main.SettingsNavigationHelpText);

        h.ViewModel.RemindMeLaterCommand.Execute(null);
        Assert.False(main.HasUpdateNotification);
        Assert.Same(h.ViewModel.WhatsNew, main.WhatsNew);
    }

    // ---------------------------------------------------------------- What's New

    private static readonly ReleaseNotesContent Rc7Notes = new(Installed, "Redesigned look", new DateOnly(2026, 10, 17), ReleaseNotesDocument.Parse("- One\n- Two"));

    [Fact]
    public async Task WhatsNew_OpensWithBundledNotes_AndClosingAcknowledgesThisVersionLocally()
    {
        var h = Create(notes: new FakeNotes(Rc7Notes));
        h.ViewModel.ShowWhatsNewCommand.Execute(null);

        var whatsNew = h.ViewModel.WhatsNew;
        Assert.True(whatsNew.IsOpen);
        Assert.Equal("Version 0.1.0-rc.7", whatsNew.VersionText);
        Assert.Equal("Redesigned look", whatsNew.Title);
        Assert.Equal("Released October 17, 2026", whatsNew.PublishedText);
        Assert.Equal(["One", "Two"], whatsNew.Blocks.Select(b => b.Text));

        whatsNew.CloseCommand.Execute(null);
        await whatsNew.PendingAcknowledge;
        Assert.False(whatsNew.IsOpen);
        Assert.Equal([Installed], h.Coordinator.Acknowledged);
    }

    [Fact]
    public void WhatsNew_NotesForAnotherVersion_AreNeverShownAsThisVersions()
    {
        var h = Create(notes: new FakeNotes(Rc7Notes with { Version = Next }));
        h.ViewModel.ShowWhatsNewCommand.Execute(null);
        Assert.False(h.ViewModel.WhatsNew.HasNotes);
        Assert.Equal("Version 0.1.0-rc.7", h.ViewModel.WhatsNew.VersionText);
    }

    [Fact]
    public void WhatsNew_MissingNotes_AreHandledGracefully()
    {
        var h = Create(notes: new FakeNotes(null));
        h.ViewModel.ShowWhatsNewCommand.Execute(null);
        Assert.True(h.ViewModel.WhatsNew.IsOpen);
        Assert.False(h.ViewModel.WhatsNew.HasNotes);
    }

    [Fact]
    public void BundledReleaseNotes_MatchTheBuiltVersion_SoTheyWorkOffline()
    {
        var notes = new EmbeddedReleaseNotesSource().LoadCurrent();
        var build = BuildIdentity.FromAssembly(typeof(App).Assembly, null);

        Assert.NotNull(notes);
        Assert.Equal(build.Version, notes.Version);
        Assert.False(notes.Document.IsEmpty);
        Assert.NotNull(notes.PublishedOn);
    }

    // ---------------------------------------------------------------- markup

    private static string Markup(string view) => File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml"));

    [Fact]
    public void Settings_HasApplicationUpdatesBeforeAbout_WithAccessibleButtonsAndLiveStatus()
    {
        var markup = Markup("PersonalSyncSettingsView");
        Assert.True(markup.IndexOf("Text=\"Diagnostics\" Classes=\"sectionTitle\"", StringComparison.Ordinal)
            < markup.IndexOf("Text=\"Application Updates\" Classes=\"sectionTitle\"", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("Text=\"Application Updates\" Classes=\"sectionTitle\"", StringComparison.Ordinal)
            < markup.IndexOf("Text=\"About this build\" Classes=\"sectionTitle\"", StringComparison.Ordinal));

        foreach (var name in new[] { "Check for Updates", "Install Update", "Remind Me Later", "Cancel Download", "Restart and Install", "Not now, install later", "What's New in this version", "Update download progress" })
        {
            Assert.Contains($"AutomationProperties.Name=\"{name}\"", markup);
        }

        foreach (var binding in new[] { "Updates.StatusTitle", "Updates.LastCheckedText", "Updates.InstalledVersionText", "Updates.ReleaseNotesBlocks", "Updates.DownloadPercent", "Updates.ReadinessText", "Updates.PreviousAttemptText", "Updates.ShowWhatsNewCommand" })
        {
            Assert.Contains(binding, markup);
        }

        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", markup[markup.IndexOf("ApplicationUpdatesSection", StringComparison.Ordinal)..]);
    }

    [Theory]
    [InlineData("WhatsNewView")]
    [InlineData("PersonalSyncSettingsView")]
    [InlineData("MainWindow")]
    public void NewMarkup_UsesThemeTokensOnly_NoHardCodedColors(string view)
    {
        var markup = Markup(view);
        Assert.Empty(Regex.Matches(markup, "(Background|Foreground|Fill|BorderBrush)=\"#"));
    }

    [Fact]
    public void WhatsNewOverlay_IsPlainText_KeyboardClosable_AndHasAnAccessibleContinue()
    {
        var markup = Markup("WhatsNewView");
        Assert.Contains("What's New in Carowinds Operations", markup);
        Assert.Contains("<KeyBinding Gesture=\"Escape\" Command=\"{Binding CloseCommand}\" />", markup);
        Assert.Contains("AutomationProperties.Name=\"Close What's New and continue\"", markup);
        Assert.Contains("<ScrollViewer", markup);
        Assert.DoesNotContain("WebView", markup);
        Assert.DoesNotContain("HyperlinkButton", markup);

        var shell = Markup("MainWindow");
        Assert.Contains("IsVisible=\"{Binding IsWhatsNewOpen}\"", shell);
        Assert.Contains("IsVisible=\"{Binding HasUpdateNotification}\"", shell);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding SettingsNavigationHelpText}\"", shell);
    }
}
