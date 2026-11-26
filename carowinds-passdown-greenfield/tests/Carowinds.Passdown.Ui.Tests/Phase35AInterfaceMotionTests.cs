using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35A: the Interface animations preference — default On only when nothing is saved, a saved Off stays
/// Off, Windows reduced motion disables effective motion without rewriting the choice, and removing it restores
/// motion. Pure controller/view-model tests (no window, no theme), so the class runs in the mixed Ui partition.
/// </summary>
public class Phase35AInterfaceMotionTests
{
    internal sealed class FakeMotionStore : IInterfaceAnimationPreferenceStore
    {
        public InterfaceAnimationPreference Stored { get; set; }

        public List<InterfaceAnimationPreference> Saves { get; } = [];

        public bool FailSaves { get; set; }

        public InterfaceAnimationPreference Load() => Stored;

        public Task SaveAsync(InterfaceAnimationPreference preference, CancellationToken cancellationToken = default)
        {
            if (FailSaves)
            {
                throw new IOException("disk full");
            }

            Saves.Add(preference);
            Stored = preference;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeReducedMotionReader(SystemReducedMotion value) : ISystemReducedMotionReader
    {
        public SystemReducedMotion Value { get; set; } = value;

        public int Reads { get; private set; }

        public SystemReducedMotion Read()
        {
            Reads++;
            return Value;
        }
    }

    internal static (InterfaceMotionController Controller, FakeMotionStore Store, FakeReducedMotionReader Os) Create(
        InterfaceAnimationPreference stored = InterfaceAnimationPreference.On, SystemReducedMotion os = SystemReducedMotion.NotRequested)
    {
        var store = new FakeMotionStore { Stored = stored };
        var reader = new FakeReducedMotionReader(os);
        var controller = new InterfaceMotionController(store, reader);
        controller.Initialize();
        return (controller, store, reader);
    }

    [Fact]
    public void FreshProfile_DefaultsToOn_AndShowsMotion()
    {
        var (controller, store, _) = Create(stored: default);

        Assert.Equal(InterfaceAnimationPreference.On, controller.Preference);
        Assert.True(controller.IsMotionEffective);
        Assert.Empty(store.Saves); // loading never writes the default back
    }

    [Fact]
    public void SavedOff_RemainsOff_AndShowsNoMotion()
    {
        var (controller, _, _) = Create(stored: InterfaceAnimationPreference.Off);

        Assert.Equal(InterfaceAnimationPreference.Off, controller.Preference);
        Assert.False(controller.IsMotionEffective);
    }

    [Fact]
    public void WindowsReducedMotion_DisablesEffectiveMotion_WithoutChangingTheSavedChoice()
    {
        var (controller, store, _) = Create(stored: InterfaceAnimationPreference.On, os: SystemReducedMotion.Requested);

        Assert.False(controller.IsMotionEffective);
        Assert.Equal(InterfaceAnimationPreference.On, controller.Preference);
        Assert.Empty(store.Saves);
    }

    [Fact]
    public void RemovingWindowsReducedMotion_RestoresMotion_WhenTheChoiceIsStillOn()
    {
        var (controller, _, os) = Create(os: SystemReducedMotion.Requested);
        var changes = 0;
        controller.Changed += (_, _) => changes++;

        os.Value = SystemReducedMotion.NotRequested;
        controller.RefreshSystemSetting();

        Assert.True(controller.IsMotionEffective);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void RemovingWindowsReducedMotion_KeepsMotionOff_WhenTheChoiceIsOff()
    {
        var (controller, _, os) = Create(stored: InterfaceAnimationPreference.Off, os: SystemReducedMotion.Requested);

        os.Value = SystemReducedMotion.NotRequested;
        controller.RefreshSystemSetting();

        Assert.False(controller.IsMotionEffective);
    }

    [Fact]
    public void RefreshWithAnUnchangedSetting_RaisesNothing()
    {
        var (controller, _, os) = Create();
        var changes = 0;
        controller.Changed += (_, _) => changes++;

        controller.RefreshSystemSetting();
        controller.RefreshSystemSetting();

        Assert.Equal(0, changes);
        Assert.Equal(3, os.Reads); // startup + two refreshes; nothing reads on its own (no polling)
    }

    [Fact]
    public void ReducedMotionStaysEffective_ThroughRapidRefreshes()
    {
        var (controller, _, os) = Create(os: SystemReducedMotion.Requested);

        for (var i = 0; i < 50; i++)
        {
            controller.RefreshSystemSetting();
            Assert.False(controller.IsMotionEffective);
        }

        Assert.Equal(51, os.Reads);
    }

    [Fact]
    public async Task SetPreference_AppliesImmediately_ThenPersistsTheLatestChoice()
    {
        var (controller, store, _) = Create();

        var save = controller.SetPreferenceAsync(InterfaceAnimationPreference.Off);
        Assert.False(controller.IsMotionEffective); // before the write completes
        Assert.True(await save);

        await controller.SetPreferenceAsync(InterfaceAnimationPreference.On);
        await controller.SetPreferenceAsync(InterfaceAnimationPreference.Off);

        Assert.Equal([InterfaceAnimationPreference.Off, InterfaceAnimationPreference.On, InterfaceAnimationPreference.Off], store.Saves);
        Assert.Equal(InterfaceAnimationPreference.Off, store.Stored);
    }

    [Fact]
    public async Task SetPreference_SameValue_WritesNothing()
    {
        var (controller, store, _) = Create();

        Assert.True(await controller.SetPreferenceAsync(InterfaceAnimationPreference.On));
        Assert.Empty(store.Saves);
    }

    [Fact]
    public async Task SaveFailure_StillAppliesForThisSession_AndReportsFalse()
    {
        var (controller, store, _) = Create();
        store.FailSaves = true;

        Assert.False(await controller.SetPreferenceAsync(InterfaceAnimationPreference.Off));
        Assert.False(controller.IsMotionEffective);
    }

    [Fact]
    public void ReaderThatThrows_IsTreatedAsNotDetected()
    {
        var controller = new InterfaceMotionController(new FakeMotionStore(), new ThrowingReader());
        controller.Initialize();

        Assert.Equal(SystemReducedMotion.NotDetected, controller.SystemReducedMotion);
        Assert.True(controller.IsMotionEffective);
    }

    [Fact]
    public void DetachedController_IsMotionFree_SoHeadlessLayoutIsDeterministic()
    {
        var controller = InterfaceMotionController.CreateDetached();
        controller.Initialize();

        Assert.False(controller.IsMotionEffective);
    }

    [Fact]
    public void Controller_DependsOnlyOnItsLocalStore_AndTheOsReader()
    {
        // No Personal Sync, database, draft, refresh, network or telemetry dependency exists to call.
        var parameters = typeof(InterfaceMotionController).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType.Name).ToList();
        Assert.Equal(["IInterfaceAnimationPreferenceStore", "ISystemReducedMotionReader", "ILogger`1"], parameters);
    }

    // ---------------------------------------------------------------- Settings view model

    [Fact]
    public async Task SettingsViewModel_ReflectsAndChangesTheChoice()
    {
        var (controller, store, _) = Create();
        var vm = new AppearanceSettingsViewModel(motion: controller);

        Assert.True(vm.IsAnimationsOnSelected);
        Assert.False(vm.IsAnimationsOffSelected);
        Assert.True(vm.IsMotionEffective);

        vm.IsAnimationsOffSelected = true;
        await vm.PendingMotionSave;

        Assert.True(vm.IsAnimationsOffSelected);
        Assert.False(vm.IsMotionEffective);
        Assert.Equal(InterfaceAnimationPreference.Off, store.Stored);
    }

    [Fact]
    public async Task SettingsViewModel_UncheckingTheOtherRadio_DoesNotAct()
    {
        var (controller, store, _) = Create();
        var vm = new AppearanceSettingsViewModel(motion: controller);

        vm.IsAnimationsOffSelected = false; // a radio group unchecks the previous option itself
        vm.IsAnimationsOnSelected = true;
        await vm.PendingMotionSave;

        Assert.Empty(store.Saves);
    }

    [Fact]
    public void SettingsViewModel_ExplainsTheWindowsOverride_OnlyWhenItApplies()
    {
        var (controller, _, os) = Create(os: SystemReducedMotion.Requested);
        var vm = new AppearanceSettingsViewModel(motion: controller);
        Assert.Equal(AppearanceSettingsViewModel.MotionOverriddenByWindowsText, vm.MotionSystemText);

        os.Value = SystemReducedMotion.NotRequested;
        vm.RefreshSystemMotion();
        Assert.Null(vm.MotionSystemText);
        Assert.True(vm.IsMotionEffective);
    }

    [Fact]
    public async Task SettingsViewModel_WindowsOffAndChoiceOff_SaysWindowsIsAlsoOff()
    {
        var (controller, _, _) = Create(os: SystemReducedMotion.Requested);
        var vm = new AppearanceSettingsViewModel(motion: controller);

        vm.IsAnimationsOffSelected = true;
        await vm.PendingMotionSave;

        Assert.Equal(AppearanceSettingsViewModel.MotionAlsoOffInWindowsText, vm.MotionSystemText);
    }

    [Fact]
    public void SettingsViewModel_UnreadablePlatform_SaysTheChoiceAloneDecides()
    {
        var (controller, _, _) = Create(os: SystemReducedMotion.NotDetected);

        Assert.Equal(AppearanceSettingsViewModel.MotionSystemNotDetectedText, new AppearanceSettingsViewModel(motion: controller).MotionSystemText);
    }

    [Fact]
    public async Task SettingsViewModel_SaveFailure_ShowsAMessage_ClearedByTheNextSuccess()
    {
        var (controller, store, _) = Create();
        var vm = new AppearanceSettingsViewModel(motion: controller);

        store.FailSaves = true;
        vm.IsAnimationsOffSelected = true;
        await vm.PendingMotionSave;
        Assert.Equal(AppearanceSettingsViewModel.MotionSaveFailedMessage, vm.MotionSaveMessage);

        store.FailSaves = false;
        vm.IsAnimationsOnSelected = true;
        await vm.PendingMotionSave;
        Assert.Null(vm.MotionSaveMessage);
    }

    [Fact]
    public void SettingsViewModel_RaisesChangeNotifications_ForTheEffectiveState()
    {
        var (controller, _, os) = Create();
        var vm = new AppearanceSettingsViewModel(motion: controller);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        os.Value = SystemReducedMotion.Requested;
        vm.RefreshSystemMotion();

        Assert.Contains(nameof(AppearanceSettingsViewModel.IsMotionEffective), changed);
        Assert.Contains(nameof(AppearanceSettingsViewModel.MotionSystemText), changed);
    }

    private sealed class ThrowingReader : ISystemReducedMotionReader
    {
        public SystemReducedMotion Read() => throw new InvalidOperationException("no platform");
    }

    // ---------------------------------------------------------------- Settings markup

    private static string AnimationsMarkup()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml"));
        var start = markup.IndexOf("x:Name=\"InterfaceAnimationsLabel\"", StringComparison.Ordinal);
        var end = markup.IndexOf("</Border>", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        return markup[start..end];
    }

    [Fact]
    public void SettingsMarkup_IsInsideAppearance_WithOnThenOff_InOneAccessibleGroup()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml"));
        var appearance = markup.IndexOf("x:Name=\"AppearanceSection\"", StringComparison.Ordinal);
        var animations = markup.IndexOf("x:Name=\"InterfaceAnimationsLabel\"", StringComparison.Ordinal);
        var nextPanel = markup.IndexOf("x:Name=\"PassdownPreferencesSection\"", StringComparison.Ordinal);
        Assert.True(appearance < animations && animations < nextPanel, "Interface animations sits in Settings → Appearance");

        var section = AnimationsMarkup();
        Assert.Contains("Text=\"Interface animations\"", section);
        // Phase 35F (approved Settings motion prototype): one switch replaces the On / Off radio pair. It is bound to
        // the same saved choice (AreAnimationsOn reads and writes SelectedAnimationPreference) and is one Tab stop.
        Assert.Single(Regex.Matches(section, "<ToggleSwitch "));
        Assert.DoesNotContain("<RadioButton ", section);
        Assert.Contains("x:Name=\"InterfaceAnimationsSwitch\"", section);
        Assert.Contains("IsChecked=\"{Binding Appearance.AreAnimationsOn}\"", section);
        Assert.Contains("AutomationProperties.Name=\"Interface animations\"", section);
        Assert.Contains("AutomationProperties.HelpText=\"Turn short interface transitions on or off on this computer\"", section);
    }

    [Fact]
    public void SettingsMarkup_ExplainsTheWindowsOverride_AndSaveFailures_AsPoliteLiveText()
    {
        var section = AnimationsMarkup();
        Assert.Contains("Text=\"{Binding Appearance.MotionSystemText}\"", section);
        Assert.Contains("Text=\"{Binding Appearance.MotionSaveMessage}\"", section);
        Assert.Equal(2, Regex.Matches(section, "AutomationProperties.LiveSetting=\"Polite\"").Count);
        Assert.Contains("Saved on this computer only.", section);
        Assert.Contains("never delayed", section);
    }

    [Fact]
    public void Texts_NameTheRealWindowsSetting_AndNeverClaimDetectionWhereThereIsNone()
    {
        Assert.Contains("Animation effects", AppearanceSettingsViewModel.MotionOverriddenByWindowsText);
        Assert.Contains("choice here is kept", AppearanceSettingsViewModel.MotionOverriddenByWindowsText);
        Assert.Contains("can't be read", AppearanceSettingsViewModel.MotionSystemNotDetectedText);
    }
}
