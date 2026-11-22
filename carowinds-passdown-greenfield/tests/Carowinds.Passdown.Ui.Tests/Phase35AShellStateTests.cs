using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Infrastructure.Accessibility;
using Carowinds.Passdown.Infrastructure.Preferences;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35A: the shell's view-model state — sidebar collapse/expand and its local persistence, effective motion,
/// window activation — never changes the section, any page's state, the selected date, or starts a refresh.
/// No window is created, so the class runs in the mixed Ui partition.
/// </summary>
public class Phase35AShellStateTests
{
    private static readonly DateOnly Today = new(2026, 8, 26);

    internal sealed class FakeSidebarStore : INavigationSidebarPreferenceStore
    {
        public NavigationSidebarState Stored { get; set; }

        public List<NavigationSidebarState> Saves { get; } = [];

        public int Loads { get; private set; }

        public bool FailSaves { get; set; }

        public NavigationSidebarState Load()
        {
            Loads++;
            return Stored;
        }

        public Task SaveAsync(NavigationSidebarState state, CancellationToken cancellationToken = default)
        {
            if (FailSaves)
            {
                throw new IOException("read-only profile");
            }

            Saves.Add(state);
            Stored = state;
            return Task.CompletedTask;
        }
    }

    private sealed record Shell(
        MainViewModel Main,
        FakeOperationsDashboardService Dashboard,
        FakeAssociatePassdownService Associate,
        FakePassdownHistoryService History,
        FakeOperationalNotesService Notes);

    private static Shell Create(FakeSidebarStore? sidebar = null, InterfaceMotionController? motion = null)
    {
        var dashboardService = new FakeOperationsDashboardService();
        var clock = new FakeClock(new DateTimeOffset(Today.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        var associateService = new FakeAssociatePassdownService();
        var notesService = new FakeOperationalNotesService();
        var historyService = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(Today.AddDays(-1), DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator(),
            appearance: new AppearanceSettingsViewModel(motion: motion));
        var main = new MainViewModel(
            new OperationsDashboardViewModel(dashboardService),
            new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock),
            new AssociateViewModel(associateService, clock),
            new OperationalNotesViewModel(notesService, new SystemClock()),
            new PassdownHistoryViewModel(historyService, new FakeFinalizedPassdownPdfRetrievalService()),
            settings,
            priceHistory: null,
            whatsNew: null,
            sidebarStore: sidebar);
        return new Shell(main, dashboardService, associateService, historyService, notesService);
    }

    // ---------------------------------------------------------------- sidebar collapse / expand

    [Fact]
    public void FreshProfile_StartsExpanded()
    {
        var shell = Create(new FakeSidebarStore());

        Assert.False(shell.Main.IsNavigationCollapsed);
        Assert.True(shell.Main.IsNavigationExpanded);
        Assert.Equal("Collapse navigation", shell.Main.NavigationToggleName);
    }

    [Fact]
    public void SavedCollapsed_StartsCollapsed()
    {
        var shell = Create(new FakeSidebarStore { Stored = NavigationSidebarState.Collapsed });

        Assert.True(shell.Main.IsNavigationCollapsed);
        Assert.Equal("Expand navigation", shell.Main.NavigationToggleName);
    }

    [Fact]
    public async Task Toggle_FlipsImmediately_AndPersistsEachStateInOrder()
    {
        var store = new FakeSidebarStore();
        var shell = Create(store);

        shell.Main.ToggleNavigationCommand.Execute(null);
        Assert.True(shell.Main.IsNavigationCollapsed);
        shell.Main.ToggleNavigationCommand.Execute(null);
        shell.Main.ToggleNavigationCommand.Execute(null);
        await shell.Main.PendingNavigationSave;

        Assert.True(shell.Main.IsNavigationCollapsed);
        Assert.Equal([NavigationSidebarState.Collapsed, NavigationSidebarState.Expanded, NavigationSidebarState.Collapsed], store.Saves);
        Assert.Equal(NavigationSidebarState.Collapsed, store.Stored);
    }

    [Fact]
    public async Task Toggle_SaveFailure_KeepsTheSessionState_NeverThrows()
    {
        var store = new FakeSidebarStore { FailSaves = true };
        var shell = Create(store);

        shell.Main.ToggleNavigationCommand.Execute(null);
        await shell.Main.PendingNavigationSave;

        Assert.True(shell.Main.IsNavigationCollapsed);
    }

    [Fact]
    public void Toggle_WithoutAStore_IsInMemoryOnly()
    {
        var shell = Create();

        shell.Main.ToggleNavigationCommand.Execute(null);

        Assert.True(shell.Main.IsNavigationCollapsed);
    }

    [Theory]
    [InlineData(AppSection.Dashboard)]
    [InlineData(AppSection.OperationsCalendar)]
    [InlineData(AppSection.AssociatePassdown)]
    [InlineData(AppSection.OperationalNotes)]
    [InlineData(AppSection.History)]
    [InlineData(AppSection.Settings)]
    public void Collapsing_NeverChangesTheSection_TheDate_OrStartsARefresh(AppSection section)
    {
        var shell = Create(new FakeSidebarStore());
        shell.Main.SelectedSection = section;
        var date = shell.Main.Associate.SelectedDate;
        var associateSection = shell.Main.Associate.SelectedSection;
        var loads = shell.Associate.LoadCalls.Count;
        var dashboardLoads = shell.Dashboard.GetTodayCallCount;
        var historyLoads = shell.History.ListCallCount;
        var notesLoads = shell.Notes.ListCallCount;

        for (var i = 0; i < 10; i++)
        {
            shell.Main.ToggleNavigationCommand.Execute(null);
        }

        Assert.Equal(section, shell.Main.SelectedSection);
        Assert.Equal(date, shell.Main.Associate.SelectedDate);
        Assert.Equal(associateSection, shell.Main.Associate.SelectedSection);
        Assert.Equal(loads, shell.Associate.LoadCalls.Count);
        Assert.Empty(shell.Associate.CheckOnlineCalls);
        Assert.Equal(0, shell.Dashboard.CheckOnlineCallCount);
        Assert.Equal(0, shell.Dashboard.DeepRefreshCallCount);
        Assert.Equal(dashboardLoads, shell.Dashboard.GetTodayCallCount);
        Assert.Equal(historyLoads, shell.History.ListCallCount);
        Assert.Equal(notesLoads, shell.Notes.ListCallCount);
    }

    [Fact]
    public void Collapsing_KeepsEveryChildViewModelInstance()
    {
        var shell = Create(new FakeSidebarStore());
        var before = (shell.Main.Dashboard, shell.Main.Calendar, shell.Main.Associate, shell.Main.Notes, shell.Main.History, shell.Main.Settings);

        shell.Main.ToggleNavigationCommand.Execute(null);
        shell.Main.ToggleNavigationCommand.Execute(null);

        Assert.Equal(before, (shell.Main.Dashboard, shell.Main.Calendar, shell.Main.Associate, shell.Main.Notes, shell.Main.History, shell.Main.Settings));
    }

    [Fact]
    public void Toggle_StaysAvailable_WhileAnOperationalNoteIsEdited_AndTheGuardStillHolds()
    {
        var shell = Create(new FakeSidebarStore());
        shell.Main.ShowOperationalNotesSectionCommand.Execute(null);
        shell.Main.Notes.NewNoteCommand.Execute(null);
        Assert.True(shell.Main.Notes.IsEditing);

        Assert.True(shell.Main.ToggleNavigationCommand.CanExecute(null));
        shell.Main.ToggleNavigationCommand.Execute(null);

        Assert.False(shell.Main.ShowDashboardSectionCommand.CanExecute(null));
        Assert.False(shell.Main.ShowSettingsSectionCommand.CanExecute(null));
        Assert.Equal(AppSection.OperationalNotes, shell.Main.SelectedSection);
    }

    [Fact]
    public void RapidNavigation_EndsOnTheLastSection_WithOneSelectionFlag()
    {
        var shell = Create(new FakeSidebarStore());
        var commands = new[]
        {
            shell.Main.ShowDashboardSectionCommand, shell.Main.ShowOperationsCalendarSectionCommand, shell.Main.ShowAssociateSectionCommand,
            shell.Main.ShowOperationalNotesSectionCommand, shell.Main.ShowHistorySectionCommand, shell.Main.ShowSettingsSectionCommand,
        };

        for (var i = 0; i < 120; i++)
        {
            commands[i % commands.Length].Execute(null);
            if (i % 7 == 0)
            {
                shell.Main.ToggleNavigationCommand.Execute(null);
            }
        }

        Assert.Equal(AppSection.Settings, shell.Main.SelectedSection); // the 120th command is index 119 % 6 == 5 (Settings)
        var flags = new[]
        {
            shell.Main.IsDashboardSectionSelected, shell.Main.IsOperationsCalendarSectionSelected, shell.Main.IsAssociateSectionSelected,
            shell.Main.IsOperationalNotesSectionSelected, shell.Main.IsHistorySectionSelected, shell.Main.IsSettingsSectionSelected,
        };
        Assert.Single(flags, f => f);
    }

    // ---------------------------------------------------------------- motion

    [Fact]
    public void Motion_FollowsTheSettingsChoice_AndRaisesChangesForTheWindowClasses()
    {
        var (controller, _, _) = Phase35AInterfaceMotionTests.Create();
        var shell = Create(motion: controller);
        var changed = new List<string?>();
        shell.Main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(shell.Main.IsInterfaceMotionEnabled);
        Assert.False(shell.Main.IsInterfaceMotionDisabled);

        shell.Main.Settings.Appearance.IsAnimationsOffSelected = true;

        Assert.False(shell.Main.IsInterfaceMotionEnabled);
        Assert.True(shell.Main.IsInterfaceMotionDisabled);
        Assert.Contains(nameof(MainViewModel.IsInterfaceMotionEnabled), changed);
        Assert.Contains(nameof(MainViewModel.IsInterfaceMotionDisabled), changed);
    }

    [Fact]
    public void WindowActivation_RereadsReducedMotion_AndChangesNothingElse()
    {
        var (controller, _, os) = Phase35AInterfaceMotionTests.Create();
        var shell = Create(new FakeSidebarStore(), controller);
        shell.Main.ShowAssociateSectionCommand.Execute(null);
        var loads = shell.Associate.LoadCalls.Count;

        os.Value = SystemReducedMotion.Requested;
        shell.Main.OnWindowActivated();

        Assert.False(shell.Main.IsInterfaceMotionEnabled);
        Assert.Equal(AppSection.AssociatePassdown, shell.Main.SelectedSection);
        Assert.Equal(loads, shell.Associate.LoadCalls.Count);
        Assert.Empty(shell.Associate.CheckOnlineCalls);

        os.Value = SystemReducedMotion.NotRequested;
        shell.Main.OnWindowActivated();
        Assert.True(shell.Main.IsInterfaceMotionEnabled);
    }

    [Fact]
    public void DefaultTestComposition_IsMotionFree()
    {
        Assert.False(Create().Main.IsInterfaceMotionEnabled);
    }

    // ---------------------------------------------------------------- composition root

    [Fact]
    public void CompositionRoot_RegistersTheLocalShellPreferences_WithoutResolvingAnyFile()
    {
        var provider = App.BuildServiceProvider();

        var sidebar = Assert.IsType<FileNavigationSidebarPreferenceStore>(provider.GetRequiredService<INavigationSidebarPreferenceStore>());
        Assert.Equal(FileNavigationSidebarPreferenceStore.DefaultPath, sidebar.ConfigFilePath);
        var motion = Assert.IsType<FileInterfaceAnimationPreferenceStore>(provider.GetRequiredService<IInterfaceAnimationPreferenceStore>());
        Assert.Equal(FileInterfaceAnimationPreferenceStore.DefaultPath, motion.ConfigFilePath);
        Assert.IsType<WindowsAnimationEffectsReader>(provider.GetRequiredService<ISystemReducedMotionReader>());
        var controller = provider.GetRequiredService<InterfaceMotionController>();
        Assert.Same(controller, provider.GetRequiredService<InterfaceMotionController>());
    }

    [Fact]
    public void MainViewModel_TheConstructorDiChooses_TakesTheSidebarStore()
    {
        // MS DI picks the longest constructor it can satisfy; it must be the one carrying the sidebar store.
        var longest = typeof(MainViewModel).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        Assert.Contains(longest.GetParameters(), p => p.ParameterType == typeof(INavigationSidebarPreferenceStore));
    }
}
