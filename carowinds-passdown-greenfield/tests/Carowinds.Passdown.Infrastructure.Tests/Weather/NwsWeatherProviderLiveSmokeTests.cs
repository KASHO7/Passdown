using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Carowinds.Passdown.Infrastructure.Weather.Nws;
using Xunit.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Weather;

/// <summary>
/// Opt-in live check against api.weather.gov (Category=LiveSmoke; excluded from the normal suite). Five read-only
/// GETs: both park reference points, the forecast, and active alerts for both points. Asserts structure only (the
/// weather changes) and that both points resolve to the expected state's zones. Writes nothing to disk.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class NwsWeatherProviderLiveSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ParkPoints_ResolveToNcAndScZones_AndForecastAndAlertsParse()
    {
        using var httpClient = NwsHttpTransport.CreateHttpClient();
        var provider = new NwsWeatherProvider(new NwsHttpTransport(httpClient, NwsHttpTransport.BuildUserAgent("0.1.0-rc.14-livesmoke"), TimeSpan.FromSeconds(20)));

        var nc = await provider.GetPointAsync(CarowindsWeatherLocation.NorthCarolina, CancellationToken.None);
        var sc = await provider.GetPointAsync(CarowindsWeatherLocation.SouthCarolina, CancellationToken.None);
        output.WriteLine($"NC point: {nc.Outcome} {nc.Value?.GridId}/{nc.Value?.GridX},{nc.Value?.GridY} {nc.Value?.ForecastZone} {nc.Value?.County}");
        output.WriteLine($"SC point: {sc.Outcome} {sc.Value?.GridId}/{sc.Value?.GridX},{sc.Value?.GridY} {sc.Value?.ForecastZone} {sc.Value?.County}");
        Assert.Equal(WeatherFetchOutcome.Retrieved, nc.Outcome);
        Assert.Equal(WeatherFetchOutcome.Retrieved, sc.Outcome);
        Assert.StartsWith("NCZ", nc.Value!.ForecastZone, StringComparison.Ordinal);
        Assert.StartsWith("SCZ", sc.Value!.ForecastZone, StringComparison.Ordinal);

        var forecast = await provider.GetForecastAsync(nc.Value, WeatherConditionalHeaders.None, CancellationToken.None);
        output.WriteLine($"Forecast: {forecast.Outcome} {forecast.Value?.Periods.Count} periods, updated {forecast.Value?.UpdateTime:O}, expires {forecast.ExpiresAt:O}");
        Assert.Equal(WeatherFetchOutcome.Retrieved, forecast.Outcome);
        Assert.NotEmpty(forecast.Value!.Periods);

        foreach (var point in CarowindsWeatherLocation.Park.Points)
        {
            var alerts = await provider.GetActiveAlertsAsync(point, WeatherConditionalHeaders.None, CancellationToken.None);
            output.WriteLine($"Alerts {point.Id}: {alerts.Outcome} {alerts.Value?.Count} (ETag {alerts.ETag ?? "none"})");
            Assert.Equal(WeatherFetchOutcome.Retrieved, alerts.Outcome);
        }
    }
}
