using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37F follow-up: "Include Weather Outlook" in a real (headless) window — a check item in the Passdown Actions
/// menu, right after the PDF style choices, bound both ways; the toolbar itself is unchanged (the Phase 35B toolbar,
/// keyboard-order and minimum-width tests in this class still pass). A partial of this run-alone class.
/// </summary>
public partial class Phase33PreviewWindowTests
{
    [Theory]
    [InlineData(1280d, 800d)]
    [InlineData(960d, 640d)]
    public async Task IncludeWeatherOutlook_IsACheckItemInTheActionsMenu_AndBindsBothWays(double width, double height)
    {
        var r = await RealizeAsync(width, height);
        var actions = (Button)Named(r.Window, "PassdownActionsButton");
        var menu = Assert.IsType<MenuFlyout>(actions.Flyout);

        menu.ShowAt(actions);
        Dispatcher.UIThread.RunJobs();

        var items = menu.Items.OfType<MenuItem>().ToList();
        var item = Assert.Single(items, i => i.Name == "IncludeWeatherOutlookMenuItem");
        Assert.Equal("Include Weather Outlook", item.Header);
        Assert.Equal("Include Weather Outlook", AutomationProperties.GetName(item));
        Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
        Assert.Equal(items.FindIndex(i => (i.Header as string) == "PDF Style: Modern") + 1, items.IndexOf(item));
        Assert.True(item.IsEffectivelyEnabled);

        // Default on; both directions of the binding.
        Assert.True(item.IsChecked);
        item.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(r.ViewModel.IncludeWeatherOutlook);

        r.ViewModel.IncludeWeatherOutlook = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(item.IsChecked);

        menu.Hide();
        Dispatcher.UIThread.RunJobs();
        AssertNoControlClipsHorizontally(r.Window);
        r.Window.Close();
    }
}
