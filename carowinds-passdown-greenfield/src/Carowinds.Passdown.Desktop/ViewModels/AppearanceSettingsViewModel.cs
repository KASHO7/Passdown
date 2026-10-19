using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 23C: Settings → Appearance. Three radio options (Use Windows setting / Light / Dark) bound to
/// <see cref="IsSystemSelected"/>/<see cref="IsLightSelected"/>/<see cref="IsDarkSelected"/>. Picking one
/// applies it at once through <see cref="AppearanceThemeController"/> and saves it on this computer only.
/// Selection state always comes from the controller, so it is correct after leaving and returning to
/// Settings and after a restart.
/// </summary>
public partial class AppearanceSettingsViewModel : ViewModelBase
{
    public const string SaveFailedMessage = "This choice couldn't be saved on this computer. It applies until the app closes.";

    private readonly AppearanceThemeController _controller;

    public AppearanceSettingsViewModel(AppearanceThemeController? controller = null)
    {
        _controller = controller ?? AppearanceThemeController.CreateDetached();
        _controller.Changed += (_, _) => Refresh();
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
