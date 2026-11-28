using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37D: Settings → Passdown Defaults → "Include Weather Outlook by default" in a real (headless) window, Light
/// and Dark, at the 960 px minimum and wider: visible inside the window, the same checkbox styling as the Passdown
/// Defaults checkboxes, named for screen readers, reachable and toggled from the keyboard, and bound to the shared
/// preference. A partial of this run-alone class. Headless renders are not Windows DPI evidence.
/// </summary>
public partial class Phase15ERealWindowLayoutTests
{
    [Theory]
    [InlineData(960d, 640d, "Light")]
    [InlineData(960d, 640d, "Dark")]
    [InlineData(1280d, 860d, "Light")]
    [InlineData(1920d, 1080d, "Dark")]
    public async Task Phase37D_SettingsWeatherOutlookCheckbox_IsVisible_Accessible_AndToggledFromTheKeyboard(double width, double height, string theme)
    {
        var store = new InMemoryPassdownWeatherOutlookPreferenceStore();
        var shared = new PassdownWeatherOutlookSettingsViewModel(store);
        await shared.PendingLoad;
        var viewModel = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator(),
            weatherOutlook: shared);
        await viewModel.PendingLoad;
        var window = RealizeInWindow(new PersonalSyncSettingsView { DataContext = viewModel }, width, height);
        window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var box = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IncludeWeatherOutlookByDefault");
        box.BringIntoView();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(box.IsEffectivelyVisible && box.IsEffectivelyEnabled && box.Focusable && box.IsTabStop);
        Assert.Equal("Include Weather Outlook by default", box.Content);
        Assert.Equal("Include Weather Outlook by default", AutomationProperties.GetName(box));
        Assert.True(box.IsChecked);
        var bounds = AbsoluteBounds(box, window);
        Assert.True(bounds.Width > 0 && bounds.X >= 0 && bounds.Right <= window.ClientSize.Width + 1, $"checkbox {bounds} is outside the window");

        // The same styling as the Passdown Defaults checkboxes beside it (no local overrides).
        var neighbour = window.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Select Shows automatically for a new Passdown");
        Assert.Equal(neighbour.Classes.Where(c => !c.StartsWith(':')).ToArray(), box.Classes.Where(c => !c.StartsWith(':')).ToArray());
        Assert.Equal(neighbour.FontSize, box.FontSize);
        Assert.Equal(neighbour.Foreground?.ToString(), box.Foreground?.ToString());

        // The description sits with it.
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == PassdownWeatherOutlookSettingsViewModel.Description && t.IsEffectivelyVisible);

        Assert.True(box.Focus(NavigationMethod.Tab));
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(shared.IncludeWeatherOutlook);
        await shared.PendingSave;
        Assert.False(store.Current.IncludeWeatherOutlook);

        // A change made elsewhere (the Passdown checkbox) shows here at once.
        shared.IncludeWeatherOutlook = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(box.IsChecked);

        window.Close();
    }
}
