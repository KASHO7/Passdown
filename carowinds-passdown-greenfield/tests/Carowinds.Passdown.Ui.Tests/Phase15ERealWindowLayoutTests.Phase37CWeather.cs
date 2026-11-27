using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37C (run-alone real-window class): the Weather card realized in the real Dashboard view at wide, medium and
/// narrow widths, Light and Dark — first in the right-hand column (after Shows and before Pricing when stacked), no
/// control past the window edge, no overlap, every state's words rendered, the details reachable by keyboard, and no
/// weather request from showing, resizing or expanding it.
/// </summary>
public partial class Phase15ERealWindowLayoutTests
{
    private static (Window Window, OperationsDashboardViewModel ViewModel, FakeWeatherMonitor Monitor) RealizeWeatherDashboard(
        double width, double height, ThemeVariant theme, WeatherSnapshot? weather, bool expanded = false, DateTimeOffset? clock = null)
    {
        var monitor = new FakeWeatherMonitor { Current = weather };
        var weatherViewModel = new DashboardWeatherViewModel(monitor, new FakeClock(clock ?? WeatherUiFixtures.Morning), new ImmediateUiDispatcher()) { IsExpanded = expanded };
        var service = new FakeOperationsDashboardService { SnapshotFactory = _ => FullDashboard(WeatherUiFixtures.Day) };
        Phase23BDashboardShowsFixtures.Apply(service, Phase23BDashboardShowsFixtures.Scheduled(Phase23BDashboardShowsFixtures.Busy));
        var viewModel = new OperationsDashboardViewModel(service, uiDispatcher: new ImmediateUiDispatcher(), weather: weatherViewModel);
        var window = RealizeInWindow(new DashboardView { DataContext = viewModel }, width, height);
        viewModel.PendingLoad.GetAwaiter().GetResult();
        window.RequestedThemeVariant = theme;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, viewModel, monitor);
    }

    private static Border Card(Window window, string name) =>
        window.GetVisualDescendants().OfType<Border>().Single(border => AutomationProperties.GetName(border) == name);

    private static IEnumerable<string> RealizedTexts(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Bounds.Width > 0)
            .Select(text => text.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text)) : text.Text ?? string.Empty);

    [Theory]
    [InlineData(1920d, 1080d, "Dark")]
    [InlineData(1280d, 860d, "Light")]
    [InlineData(1280d, 720d, "Dark")]
    [InlineData(960d, 640d, "Light")] // medium: columns 1.4 : 1
    [InlineData(700d, 640d, "Dark")]  // narrow: columns stack
    public void Phase37C_WeatherCard_IsFirstOnTheRight_FitsAndOverlapsNothing(double width, double height, string theme)
    {
        var (window, _, monitor) = RealizeWeatherDashboard(width, height, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, WeatherUiFixtures.TwoAlerts());

        var weather = AbsoluteBounds(Card(window, "Weather"), window);
        var pricing = AbsoluteBounds(Card(window, "Daily admission"), window);
        var shows = AbsoluteBounds(Card(window, "Today's shows"), window);
        var passdown = AbsoluteBounds(Card(window, "Passdown Status"), window);

        Assert.True(weather.Height > 0 && weather.Width > 0, "the Weather card is realized");
        Assert.True(weather.Right <= window.ClientSize.Width + 1.0, "Weather runs past the window edge");
        Assert.True(weather.Bottom <= pricing.Top - 15, "Weather sits above Daily Ticket Pricing with the column gap");
        Assert.Equal(pricing.Left, weather.Left, 1.0);
        Assert.Equal(pricing.Width, weather.Width, 1.0);

        var view = window.GetVisualDescendants().OfType<DashboardView>().Single();
        if (view.LayoutMode == DashboardLayoutMode.Narrow)
        {
            Assert.True(weather.Top >= shows.Bottom, "stacked: Weather follows Shows");
        }
        else
        {
            Assert.Equal(passdown.Top, weather.Top, 1.0); // the left column does not move
        }

        foreach (var other in new[] { pricing, shows, passdown })
        {
            var overlap = weather.Intersect(other);
            Assert.False(overlap.Width > 1 && overlap.Height > 1, "Weather overlaps another card");
        }

        AssertNoControlClipsHorizontally(window);
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "DashboardScroller");
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1.0, "no horizontal scrolling");
        Assert.Equal(0, monitor.RefreshNowCount);
        window.Close();
    }

    [Theory]
    [InlineData(1280d, 860d, "Light")]
    [InlineData(960d, 640d, "Dark")]
    [InlineData(700d, 640d, "Light")]
    public void Phase37C_WeatherDetails_ShowEveryAlertWithItsPortion_AndRefreshWeather(double width, double height, string theme)
    {
        var (window, _, monitor) = RealizeWeatherDashboard(width, height, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, WeatherUiFixtures.TwoAlerts(), expanded: true);
        var card = Card(window, "Weather");
        var texts = RealizedTexts(card).ToList();

        Assert.Contains("2 active NWS alerts", texts);
        Assert.Contains("Wind Advisory · until 8:00 PM ET", texts);
        Assert.Contains("Applies to NC and SC portions", texts);
        Assert.Contains("Applies to SC portion only", texts);
        Assert.Contains("OBSERVED · CHARLOTTE AIRPORT", texts);
        Assert.Contains(texts, text => text.StartsWith("Informational only, not a warning system.", StringComparison.Ordinal));

        var refresh = card.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RefreshWeatherButton");
        Assert.True(refresh.IsEffectivelyVisible && refresh.IsEffectivelyEnabled);
        Assert.Equal(2, card.GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("wxAlertItem")));
        AssertNoControlClipsHorizontally(window);
        Assert.Equal(0, monitor.RefreshNowCount);
        window.Close();
    }

    [Theory]
    [InlineData("Stale", "Weather could not be refreshed.", "May be out of date")]
    [InlineData("Unavailable", "Weather unavailable.", "National Weather Service")]
    [InlineData("Loading", "Getting weather from the National Weather Service…", "National Weather Service")]
    [InlineData("Evening", "Today's high not in current forecast", "National Weather Service")]
    public void Phase37C_WeatherStates_RenderTheirWords(string state, string expectedText, string expectedHeader)
    {
        var snapshot = state switch
        {
            "Stale" => WeatherUiFixtures.Stale(),
            "Unavailable" => WeatherUiFixtures.Unavailable(),
            "Loading" => null,
            _ => WeatherUiFixtures.EveningForecast(),
        };
        var (window, _, _) = RealizeWeatherDashboard(1280, 860, ThemeVariant.Light, snapshot,
            clock: state == "Evening" ? WeatherUiFixtures.Evening : WeatherUiFixtures.Morning); // 7:01 PM for the evening forecast

        var texts = RealizedTexts(Card(window, "Weather")).ToList();

        Assert.Contains(texts, text => text.Contains(expectedText, StringComparison.Ordinal));
        Assert.Contains(expectedHeader, texts);
        Assert.DoesNotContain(texts, text => text.StartsWith("No active NWS alerts", StringComparison.Ordinal) && state != "Evening");
        if (state is "Unavailable" or "Loading")
        {
            Assert.DoesNotContain(texts, text => text.Contains("°", StringComparison.Ordinal));
        }

        window.Close();
    }

    [Fact]
    public void Phase37C_KeyboardReachesTheWeatherDetailsBetweenShowsAndPricing_AndSpaceExpandsWithoutARequest()
    {
        var (window, viewModel, monitor) = RealizeWeatherDashboard(1280, 860, ThemeVariant.Light, WeatherUiFixtures.Normal());
        var expected = new[] { "View Shows", "Weather details", "View Pricing" };

        var first = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Check Online");
        Assert.True(first.Focus(NavigationMethod.Tab));
        var visited = new List<string> { "Check Online" };
        for (var i = 0; i < 40; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (window.FocusManager?.GetFocusedElement() is Control focused && AutomationProperties.GetName(focused) is { Length: > 0 } name)
            {
                visited.Add(name);
                if (name == "Weather details")
                {
                    break;
                }
            }
        }

        Assert.Equal(expected[0], visited[^2]);
        Assert.Equal(expected[1], visited[^1]);

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.Weather!.IsExpanded);
        var toggle = window.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "WeatherDetailsToggle");
        Assert.Equal("Hide weather details", AutomationProperties.GetName(toggle));
        Assert.Equal(0, monitor.RefreshNowCount);
        window.Close();
    }

    [Fact]
    public void Phase37C_ResizingAndThemeChanges_MakeNoWeatherRequest()
    {
        var (window, _, monitor) = RealizeWeatherDashboard(1920, 1080, ThemeVariant.Light, WeatherUiFixtures.TwoAlerts(), expanded: true);

        foreach (var w in new[] { 960d, 700d, 1280d })
        {
            window.Width = w;
            window.RequestedThemeVariant = w < 1000 ? ThemeVariant.Dark : ThemeVariant.Light;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AssertNoControlClipsHorizontally(window);
        }

        Assert.Equal(0, monitor.RefreshNowCount);
        Assert.Equal(0, monitor.StartCount);
        window.Close();
    }
}
