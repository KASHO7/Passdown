using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 23C: Settings → Appearance. Three radio options (Use Windows setting / Light / Dark) bound to
/// <see cref="IsSystemSelected"/>/<see cref="IsLightSelected"/>/<see cref="IsDarkSelected"/>. Picking one
/// applies it at once through <see cref="AppearanceThemeController"/> and saves it on this computer only.
/// Phase 35A adds Interface animations (On / Off) through <see cref="InterfaceMotionController"/>.
/// Selection state always comes from the controller, so it is correct after leaving and returning to
/// Settings and after a restart.
/// </summary>
public partial class AppearanceSettingsViewModel : ViewModelBase
{
    public const string SaveFailedMessage = "This choice couldn't be saved on this computer. It applies until the app closes.";

    public const string MotionSaveFailedMessage = "This animation choice couldn't be saved on this computer. It applies until the app closes.";

    /// <summary>Phase 35A: shown while Windows "Animation effects" is off and the in-app choice is On.</summary>
    public const string MotionOverriddenByWindowsText =
        "Windows \u201CAnimation effects\u201D is turned off, so animations are off. Your choice here is kept and applies again if that Windows setting is turned back on.";

    /// <summary>Phase 35A: shown while Windows "Animation effects" is off and the in-app choice is already Off.</summary>
    public const string MotionAlsoOffInWindowsText = "Windows \u201CAnimation effects\u201D is also turned off.";

    /// <summary>Phase 35A: shown on a computer where the operating system setting cannot be read (any non-Windows host).</summary>
    public const string MotionSystemNotDetectedText = "This computer's reduced-motion setting can't be read here, so this choice alone decides.";

    private readonly AppearanceThemeController _controller;
    private readonly InterfaceMotionController _motion;

    public AppearanceSettingsViewModel(AppearanceThemeController? controller = null, InterfaceMotionController? motion = null)
    {
        _controller = controller ?? AppearanceThemeController.CreateDetached();
        _controller.Changed += (_, _) => Refresh();
        _motion = motion ?? InterfaceMotionController.CreateDetached();
        _motion.Changed += (_, _) => RefreshMotion();
    }

    /// <summary>The last save started by a selection; tests await it.</summary>
    public Task<bool> PendingSave { get; private set; } = Task.FromResult(true);

    public AppearancePreference SelectedPreference => _controller.Preference;

    public bool IsSystemSelected
    {
        get => SelectedPreference == AppearancePreference.System;
        set => Select(AppearancePreference.System, value);
    }

    public bool IsLightSelected
    {
        get => SelectedPreference == AppearancePreference.Light;
        set => Select(AppearancePreference.Light, value);
    }

    public bool IsDarkSelected
    {
        get => SelectedPreference == AppearancePreference.Dark;
        set => Select(AppearancePreference.Dark, value);
    }

    /// <summary>What Windows is set to right now — shown under "Use Windows setting" so its effect is clear.</summary>
    public string SystemAppearanceText => _controller.SystemAppearance == EffectiveAppearance.Dark
        ? "Windows is currently set to Dark."
        : "Windows is currently set to Light.";

    /// <summary>Set when the last choice could not be written; cleared by the next successful save.</summary>
    [ObservableProperty]
    public partial string? SaveMessage { get; set; }

    /// <summary>Phase 35A: the last animation-choice save; tests await it.</summary>
    public Task<bool> PendingMotionSave { get; private set; } = Task.FromResult(true);

    public InterfaceAnimationPreference SelectedAnimationPreference => _motion.Preference;

    public bool IsAnimationsOnSelected
    {
        get => SelectedAnimationPreference == InterfaceAnimationPreference.On;
        set => SelectAnimation(InterfaceAnimationPreference.On, value);
    }

    public bool IsAnimationsOffSelected
    {
        get => SelectedAnimationPreference == InterfaceAnimationPreference.Off;
        set => SelectAnimation(InterfaceAnimationPreference.Off, value);
    }

    /// <summary>
    /// Phase 35F: the Settings switch for Interface animations — the same saved On / Off choice as
    /// <see cref="IsAnimationsOnSelected"/> / <see cref="IsAnimationsOffSelected"/>, expressed as one boolean.
    /// It reports the saved choice, not the effective motion: Windows reduced motion never flips it.
    /// </summary>
    public bool AreAnimationsOn
    {
        get => SelectedAnimationPreference == InterfaceAnimationPreference.On;
        set => SelectAnimation(value ? InterfaceAnimationPreference.On : InterfaceAnimationPreference.Off, isChecked: true);
    }

    /// <summary>True when cosmetic transitions are actually shown (choice On and the OS not requesting reduced motion).</summary>
    public bool IsMotionEffective => _motion.IsMotionEffective;

    /// <summary>Explains how the operating system setting affects the choice, or null when there is nothing to add.</summary>
    public string? MotionSystemText => _motion.SystemReducedMotion switch
    {
        SystemReducedMotion.Requested when SelectedAnimationPreference == InterfaceAnimationPreference.On => MotionOverriddenByWindowsText,
        SystemReducedMotion.Requested => MotionAlsoOffInWindowsText,
        SystemReducedMotion.NotDetected => MotionSystemNotDetectedText,
        _ => null,
    };

    /// <summary>Set when the last animation choice could not be written; cleared by the next successful save.</summary>
    [ObservableProperty]
    public partial string? MotionSaveMessage { get; set; }

    /// <summary>Re-reads the operating system's reduced-motion setting (the main window calls this when activated).</summary>
    public void RefreshSystemMotion() => _motion.RefreshSystemSetting();

    private void SelectAnimation(InterfaceAnimationPreference preference, bool isChecked)
    {
        if (!isChecked || preference == SelectedAnimationPreference)
        {
            return;
        }

        PendingMotionSave = SaveMotionAsync(_motion.SetPreferenceAsync(preference));
    }

    private async Task<bool> SaveMotionAsync(Task<bool> save)
    {
        var saved = await save.ConfigureAwait(true);
        MotionSaveMessage = saved ? null : MotionSaveFailedMessage;
        return saved;
    }

    private void RefreshMotion()
    {
        OnPropertyChanged(nameof(SelectedAnimationPreference));
        OnPropertyChanged(nameof(IsAnimationsOnSelected));
        OnPropertyChanged(nameof(IsAnimationsOffSelected));
        OnPropertyChanged(nameof(AreAnimationsOn));
        OnPropertyChanged(nameof(IsMotionEffective));
        OnPropertyChanged(nameof(MotionSystemText));
    }

    private void Select(AppearancePreference preference, bool isChecked)
    {
        // A radio group unchecks the previous option as part of checking the new one; only "checked" acts.
        if (!isChecked || preference == SelectedPreference)
        {
            return;
        }

        PendingSave = SaveAsync(_controller.SetPreferenceAsync(preference));
    }

    private async Task<bool> SaveAsync(Task<bool> save)
    {
        var saved = await save.ConfigureAwait(true);
        SaveMessage = saved ? null : SaveFailedMessage;
        return saved;
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(SelectedPreference));
        OnPropertyChanged(nameof(IsSystemSelected));
        OnPropertyChanged(nameof(IsLightSelected));
        OnPropertyChanged(nameof(IsDarkSelected));
        OnPropertyChanged(nameof(SystemAppearanceText));
    }
}
