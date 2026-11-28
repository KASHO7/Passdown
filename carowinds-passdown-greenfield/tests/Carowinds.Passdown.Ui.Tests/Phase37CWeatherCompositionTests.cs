using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Weather;
using Microsoft.Extensions.DependencyInjection;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37C: the composition root activates weather through one long-lived monitor at the verified park points,
/// and resolving the services (as the headless scheduled-refresh host and the Dashboard do) starts nothing.
/// </summary>
public class Phase37CWeatherCompositionTests
{
    [Fact]
    public void BuildServiceProvider_RegistersOneMonitor_OneCoordinator_AndOneCardViewModel()
    {
        var provider = App.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IWeatherMonitor>(), provider.GetRequiredService<IWeatherMonitor>());
        Assert.Same(provider.GetRequiredService<WeatherRefreshCoordinator>(), provider.GetRequiredService<WeatherRefreshCoordinator>());
        Assert.Same(provider.GetRequiredService<DashboardWeatherViewModel>(), provider.GetRequiredService<DashboardWeatherViewModel>());
        Assert.IsType<AvaloniaWeatherLinkLauncher>(provider.GetRequiredService<IWeatherLinkLauncher>());
    }

    [Fact]
    public void BuildServiceProvider_UsesTheVerifiedParkPoints_NeverTheComputersLocation()
    {
        var location = App.BuildServiceProvider().GetRequiredService<WeatherLocation>();

        Assert.Same(CarowindsWeatherLocation.Park, location);
        Assert.True(location.IsRetrievalPermitted);
        Assert.Equal(["35.1050,-80.9420", "35.1002,-80.9420"], location.Points.Select(point => point.ToNwsPointText()));
    }

    [Fact]
    public void ResolvingWeather_StartsNothing()
    {
        var monitor = (WeatherMonitor)App.BuildServiceProvider().GetRequiredService<IWeatherMonitor>();

        Assert.Null(monitor.Current);
        Assert.False(monitor.IsRefreshing);
        Assert.True(monitor.Loop.IsCompleted); // the loop only exists after Start()
    }

    [Fact]
    public async Task RefreshNow_BeforeTheAppStartsTheMonitor_IsRefused()
    {
        var monitor = App.BuildServiceProvider().GetRequiredService<IWeatherMonitor>();

        Assert.Equal(WeatherManualRefreshOutcome.NotPermitted, (await monitor.RefreshNowAsync()).Outcome);
    }
}
