using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Application.MorningMessage.Sync;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 30: Sync Morning Messages in Settings → Personal Sync, and the composer's side of sync safety (it
/// holds its date while open, never has typed text replaced, and shows a notice). View-model/presentation level
/// plus markup-string checks — the mixed partition never asserts bound values from a live view.
/// </summary>
public class Phase30MorningMessageSyncUiTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);

    private static string Markup(string view) =>
        Regex.Replace(File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml")), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private sealed record Composer(
        AssociateViewModel Associate,
        MorningMessageEditorSession Session,
        InMemoryMorningMessagePreferenceStore Preferences,
        InMemoryMorningMessageContentStore Content)
    {
        public MorningMessageViewModel ViewModel => Associate.MorningMessage;
    }

    private static async Task<Composer> CreateComposerAsync(bool syncEnabled = true)
    {
        var service = new FakeAssociatePassdownService();
        var session = new MorningMessageEditorSession { SyncEnabled = syncEnabled };
        var preferences = new InMemoryMorningMessagePreferenceStore();
        var content = new InMemoryMorningMessageContentStore();
        var associate = new AssociateViewModel(
            service, new FakeClock(FixedNow), autosaveDelay: new ManualDebounceScheduler().Delay, clipboardService: new FakeClipboardService(),
            uiDispatcher: new ImmediateUiDispatcher(), morningMessagePreferenceStore: preferences, morningMessageContentStore: content,
            morningMessageSession: session);
        await associate.PendingLoad;
        return new Composer(associate, session, preferences, content);
    }

    // ----- Composer -----

    [Fact]
    public async Task Composer_HoldsItsDateWhileOpen_AndReleasesIt_AndAsksForASync_WhenClosed()
    {
        var c = await CreateComposerAsync();
        var requests = 0;
        c.Session.SyncRequested += (_, _) => requests++;

        await c.Associate.OpenMorningMessageCommand.ExecuteAsync(null);
        Assert.True(c.ViewModel.IsOpen);
        Assert.Equal(c.Associate.SelectedDate, c.Session.OpenDate);

        await c.ViewModel.CloseCommand.ExecuteAsync(null);
        Assert.Null(c.Session.OpenDate);
        Assert.False(c.Session.IsComposerOpen);
        Assert.True(requests >= 1);
    }

    [Fact]
    public async Task Composer_ChangesWaitingForItsDate_ShowANotice_AndNeverReplaceTypedText()
    {
        var c = await CreateComposerAsync();
        await c.Associate.OpenMorningMessageCommand.ExecuteAsync(null);
        var date = c.ViewModel.OperatingDate!.Value;
        c.ViewModel.Reminders = "Typing this right now";

        c.Session.PublishChange(new MorningMessageSyncChange([date.AddDays(1)], false, [], false));
        Assert.Null(c.ViewModel.SyncNotice); // another date's update is not this window's business

        c.Session.PublishChange(new MorningMessageSyncChange([], false, [date], false));
        Assert.Equal(MorningMessageViewModel.SyncWaitingNotice, c.ViewModel.SyncNotice);
        Assert.Equal("Typing this right now", c.ViewModel.Reminders);

        await c.ViewModel.CloseCommand.ExecuteAsync(null);
        Assert.Null(c.ViewModel.SyncNotice);
        Assert.Equal("Typing this right now", (await c.Content.LoadAsync(date)).Reminders);
    }

    [Fact]
    public async Task Composer_PreferencesWaiting_AlsoShowTheNotice()
    {
        var c = await CreateComposerAsync();
        await c.Associate.OpenMorningMessageCommand.ExecuteAsync(null);

        c.Session.PublishChange(new MorningMessageSyncChange([], false, [], PreferencesDeferred: true));

        Assert.Equal(MorningMessageViewModel.SyncWaitingNotice, c.ViewModel.SyncNotice);
    }

    [Fact]
    public async Task Composer_ReloadsPreferencesAndTextEachTimeItOpens_SoSynchronizedChangesAreShown()
    {
        var c = await CreateComposerAsync();
        await c.Associate.OpenMorningMessageCommand.ExecuteAsync(null);
        var date = c.ViewModel.OperatingDate!.Value;
        await c.ViewModel.CloseCommand.ExecuteAsync(null);

        // What a sync pass does while the composer is closed.
        await c.Preferences.SaveAsync(MorningMessagePreferences.Default with { Greeting = "Synced greeting", DefaultCopyMode = TeamsCopyMode.Formatted });
        await c.Content.SaveManyAsync(new Dictionary<DateOnly, MorningMessageContent> { [date] = MorningMessageContent.Empty with { BreakPlan = "Synced plan" } });
        c.Session.PublishChange(new MorningMessageSyncChange([date], true, [], false));

        Assert.Equal(TeamsCopyMode.Formatted, c.ViewModel.Preferences.DefaultCopyMode); // the Copy menu follows it too

        await c.Associate.OpenMorningMessageCommand.ExecuteAsync(null);
        Assert.Equal("Synced greeting", c.ViewModel.Greeting);
        Assert.Equal("Synced plan", c.ViewModel.BreakPlan);
        Assert.True(c.ViewModel.UseFormattedByDefault);
    }

    [Fact]
    public async Task Composer_Saves_AskForASync_OnlyWhenTheOptionIsOn()
    {
        var off = await CreateComposerAsync(syncEnabled: false);
        var offRequests = 0;
        off.Session.SyncRequested += (_, _) => offRequests++;
        await off.Associate.OpenMorningMessageCommand.ExecuteAsync(null);
        off.ViewModel.OdNotes = "OD";
        await off.ViewModel.FlushSaveAsync();
        await off.ViewModel.CloseCommand.ExecuteAsync(null);
        Assert.Equal(0, offRequests);

        var on = await CreateComposerAsync(syncEnabled: true);
        var onRequests = 0;
        on.Session.SyncRequested += (_, _) => onRequests++;
        await on.Associate.OpenMorningMessageCommand.ExecuteAsync(null);
        on.ViewModel.OdNotes = "OD";
        await on.ViewModel.FlushSaveAsync();
        Assert.Equal(1, onRequests);
    }

    [Fact]
    public async Task Composer_DateSwitching_MovesTheHoldToTheNewDate()
    {
        var c = await CreateComposerAsync();
        await c.Associate.OpenMorningMessageCommand.ExecuteAsync(null);
        var first = c.Session.OpenDate;
        await c.ViewModel.CloseCommand.ExecuteAsync(null);

        c.Associate.SelectedDate = c.Associate.SelectedDate.AddDays(1);
        await c.Associate.PendingLoad;
        await c.Associate.OpenMorningMessageCommand.ExecuteAsync(null);

        Assert.Equal(first!.Value.AddDays(1), c.Session.OpenDate);
    }

    [Fact]
    public async Task Composer_OdNotes_StayOutOfTheMainMessage_WithSyncedContent()
    {
        var c = await CreateComposerAsync();
        await c.Associate.OpenMorningMessageCommand.ExecuteAsync(null);
        var date = c.ViewModel.OperatingDate!.Value;
        await c.ViewModel.CloseCommand.ExecuteAsync(null);
        await c.Content.SaveManyAsync(new Dictionary<DateOnly, MorningMessageContent>
        {
            [date] = MorningMessageContent.Empty with { Reminders = "Synced reminder", OdNotes = "Synced OD-only note" },
        });

        await c.Associate.OpenMorningMessageCommand.ExecuteAsync(null);

        Assert.Contains("Synced reminder", c.ViewModel.PreviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("Synced OD-only note", c.ViewModel.PreviewText, StringComparison.Ordinal);
        Assert.Contains("Synced OD-only note", c.ViewModel.OdNotesPreviewText, StringComparison.Ordinal);
    }

    // ----- Settings: Sync Morning Messages -----

    private sealed class FakeMorningMessageSyncService : IMorningMessageSyncService
    {
        public bool Enabled { get; set; }

        public MorningMessageSyncStatus? StatusOverride { get; set; }

        public List<bool> SetEnabledCalls { get; } = [];

        public List<PersonalSyncStatus> OverviewRequests { get; } = [];

        public List<(DateOnly? Date, MorningMessageConflictResolution Resolution)> Resolutions { get; } = [];

        public List<MorningMessageConflict> Conflicts { get; } = [];

        public MorningMessageResolveOutcome NextOutcome { get; set; } = MorningMessageResolveOutcome.Resolved;

        public Task<MorningMessageSyncOverview> GetOverviewAsync(PersonalSyncStatus personalSyncStatus, CancellationToken cancellationToken = default)
        {
            OverviewRequests.Add(personalSyncStatus);
            var status = StatusOverride ?? (Enabled ? MorningMessageSyncStatus.Synced : MorningMessageSyncStatus.NotSyncing);
            return Task.FromResult(new MorningMessageSyncOverview(Enabled, status, Conflicts.Count, null));
        }

        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            SetEnabledCalls.Add(enabled);
            Enabled = enabled;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MorningMessageConflict>> GetConflictsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MorningMessageConflict>>(Conflicts.ToList());

        public Task<MorningMessageResolveOutcome> ResolveAsync(DateOnly? operatingDate, MorningMessageConflictResolution resolution, CancellationToken cancellationToken = default)
        {
            Resolutions.Add((operatingDate, resolution));
            if (NextOutcome == MorningMessageResolveOutcome.Resolved)
            {
                Conflicts.RemoveAll(conflict => conflict.OperatingDate == operatingDate);
            }

            return Task.FromResult(NextOutcome);
        }
    }

    private static MorningMessageConflict ReminderConflict(DateOnly? date = null) => new(
        date ?? Today,
        [new MorningMessageConflictField(MorningMessageSyncFields.Reminders, "Reminders", "Parking meeting at 9:00.", "Parking meeting at 9:30.")],
        FixedNow);

    [Fact]
    public async Task Settings_OptionIsOffByDefault_AndUnavailableUntilPersonalSyncHasAFolder()
    {
        var service = new FakeMorningMessageSyncService();
        var viewModel = new MorningMessageSyncSettingsViewModel(service);

        await viewModel.RefreshAsync(PersonalSyncStatus.Off, syncFolderConfigured: false);

        Assert.False(viewModel.IsEnabled);
        Assert.False(viewModel.IsAvailable);
        Assert.True(viewModel.ShowNeedsFolder);
        Assert.Equal("Morning Messages: Not syncing", viewModel.StatusText);
        Assert.Empty(service.SetEnabledCalls); // loading never saves

        await viewModel.RefreshAsync(PersonalSyncStatus.Ready, syncFolderConfigured: true);
        Assert.True(viewModel.IsAvailable);
        Assert.False(viewModel.ShowNeedsFolder);
    }

    [Fact]
    public async Task Settings_TurningOnAndOff_SavesThisComputersChoice_AndRequestsASyncWhenTurnedOn()
    {
        var service = new FakeMorningMessageSyncService();
        var coordinator = new FakePersonalSyncCoordinator();
        var viewModel = new MorningMessageSyncSettingsViewModel(service, coordinator);
        await viewModel.RefreshAsync(PersonalSyncStatus.Ready, true);

        viewModel.IsEnabled = true;
        await viewModel.PendingWork;
        Assert.Equal([true], service.SetEnabledCalls);
        Assert.Equal([SyncTriggerReason.LocalChange], coordinator.RequestedReasons);
        Assert.Equal("Morning Messages: Synced", viewModel.StatusText);

        viewModel.IsEnabled = false;
        await viewModel.PendingWork;
        Assert.Equal([true, false], service.SetEnabledCalls);
        Assert.Single(coordinator.RequestedReasons);
        Assert.Equal("Morning Messages: Not syncing", viewModel.StatusText);
    }

    [Theory]
    [InlineData(MorningMessageSyncStatus.NotSyncing, "Morning Messages: Not syncing", false)]
    [InlineData(MorningMessageSyncStatus.Synced, "Morning Messages: Synced", false)]
    [InlineData(MorningMessageSyncStatus.ChangesWaiting, "Morning Messages: Changes waiting to sync", false)]
    [InlineData(MorningMessageSyncStatus.ConflictNeedsReview, "Morning Messages: Conflict needs review", true)]
    [InlineData(MorningMessageSyncStatus.FolderUnavailable, "Morning Messages: Shared folder unavailable", true)]
    [InlineData(MorningMessageSyncStatus.NeedsSyncFolder, "Morning Messages: Not syncing — no sync folder", true)]
    public async Task Settings_StatusLine_SaysExactlyWhatIsTrue(MorningMessageSyncStatus status, string text, bool isProblem)
    {
        var viewModel = new MorningMessageSyncSettingsViewModel(new FakeMorningMessageSyncService { Enabled = true, StatusOverride = status });

        await viewModel.RefreshAsync(PersonalSyncStatus.Ready, true);

        Assert.Equal(text, viewModel.StatusText);
        Assert.Equal(isProblem, viewModel.StatusIsProblem);
    }

    [Theory]
    [InlineData(MorningMessageConflictResolution.KeepThisDevice)]
    [InlineData(MorningMessageConflictResolution.UseOtherDevice)]
    public async Task Settings_ConflictReview_ShowsBothVersions_AndPassesTheChoiceOn(MorningMessageConflictResolution choice)
    {
        var service = new FakeMorningMessageSyncService { Enabled = true };
        service.Conflicts.Add(ReminderConflict());
        var viewModel = new MorningMessageSyncSettingsViewModel(service);
        await viewModel.RefreshAsync(PersonalSyncStatus.Ready, true);

        Assert.True(viewModel.HasConflicts);
        var conflict = Assert.Single(viewModel.Conflicts);
        Assert.Equal("Morning message for Thursday, October 22, 2026", conflict.Title);
        Assert.False(viewModel.KeepThisDeviceCommand.CanExecute(null));

        viewModel.ReviewConflictCommand.Execute(conflict);
        var field = Assert.Single(viewModel.SelectedConflict!.Fields);
        Assert.Equal(("Reminders", "Parking meeting at 9:00.", "Parking meeting at 9:30."), (field.Label, field.ThisDevice, field.OtherDevice));

        var command = choice == MorningMessageConflictResolution.KeepThisDevice ? viewModel.KeepThisDeviceCommand : viewModel.UseOtherDeviceCommand;
        await command.ExecuteAsync(null);

        Assert.Equal([((DateOnly?)Today, choice)], service.Resolutions);
        Assert.Null(viewModel.SelectedConflict);
        Assert.False(viewModel.HasConflicts);
    }

    [Fact]
    public async Task Settings_ResolvingWhileTheComposerIsOpen_ExplainsWhy_AndKeepsTheConflict()
    {
        var service = new FakeMorningMessageSyncService { Enabled = true, NextOutcome = MorningMessageResolveOutcome.ComposerOpen };
        service.Conflicts.Add(ReminderConflict());
        var viewModel = new MorningMessageSyncSettingsViewModel(service);
        await viewModel.RefreshAsync(PersonalSyncStatus.Ready, true);
        viewModel.ReviewConflictCommand.Execute(viewModel.Conflicts[0]);

        await viewModel.UseOtherDeviceCommand.ExecuteAsync(null);

        Assert.Equal("Close the Morning Message window first, so nothing you are typing is replaced.", viewModel.Message);
        Assert.NotNull(viewModel.SelectedConflict);
        Assert.True(viewModel.HasConflicts);
    }

    [Fact]
    public async Task PersonalSyncSettings_RefreshesTheMorningMessageLine_FromItsFolderStatus()
    {
        var personal = new FakePersonalSyncSettingsService { PersonalSyncEnabled = true, SyncRootPath = "/Users/associate/OneDrive", Status = PersonalSyncStatus.Ready };
        var service = new FakeMorningMessageSyncService { Enabled = true };
        var morning = new MorningMessageSyncSettingsViewModel(service);
        var settings = new PersonalSyncSettingsViewModel(
            personal, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator(), morningMessages: morning);
        await settings.PendingLoad;
        await morning.PendingWork;

        Assert.True(settings.HasMorningMessageSync);
        Assert.Same(morning, settings.MorningMessages);
        Assert.Contains(PersonalSyncStatus.Ready, service.OverviewRequests);
        Assert.True(morning.IsAvailable);
        Assert.True(morning.IsEnabled);

        // Without the sub-view model (older composition) nothing changes for Personal Sync itself.
        var plain = new PersonalSyncSettingsViewModel(personal, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        await plain.PendingLoad;
        Assert.False(plain.HasMorningMessageSync);
    }

    // ----- Presentation -----

    [Fact]
    public void SyncResult_MentionsMorningMessages_OnlyWhenTheOptionRan()
    {
        var without = new PersonalSyncResult(PersonalSyncOutcome.Completed, 0, 0, 2, 0, 0, 0, null);
        Assert.Equal("Synced: 0 imported, 0 exported, 2 unchanged.", PersonalSyncResultPresentation.Format(without));

        var quiet = without with { MorningMessages = new MorningMessageSyncResult(true, 0, false, 0, 0, 0, 0, 0, false) };
        Assert.Equal("Synced: 0 imported, 0 exported, 2 unchanged. Morning messages up to date.", PersonalSyncResultPresentation.Format(quiet));

        var busy = without with { MorningMessages = new MorningMessageSyncResult(true, 3, true, 1, 2, 0, 0, 0, true) };
        Assert.Equal(
            "Synced: 0 imported, 0 exported, 2 unchanged. Morning messages: 3 morning messages updated from another computer; morning message preferences updated; 1 morning message conflict to review; morning message changes waiting until the composer is closed.",
            PersonalSyncResultPresentation.Format(busy));
    }

    [Theory]
    [InlineData(MorningMessageSyncFields.Reminders, "", "(empty)")]
    [InlineData(MorningMessageSyncFields.DapImageReminder, "true", "Yes")]
    [InlineData(MorningMessageSyncFields.RemindersAsBullets, "false", "No")]
    [InlineData(MorningMessageSyncFields.DefaultCopyMode, "PlainText", "Plain text")]
    [InlineData(MorningMessageSyncFields.Sections, "Greeting,-Operations,DailyFeature", "Greeting, Today's Operations (not included), Daily Feature")]
    [InlineData(MorningMessageSyncFields.BreakPlan, "Avery  10:30", "Avery  10:30")]
    public void ConflictValues_AreShownInPlainWords(string field, string value, string expected) =>
        Assert.Equal(expected, MorningMessageSyncPresentation.Display(field, value));

    // ----- Markup -----

    [Fact]
    public void SettingsMarkup_HasTheOptionalToggle_ItsWording_StatusAndConflictActions()
    {
        var markup = Markup("PersonalSyncSettingsView");

        Assert.Contains("x:Name=\"SyncMorningMessagesCheckBox\" Content=\"Sync Morning Messages\"", markup, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding MorningMessages.IsEnabled}\"", markup, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding MorningMessages.IsAvailable}\"", markup, StringComparison.Ordinal);
        Assert.Contains("{x:Static vm:MorningMessageSyncSettingsViewModel.Description}", markup, StringComparison.Ordinal);
        Assert.Contains("{x:Static vm:MorningMessageSyncSettingsViewModel.DisableExplanation}", markup, StringComparison.Ordinal);
        Assert.Contains("{x:Static vm:MorningMessageSyncSettingsViewModel.NeedsFolderText}", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding MorningMessages.StatusText}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MorningMessages.KeepThisDeviceCommand}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MorningMessages.UseOtherDeviceCommand}\"", markup, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Sync Morning Messages\"", markup, StringComparison.Ordinal);

        Assert.Equal(
            "Synchronize morning-message drafts, reminders, break plans, OD Notes, and layout preferences across your computers.",
            MorningMessageSyncSettingsViewModel.Description);
        Assert.Equal(
            "Turning this off stops future synchronization. Messages already saved on this computer will remain available.",
            MorningMessageSyncSettingsViewModel.DisableExplanation);

        // The toggle sits inside the Personal Sync panel, before the draft/note conflict callout.
        Assert.True(markup.IndexOf("x:Name=\"PersonalSyncSection\"", StringComparison.Ordinal) < markup.IndexOf("x:Name=\"MorningMessageSyncSection\"", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("x:Name=\"MorningMessageSyncSection\"", StringComparison.Ordinal) < markup.IndexOf("x:Name=\"ConflictCallout\"", StringComparison.Ordinal));
    }

    [Fact]
    public void ComposerMarkup_ShowsTheWaitingNotice_Politely()
    {
        var markup = Markup("MorningMessageView");

        Assert.Contains("x:Name=\"SyncNoticeText\" Text=\"{Binding SyncNotice}\"", markup, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding SyncNotice, Converter={x:Static StringConverters.IsNotNullOrEmpty}}\"", markup, StringComparison.Ordinal);
    }
}
