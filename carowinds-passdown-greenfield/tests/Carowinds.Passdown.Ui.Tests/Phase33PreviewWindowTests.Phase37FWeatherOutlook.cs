using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37F: the "Weather Outlook" checkbox in Overview → Include in Passdown, in a real (headless) window, Light and
/// Dark, at the 960 px minimum and wider — present, inside the window, directly below Shows in the same column, the
/// next Tab stop after Shows, the same checkbox styling as its neighbours, bound both ways, and absent from the Actions
/// menu. A partial of this run-alone class.
/// </summary>
public partial class Phase33PreviewWindowTests
{
    [Theory]
    [InlineData(960d, 640d, "Light")]
    [InlineData(960d, 640d, "Dark")]
    [InlineData(1280d, 720d, "Light")]
    [InlineData(1920d, 1080d, "Dark")]
    public async Task WeatherOutlookCheckbox_SitsDirectlyBelowShows_InIncludeInPassdown_AndBindsBothWays(double width, double height, string theme)
    {
        var w = await RealizeWorkspaceAsync(width, height, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        var boxes = w.Window.GetVisualDescendants().OfType<CheckBox>().ToList();
        var weather = boxes.Single(box => box.Name == "IncludeWeatherOutlookCheckBox");
        var shows = boxes.Single(box => box.Name == "IncludeShowsCheckBox");

        weather.BringIntoView();
        Pump();

        Assert.True(weather.IsEffectivelyVisible);
        Assert.Equal("Weather Outlook", weather.Content);
        Assert.Equal("Include Weather Outlook in the Passdown", AutomationProperties.GetName(weather));
        Assert.True(weather.Focusable && weather.IsTabStop && weather.IsEffectivelyEnabled);

        // Same column, next row: directly below Shows.
        var weatherRect = AbsoluteBounds(weather, w.Window);
        var showsRect = AbsoluteBounds(shows, w.Window);
        Assert.Equal(showsRect.X, weatherRect.X, 0.5);
        Assert.True(weatherRect.Y >= showsRect.Bottom - 0.5, $"Weather Outlook {weatherRect} is not below Shows {showsRect}");
        Assert.True(weatherRect.Y - showsRect.Bottom <= 8, $"Weather Outlook {weatherRect} is not directly below Shows {showsRect}");
        Assert.True(weatherRect.Width > 0 && weatherRect.X >= 0 && weatherRect.Right <= w.Window.ClientSize.Width + 1, $"Weather Outlook {weatherRect} is outside the window");
        Assert.Equal(showsRect.Height, weatherRect.Height, 0.5);

        // Same styling as its neighbours (no local overrides), and the next keyboard stop after Shows.
        Assert.Equal(shows.Classes.Where(c => !c.StartsWith(':')).ToArray(), weather.Classes.Where(c => !c.StartsWith(':')).ToArray()); // style classes; ':checked' is state
        Assert.Equal(shows.FontSize, weather.FontSize);
        Assert.Equal(shows.Foreground?.ToString(), weather.Foreground?.ToString());
        shows.Focus(NavigationMethod.Tab);
        Pump();
        w.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        w.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Pump();
        Assert.Same(weather, w.Window.FocusManager?.GetFocusedElement());
        w.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        w.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Pump();
        Assert.False(weather.IsChecked); // Space toggles it like every other checkbox here
        Assert.False(w.ViewModel.IncludeWeatherOutlook);
        weather.IsChecked = true;
        Pump();

        // Default on; both directions of the binding; a change refreshes the preview through the existing scheduling.
        Assert.True(weather.IsChecked);
        var version = w.ViewModel.Preview.ContentVersion;
        weather.IsChecked = false;
        Pump();
        Assert.False(w.ViewModel.IncludeWeatherOutlook);
        Assert.True(w.ViewModel.Preview.ContentVersion > version);

        w.ViewModel.IncludeWeatherOutlook = true;
        Pump();
        Assert.True(weather.IsChecked);

        // In season (this fixture) the SCarowinds / Haunt box is shown; off season it is hidden. The panel's fixed
        // ItemWidth keeps a hidden item's cell, so Shows keeps its place and Weather Outlook stays directly below it.
        var haunt = boxes.Single(box => AutomationProperties.GetName(box) == "Include SCarowinds / Haunt in the Passdown");
        Assert.True(haunt.IsVisible);
        var showsBefore = AbsoluteBounds(shows, w.Window);
        haunt.IsVisible = false;
        Pump();
        w.Window.UpdateLayout();
        showsRect = AbsoluteBounds(shows, w.Window);
        weatherRect = AbsoluteBounds(weather, w.Window);
        Assert.Equal(showsBefore, showsRect);
        Assert.Equal(showsRect.X, weatherRect.X, 0.5);
        Assert.True(weatherRect.Y >= showsRect.Bottom - 0.5 && weatherRect.Y - showsRect.Bottom <= 8, $"off season, Weather Outlook {weatherRect} is not directly below Shows {showsRect}");

        // No duplicate in the Actions menu.
        var actions = (Button)Named(w.Window, "PassdownActionsButton");
        Assert.DoesNotContain(Assert.IsType<MenuFlyout>(actions.Flyout).Items.OfType<MenuItem>(), item => (item.Header as string)?.Contains("Weather", StringComparison.Ordinal) == true);

        AssertNoControlClipsHorizontally(w.Window);
        w.Window.Close();
    }
}
