using System.ComponentModel;
using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Infrastructure.Preferences;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 23C: the System / Light / Dark appearance preference — controller rules, the Settings view
/// model, the selector markup, startup ordering and isolation from every other kind of data. Nothing here
/// sets <c>Application.RequestedThemeVariant</c> (the controller writes to a <see cref="RecordingThemeVariantTarget"/>),
/// so the class runs in the mixed Ui partition; live switching on real windows is covered in
/// <see cref="Phase15ERealWindowLayoutTests"/>.
/// </summary>
public class Phase23CAppearancePreferenceTests
{
    private static (AppearanceThemeController Controller, FakeAppearancePreferenceStore Store, FakeSystemAppearanceSource Os, RecordingThemeVariantTarget Target) Create(
        AppearancePreference stored = AppearancePreference.System, EffectiveAppearance os = EffectiveAppearance.Light)
    {
        var store = new FakeAppearancePreferenceStore { Stored = stored };
        var source = new FakeSystemAppearanceSource(os);
        var target = new RecordingThemeVariantTarget();
        return (new AppearanceThemeController(store, source, target), store, source, target);
    }

    private static string Markup(string relativePath) => File.ReadAllText(MarkupFiles.Find(relativePath));

    private static string AppearanceSectionMarkup()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml");
        return markup[markup.IndexOf("x:Name=\"AppearanceSection\"", StringComparison.Ordinal)..markup.IndexOf("x:Name=\"DataRefreshSection\"", StringComparison.Ordinal)];
    }

    // ---------------------------------------------------------------- resolution rules

    [Theory]
    [InlineData(AppearancePreference.System, EffectiveAppearance.Light, EffectiveAppearance.Light)]
    [InlineData(AppearancePreference.System, EffectiveAppearance.Dark, EffectiveAppearance.Dark)]
    [InlineData(AppearancePreference.Light, EffectiveAppearance.Dark, EffectiveAppearance.Light)]
    [InlineData(AppearancePreference.Light, EffectiveAppearance.Light, EffectiveAppearance.Light)]
    [InlineData(AppearancePreference.Dark, EffectiveAppearance.Light, EffectiveAppearance.Dark)]
    [InlineData(AppearancePreference.Dark, EffectiveAppearance.Dark, EffectiveAppearance.Dark)]
    public void Initialize_AppliesTheSavedPreference_AgainstTheOperatingSystem(AppearancePreference stored, EffectiveAppearance os, EffectiveAppearance expected)
    {
        var (controller, store, _, target) = Create(stored, os);

        controller.Initialize();

        Assert.Equal(stored, controller.Preference);
        Assert.Equal(expected, controller.EffectiveAppearance);
        Assert.Equal([expected], target.Applied); // applied once, synchronously, during Initialize
        Assert.Equal(1, store.LoadCount);
        Assert.Empty(store.Saves); // loading never rewrites the file
    }

    [Fact]
    public void Default_IsSystem_ForAFirstLaunchOrAnRc7Upgrade()
    {
        var (controller, _, _, target) = Create(os: EffectiveAppearance.Dark);
        Assert.Equal(AppearancePreference.System, controller.Preference); // before loading

        controller.Initialize();

        Assert.Equal(AppearancePreference.System, controller.Preference);
        Assert.Equal(EffectiveAppearance.Dark, target.Last); // RC.7 behavior: follows the OS
        Assert.Equal(AppearancePreference.System, default(AppearancePreference));
    }

    [Fact]
    public void Initialize_RunsOnce_AndSubscribesToTheOperatingSystemOnce()
    {
        var (controller, store, os, target) = Create();

        controller.Initialize();
        controller.Initialize();

        Assert.Equal(1, store.LoadCount);
        Assert.Single(target.Applied);
        Assert.Equal(1, os.SubscriberCount);
    }

    [Fact]
    public void UnrecognizedStoredValue_FallsBackToSystem()
    {
        var (controller, _, _, target) = Create((AppearancePreference)42, EffectiveAppearance.Dark);

        controller.Initialize();

        Assert.Equal(AppearancePreference.System, controller.Preference);
        Assert.Equal(EffectiveAppearance.Dark, target.Last);
    }

    [Theory]
    [InlineData(AppearancePreference.System, AppearancePreference.System)]
    [InlineData(AppearancePreference.Light, AppearancePreference.Light)]
    [InlineData(AppearancePreference.Dark, AppearancePreference.Dark)]
    [InlineData((AppearancePreference)(-1), AppearancePreference.System)]
    [InlineData((AppearancePreference)3, AppearancePreference.System)]
    public void Sanitize_KeepsOnlyDefinedOptions(AppearancePreference value, AppearancePreference expected) =>
        Assert.Equal(expected, AppearanceResolver.Sanitize(value));

    // ---------------------------------------------------------------- operating-system changes

    [Fact]
    public void SystemMode_FollowsOperatingSystemChanges_WhileTheAppIsOpen()
    {
        var (controller, _, os, target) = Create(os: EffectiveAppearance.Light);
        controller.Initialize();

        os.Switch(EffectiveAppearance.Dark);
        Assert.Equal(EffectiveAppearance.Dark, controller.EffectiveAppearance);
        os.Switch(EffectiveAppearance.Light);

        Assert.Equal([EffectiveAppearance.Light, EffectiveAppearance.Dark, EffectiveAppearance.Light], target.Applied);
    }

    [Theory]
    [InlineData(AppearancePreference.Light, EffectiveAppearance.Light)]
    [InlineData(AppearancePreference.Dark, EffectiveAppearance.Dark)]
    public void ForcedMode_IgnoresOperatingSystemChanges(AppearancePreference forced, EffectiveAppearance shown)
    {
        var (controller, _, os, target) = Create(forced, EffectiveAppearance.Light);
        controller.Initialize();

        os.Switch(EffectiveAppearance.Dark);
        os.Switch(EffectiveAppearance.Light);
        os.Switch(EffectiveAppearance.Dark);

        Assert.Equal([shown], target.Applied);
        Assert.Equal(shown, controller.EffectiveAppearance);
    }

    [Fact]
    public async Task ReturningToSystem_ImmediatelyShowsTheCurrentOperatingSystemAppearance()
    {
        var (controller, _, os, target) = Create(AppearancePreference.Light, EffectiveAppearance.Light);
        controller.Initialize();
        os.Switch(EffectiveAppearance.Dark); // ignored while forced Light

        await controller.SetPreferenceAsync(AppearancePreference.System);

        Assert.Equal(EffectiveAppearance.Dark, target.Last);
        os.Switch(EffectiveAppearance.Light); // and System keeps following afterwards
        Assert.Equal(EffectiveAppearance.Light, target.Last);
    }

    // ---------------------------------------------------------------- transitions

    [Fact]
    public async Task Transitions_SystemDarkLightSystem_ApplyImmediately_AndPersistEach()
    {
        var (controller, store, _, target) = Create(os: EffectiveAppearance.Light);
        controller.Initialize();

        Assert.True(await controller.SetPreferenceAsync(AppearancePreference.Dark));
        Assert.Equal(EffectiveAppearance.Dark, target.Last);
        Assert.True(await controller.SetPreferenceAsync(AppearancePreference.Light));
        Assert.Equal(EffectiveAppearance.Light, target.Last);
        Assert.True(await controller.SetPreferenceAsync(AppearancePreference.System));
        Assert.Equal(EffectiveAppearance.Light, target.Last);

        Assert.Equal([AppearancePreference.Dark, AppearancePreference.Light, AppearancePreference.System], store.Saves);
    }

    [Fact]
    public async Task Transitions_SystemLightDarkSystem_UnderADarkOperatingSystem()
    {
        var (controller, store, _, target) = Create(os: EffectiveAppearance.Dark);
        controller.Initialize();

        await controller.SetPreferenceAsync(AppearancePreference.Light);
        await controller.SetPreferenceAsync(AppearancePreference.Dark);
        await controller.SetPreferenceAsync(AppearancePreference.System);

        Assert.Equal([EffectiveAppearance.Dark, EffectiveAppearance.Light, EffectiveAppearance.Dark, EffectiveAppearance.Dark], target.Applied);
        Assert.Equal(AppearancePreference.System, store.Stored);
    }

    [Fact]
    public async Task SelectingTheCurrentOption_DoesNotSaveOrReapply()
    {
        var (controller, store, _, target) = Create(AppearancePreference.Dark);
        controller.Initialize();

        Assert.True(await controller.SetPreferenceAsync(AppearancePreference.Dark));

        Assert.Empty(store.Saves);
        Assert.Single(target.Applied);
    }

    [Fact]
    public async Task RapidChanges_ApplyEachAtOnce_AndTheFileEndsOnTheLastChoice()
    {
        var (controller, store, _, target) = Create(os: EffectiveAppearance.Light);
        controller.Initialize();
        store.SaveGate = new TaskCompletionSource<bool>();

        var saves = new[]
        {
            controller.SetPreferenceAsync(AppearancePreference.Dark),
            controller.SetPreferenceAsync(AppearancePreference.Light),
            controller.SetPreferenceAsync(AppearancePreference.Dark),
            controller.SetPreferenceAsync(AppearancePreference.System),
            controller.SetPreferenceAsync(AppearancePreference.Light),
        };

        // Every pick is visible before any write finishes.
        Assert.Equal(
            [EffectiveAppearance.Light, EffectiveAppearance.Dark, EffectiveAppearance.Light, EffectiveAppearance.Dark, EffectiveAppearance.Light, EffectiveAppearance.Light],
            target.Applied);

        store.SaveGate.SetResult(true);
        Assert.All(await Task.WhenAll(saves), Assert.True);

        Assert.Equal(AppearancePreference.Light, store.Stored);
        Assert.Equal(AppearancePreference.Light, store.Saves[^1]);
        Assert.Equal(AppearancePreference.Light, controller.Preference);
    }

    [Fact]
    public async Task SaveFailure_KeepsTheChoiceForThisSession_AndReportsIt()
    {
        var (controller, store, _, target) = Create();
        controller.Initialize();
        store.SaveException = new IOException("synthetic read-only disk");

        var saved = await controller.SetPreferenceAsync(AppearancePreference.Dark);

        Assert.False(saved);
        Assert.Equal(AppearancePreference.Dark, controller.Preference);
        Assert.Equal(EffectiveAppearance.Dark, target.Last);
    }

    [Fact]
    public async Task IndependentProfiles_KeepTheirOwnChoice()
    {
        var (first, firstStore, _, _) = Create();
        var (second, secondStore, _, _) = Create();
        var (third, thirdStore, _, _) = Create();
        first.Initialize(); second.Initialize(); third.Initialize();

        await first.SetPreferenceAsync(AppearancePreference.Dark);
        await second.SetPreferenceAsync(AppearancePreference.Light);

        Assert.Equal(AppearancePreference.Dark, firstStore.Stored);
        Assert.Equal(AppearancePreference.Light, secondStore.Stored);
        Assert.Equal(AppearancePreference.System, thirdStore.Stored);
        Assert.Empty(thirdStore.Saves);
    }

    [Fact]
    public async Task Restart_LoadsTheLastSavedChoice()
    {
        var store = new FakeAppearancePreferenceStore();
        var before = new AppearanceThemeController(store, new FakeSystemAppearanceSource(), new RecordingThemeVariantTarget());
        before.Initialize();
        await before.SetPreferenceAsync(AppearancePreference.Dark);
        before.Dispose();

        var target = new RecordingThemeVariantTarget();
        var after = new AppearanceThemeController(store, new FakeSystemAppearanceSource(EffectiveAppearance.Light), target);
        after.Initialize();

        Assert.Equal(AppearancePreference.Dark, after.Preference);
        Assert.Equal([EffectiveAppearance.Dark], target.Applied); // the first theme drawn is the saved one
    }

    [Fact]
    public void Dispose_StopsFollowingTheOperatingSystem()
    {
        var (controller, _, os, _) = Create();
        controller.Initialize();

        controller.Dispose();

        Assert.Equal(0, os.SubscriberCount);
    }

    // ---------------------------------------------------------------- isolation from other data

    [Fact]
    public void Controller_DependsOnlyOnTheAppearanceStore_TheOsSource_AndTheThemeTarget()
    {
        // No Personal Sync, database, draft, refresh or official-data dependency exists to call.
        var parameters = typeof(AppearanceThemeController).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType.Name).ToList();
        Assert.Equal(["IAppearancePreferenceStore", "ISystemAppearanceSource", "IThemeVariantTarget", "ILogger`1"], parameters);

        var viewModelParameters = typeof(AppearanceSettingsViewModel).GetConstructors().Single().GetParameters();
        Assert.Equal([typeof(AppearanceThemeController)], viewModelParameters.Select(p => p.ParameterType));
    }

    [Fact]
    public async Task ChangingAppearanceInSettings_TouchesNoSyncSettingsOrSyncPass()
    {
        var settingsService = new FakePersonalSyncSettingsService();
        var coordinator = new FakePersonalSyncCoordinator();
        var (controller, store, _, _) = Create();
        controller.Initialize();
        var settings = new PersonalSyncSettingsViewModel(settingsService, new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), coordinator,
            appearance: new AppearanceSettingsViewModel(controller));
        await settings.PendingLoad;
        var loads = settingsService.LoadCallCount;

        settings.Appearance.IsDarkSelected = true;
        await settings.Appearance.PendingSave;
        settings.Appearance.IsSystemSelected = true;
        await settings.Appearance.PendingSave;

        Assert.Equal([AppearancePreference.Dark, AppearancePreference.System], store.Saves);
        Assert.Equal(loads, settingsService.LoadCallCount);
        Assert.Equal(0, settingsService.SetEnabledCallCount);
        Assert.Equal(0, settingsService.SetSyncRootCallCount);
        Assert.Equal(0, coordinator.SyncNowCallCount);
        Assert.Equal(0, coordinator.StartCallCount);
    }

    [Fact]
    public void PersonalSync_NeverReferencesTheAppearancePreference()
    {
        var root = Path.GetDirectoryName(MarkupFiles.Find("src/Carowinds.Passdown.Infrastructure/Carowinds.Passdown.Infrastructure.csproj"))!;
        var appRoot = Path.GetDirectoryName(MarkupFiles.Find("src/Carowinds.Passdown.Application/Carowinds.Passdown.Application.csproj"))!;
        var syncSources = Directory.GetFiles(Path.Combine(root, "PersonalSync"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(appRoot, "PersonalSync"), "*.cs", SearchOption.AllDirectories));

        foreach (var file in syncSources)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Appearance", text);
            Assert.DoesNotContain("appearance.json", text);
        }
    }

    [Fact]
    public void AppearanceStore_UsesNoDatabase_AndNoMigrationWasAdded()
    {
        var store = Markup("src/Carowinds.Passdown.Infrastructure/Preferences/FileAppearancePreferenceStore.cs");
        Assert.DoesNotContain("Sqlite", store);
        Assert.DoesNotContain("Migration", store);
        var app = Markup("src/Carowinds.Passdown.Desktop/App.axaml.cs");
        Assert.Contains("new Migration0016CreateSeasonPriceHistoryTable(),", app);
        Assert.DoesNotContain("Migration0017", app);
    }

    // ---------------------------------------------------------------- composition root and startup

    [Fact]
    public void CompositionRoot_RegistersOneLocalAppearanceController()
    {
        var provider = App.BuildServiceProvider();

        var controller = provider.GetRequiredService<AppearanceThemeController>();
        Assert.Same(controller, provider.GetRequiredService<AppearanceThemeController>());
        var store = Assert.IsType<FileAppearancePreferenceStore>(provider.GetRequiredService<IAppearancePreferenceStore>());
        Assert.Equal(FileAppearancePreferenceStore.DefaultPath, store.ConfigFilePath);
        Assert.Equal("appearance.json", Path.GetFileName(store.ConfigFilePath));
        Assert.IsType<ApplicationThemeVariantTarget>(provider.GetRequiredService<IThemeVariantTarget>());
        Assert.IsType<AvaloniaSystemAppearanceSource>(provider.GetRequiredService<ISystemAppearanceSource>());
        Assert.NotNull(provider.GetRequiredService<AppearanceSettingsViewModel>());
        // Resolving does not load or apply anything; only interactive startup calls Initialize.
        Assert.Equal(AppearancePreference.System, controller.Preference);
    }

    [Fact]
    public void Startup_AppliesTheSavedAppearance_BeforeMigrationsAndBeforeTheMainWindowIsCreated()
    {
        var app = Markup("src/Carowinds.Passdown.Desktop/App.axaml.cs");
        var startup = app[app.IndexOf("public override void OnFrameworkInitializationCompleted()", StringComparison.Ordinal)..];

        var apply = startup.IndexOf("ApplySavedAppearance(services, logger);", StringComparison.Ordinal);
        var migrations = startup.IndexOf("GetRequiredService<MigrationRunner>()", StringComparison.Ordinal);
        var window = startup.IndexOf("GetRequiredService<MainWindow>()", StringComparison.Ordinal);
        var assigned = startup.IndexOf("desktop.MainWindow = mainWindow;", StringComparison.Ordinal);
        var refresh = startup.IndexOf("refreshCoordinator.Start();", StringComparison.Ordinal);

        Assert.True(apply > 0);
        Assert.True(apply < migrations, "theme is applied before database work");
        Assert.True(apply < window && apply < assigned, "theme is applied before any window exists");
        Assert.True(apply < refresh);

        // The scheduled-refresh host shares BuildServiceProvider but never initializes appearance.
        var compositionRoot = app[app.IndexOf("internal static IServiceProvider BuildServiceProvider()", StringComparison.Ordinal)..];
        Assert.DoesNotContain(".Initialize()", compositionRoot);
    }

    [Fact]
    public void AppXaml_StillFollowsTheSystem_UntilThePreferenceIsApplied()
    {
        // Before the controller runs (and for any host that never runs it) the RC.7 default stands.
        Assert.Contains("RequestedThemeVariant=\"Default\"", Markup("src/Carowinds.Passdown.Desktop/App.axaml"));
    }

    // ---------------------------------------------------------------- Settings view model

    [Theory]
    [InlineData(AppearancePreference.System)]
    [InlineData(AppearancePreference.Light)]
    [InlineData(AppearancePreference.Dark)]
    public void ViewModel_ReflectsTheLoadedChoice_WithExactlyOneOptionSelected(AppearancePreference stored)
    {
        var (controller, _, _, _) = Create(stored);
        controller.Initialize();

        var viewModel = new AppearanceSettingsViewModel(controller);

        Assert.Equal(1, new[] { viewModel.IsSystemSelected, viewModel.IsLightSelected, viewModel.IsDarkSelected }.Count(selected => selected));
        Assert.Equal(stored, viewModel.SelectedPreference);
    }

    [Fact]
    public async Task ViewModel_CheckingAnOption_AppliesAndSaves_AndKeepsExactlyOneSelected()
    {
        var (controller, store, _, target) = Create(os: EffectiveAppearance.Light);
        controller.Initialize();
        var viewModel = new AppearanceSettingsViewModel(controller);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.IsDarkSelected = true;
        await viewModel.PendingSave;

        Assert.Equal(EffectiveAppearance.Dark, target.Last);
        Assert.Equal([AppearancePreference.Dark], store.Saves);
        Assert.True(viewModel.IsDarkSelected);
        Assert.False(viewModel.IsSystemSelected);
        Assert.False(viewModel.IsLightSelected);
        Assert.Contains(nameof(AppearanceSettingsViewModel.IsDarkSelected), changed);
        Assert.Contains(nameof(AppearanceSettingsViewModel.IsSystemSelected), changed);
    }

    [Fact]
    public async Task ViewModel_TheRadioGroupUncheckingTheOldOption_DoesNothing()
    {
        var (controller, store, _, target) = Create(AppearancePreference.Light);
        controller.Initialize();
        var viewModel = new AppearanceSettingsViewModel(controller);

        viewModel.IsLightSelected = false; // what a radio group does to the previous option
        viewModel.IsSystemSelected = false;
        await viewModel.PendingSave;

        Assert.Equal(AppearancePreference.Light, controller.Preference);
        Assert.Empty(store.Saves);
        Assert.Single(target.Applied);
    }

    [Fact]
    public async Task ViewModel_ReturningToSettings_ShowsTheCurrentChoice()
    {
        var (controller, _, _, _) = Create();
        controller.Initialize();
        var first = new AppearanceSettingsViewModel(controller);
        first.IsLightSelected = true;
        await first.PendingSave;

        var again = new AppearanceSettingsViewModel(controller);

        Assert.True(again.IsLightSelected);
        Assert.False(again.IsSystemSelected);
    }

    [Fact]
    public async Task ViewModel_SaveFailure_ShowsAnHonestMessage_ThatTheNextSuccessClears()
    {
        var (controller, store, _, target) = Create();
        controller.Initialize();
        var viewModel = new AppearanceSettingsViewModel(controller);
        store.SaveException = new UnauthorizedAccessException("synthetic");

        viewModel.IsDarkSelected = true;
        Assert.False(await viewModel.PendingSave);
        Assert.Equal(AppearanceSettingsViewModel.SaveFailedMessage, viewModel.SaveMessage);
        Assert.Equal(EffectiveAppearance.Dark, target.Last); // still applied for this session

        store.SaveException = null;
        viewModel.IsLightSelected = true;
        Assert.True(await viewModel.PendingSave);
        Assert.Null(viewModel.SaveMessage);
    }

    [Fact]
    public void ViewModel_SystemHint_FollowsTheOperatingSystem_EvenWhileForced()
    {
        var (controller, _, os, _) = Create(AppearancePreference.Dark, EffectiveAppearance.Light);
        controller.Initialize();
        var viewModel = new AppearanceSettingsViewModel(controller);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal("Windows is currently set to Light.", viewModel.SystemAppearanceText);

        os.Switch(EffectiveAppearance.Dark);

        Assert.Equal("Windows is currently set to Dark.", viewModel.SystemAppearanceText);
        Assert.Contains(nameof(AppearanceSettingsViewModel.SystemAppearanceText), changed);
        Assert.True(viewModel.IsDarkSelected);
    }

    [Fact]
    public async Task ViewModel_WithoutAController_IsDetached_AndChangesNothingOutsideItself()
    {
        var viewModel = new AppearanceSettingsViewModel();
        Assert.True(viewModel.IsSystemSelected);

        viewModel.IsDarkSelected = true;
        Assert.True(await viewModel.PendingSave);

        Assert.True(viewModel.IsDarkSelected); // held in memory only: no file, no application theme
    }

    [Fact]
    public void SettingsViewModel_DefaultsToADetachedAppearanceSection()
    {
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());

        Assert.NotNull(settings.Appearance);
        Assert.True(settings.Appearance.IsSystemSelected);
    }

    // ---------------------------------------------------------------- selector markup

    [Fact]
    public void Selector_HasExactlyThreeOptions_InOneGroup_InOrder()
    {
        var section = AppearanceSectionMarkup();

        var radios = Regex.Matches(section, "<RadioButton ");
        Assert.Equal(3, radios.Count);
        Assert.Equal(3, Regex.Matches(section, "GroupName=\"Appearance\"").Count);

        var system = section.IndexOf("Content=\"Use Windows setting\"", StringComparison.Ordinal);
        var light = section.IndexOf("Content=\"Light\"", StringComparison.Ordinal);
        var dark = section.IndexOf("Content=\"Dark\"", StringComparison.Ordinal);
        Assert.True(system > 0 && system < light && light < dark);

        Assert.Contains("IsChecked=\"{Binding Appearance.IsSystemSelected}\"", section);
        Assert.Contains("IsChecked=\"{Binding Appearance.IsLightSelected}\"", section);
        Assert.Contains("IsChecked=\"{Binding Appearance.IsDarkSelected}\"", section);
    }

    [Fact]
    public void Selector_IsOneTabStop_WithArrowKeyNavigation()
    {
        var section = AppearanceSectionMarkup();

        Assert.Contains("KeyboardNavigation.TabNavigation=\"Once\"", section);
        Assert.Contains("XYFocus.NavigationModes=\"Keyboard\"", section);
        Assert.Contains("GotFocus=\"OnAppearanceOptionGotFocus\"", section);
        Assert.Equal(3, Regex.Matches(section, "IsCheckedChanged=\"OnAppearanceOptionCheckedChanged\"").Count);
        Assert.DoesNotContain("IsTabStop=\"False\"", section);

        // The code-behind only moves focus/selection; it never reads or writes settings itself.
        var codeBehind = Markup("src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml.cs");
        Assert.DoesNotContain("Appearance.", codeBehind.Replace("AppearanceOptions", string.Empty));
        Assert.DoesNotContain("Controller", codeBehind);
    }

    [Fact]
    public void Selector_HasAccessibleNames_ForTheGroupAndEveryOption()
    {
        var section = AppearanceSectionMarkup();

        Assert.Contains("x:Name=\"AppearanceSection\" Classes=\"panel\" AutomationProperties.Name=\"Appearance\"", section);
        Assert.Contains("x:Name=\"AppearanceOptions\"", section);
        foreach (var name in new[] { "Use Windows setting", "Light", "Dark" })
        {
            Assert.Contains($"AutomationProperties.Name=\"{name}\"", section);
        }

        Assert.Contains("Choose how Carowinds Operations looks.", section);
        Assert.Contains("Saved on this computer only.", section);
    }

    [Fact]
    public void Selector_HasNoApplyButton_NoHardCodedColors_AndShowsSaveFailuresWithTheSharedErrorStyle()
    {
        var section = AppearanceSectionMarkup();

        Assert.DoesNotContain("<Button", section);
        Assert.DoesNotContain("Command=", section);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", section);
        Assert.DoesNotContain("Foreground=", section);
        Assert.DoesNotContain("Background=", section);
        Assert.Contains("Classes=\"statusError\"", section);
        Assert.Contains("Appearance.SaveMessage", section);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", section);
        Assert.Contains("Appearance.SystemAppearanceText", section);
    }

    [Fact]
    public void ThemeTarget_MapsToTheTwoExistingAvaloniaVariants()
    {
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, ApplicationThemeVariantTarget.ToThemeVariant(EffectiveAppearance.Light));
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, ApplicationThemeVariantTarget.ToThemeVariant(EffectiveAppearance.Dark));
    }

    [Fact]
    public void OsSource_MapsPlatformDarkToDark_AndEverythingElseToLight()
    {
        Assert.Equal(EffectiveAppearance.Dark, AvaloniaSystemAppearanceSource.FromPlatform(Avalonia.Platform.PlatformThemeVariant.Dark));
        Assert.Equal(EffectiveAppearance.Light, AvaloniaSystemAppearanceSource.FromPlatform(Avalonia.Platform.PlatformThemeVariant.Light));
        Assert.Equal(EffectiveAppearance.Light, AvaloniaSystemAppearanceSource.FromPlatform(null));
        Assert.Equal(EffectiveAppearance.Light, new AvaloniaSystemAppearanceSource(settings: null).Current);
    }

    [Fact]
    public void NoNewPalette_WasAdded()
    {
        // Phase 22A tokens stay the only palette: still exactly one Light and one Dark theme dictionary.
        var tokens = Markup("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml");
        Assert.Single(Regex.Matches(tokens, "<ResourceDictionary x:Key=\"Light\">"));
        Assert.Single(Regex.Matches(tokens, "<ResourceDictionary x:Key=\"Dark\">"));
        var themes = Directory.GetFiles(Path.GetDirectoryName(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"))!);
        Assert.Equal(["PassdownStyles.axaml", "PassdownTokens.axaml"], themes.Select(Path.GetFileName).Order());
    }
}
