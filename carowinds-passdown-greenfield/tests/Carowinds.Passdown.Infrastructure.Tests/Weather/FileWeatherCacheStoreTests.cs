using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Carowinds.Passdown.Infrastructure.Weather;
using Carowinds.Passdown.Infrastructure.Weather.Nws;

namespace Carowinds.Passdown.Infrastructure.Tests.Weather;

public sealed class FileWeatherCacheStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "carowinds-weather-cache-tests", Guid.NewGuid().ToString("N"));

    private string CachePath => Path.Combine(_directory, "weather-cache.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static WeatherCacheDocument RealisticDocument()
    {
        var now = new DateTimeOffset(2026, 11, 27, 13, 50, 0, TimeSpan.Zero);
        var grid = NwsPointParser.Parse(FixtureFile.ReadText("weather/nws/points-nc-check-35.1037-80.9426-2026-11-27.json"));
        var station = new WeatherStation("KCLT", "Charlotte/Douglas International Airport", 35.20833, -80.96139);
        var alerts = NwsAlertParser.Parse(FixtureFile.ReadText("weather/nws/alerts-active-synthetic-multiple.json")).Alerts;
        return new WeatherCacheDocument
        {
            Points = new Dictionary<string, WeatherEndpointState<WeatherPointMetadata>> { ["carowinds-nc"] = new() { Value = grid, CheckedAt = now, ExpiresAt = now.AddDays(1) } },
            Stations = new() { Value = NwsStationParser.Parse(FixtureFile.ReadText("weather/nws/stations-gsp-116-59-2026-11-27-first3.json")), CheckedAt = now },
            Forecast = new() { Value = NwsForecastParser.ParseTwelveHour(FixtureFile.ReadText("weather/nws/forecast-gsp-116-59-2026-11-27.json"), grid), CheckedAt = now, LastModified = now.AddHours(-2) },
            // The full 156-hour forecast is about five times the fixture; repeat the fixture to that size.
            Hourly = new() { Value = NwsForecastParser.ParseHourly(FixtureFile.ReadText("weather/nws/forecast-hourly-gsp-116-59-2026-11-27-first30.json"), grid) is var hourly ? hourly with { Periods = Enumerable.Repeat(hourly.Periods, 6).SelectMany(periods => periods).Take(156).ToArray() } : null, CheckedAt = now },
            Observations = new Dictionary<string, WeatherEndpointState<WeatherObservation>>
            {
                ["KCLT"] = new() { Value = NwsObservationParser.Parse(FixtureFile.ReadText("weather/nws/observation-latest-kclt-2026-11-27.json"), station, 11.6), CheckedAt = now, LastFailure = WeatherFailureKind.Timeout, ConsecutiveFailures = 1 },
            },
            Alerts = new Dictionary<string, WeatherEndpointState<IReadOnlyList<WeatherAlert>>> { ["carowinds-nc"] = new() { Value = alerts, CheckedAt = now, ETag = "W/\"746d\"" } },
        };
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsEveryPart_WithinTheSizeLimit()
    {
        var store = new FileWeatherCacheStore(CachePath);
        var document = RealisticDocument();

        Assert.True(await store.SaveAsync(document));
        Assert.InRange(new FileInfo(CachePath).Length, 1, FileWeatherCacheStore.MaxBytes);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(WeatherCacheDocument.CurrentFormatVersion, loaded.FormatVersion);
        Assert.Equal(document.Points["carowinds-nc"].Value, loaded.Points["carowinds-nc"].Value);
        Assert.Equal(document.Forecast.Value!.Periods, loaded.Forecast.Value!.Periods);
        Assert.Equal(156, loaded.Hourly.Value!.Periods.Count);
        Assert.Equal(document.Observations["KCLT"].Value, loaded.Observations["KCLT"].Value);
        Assert.Equal(WeatherFailureKind.Timeout, loaded.Observations["KCLT"].LastFailure);
        Assert.Equal(document.Alerts["carowinds-nc"].Value!.Select(alert => (alert.Id, alert.Ends, alert.SourceUrl)), loaded.Alerts["carowinds-nc"].Value!.Select(alert => (alert.Id, alert.Ends, alert.SourceUrl)));
        Assert.Equal(document.Alerts["carowinds-nc"].Value!.SelectMany(alert => alert.ReferenceIds), loaded.Alerts["carowinds-nc"].Value!.SelectMany(alert => alert.ReferenceIds));
        Assert.Equal("W/\"746d\"", loaded.Alerts["carowinds-nc"].ETag);
    }

    [Fact]
    public async Task MissingFile_IsNoCache() => Assert.Null(await new FileWeatherCacheStore(CachePath).LoadAsync());

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("{\"FormatVersion\":2}")]
    [InlineData("{\"FormatVersion\":0}")]
    public async Task CorruptOrUnknownFile_IsNoCache(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(CachePath, content);

        Assert.Null(await new FileWeatherCacheStore(CachePath).LoadAsync());
    }

    [Fact]
    public async Task OversizedFile_IsNotRead()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(CachePath, "{\"FormatVersion\":1,\"Pad\":\"" + new string('x', FileWeatherCacheStore.MaxBytes) + "\"}");

        Assert.Null(await new FileWeatherCacheStore(CachePath).LoadAsync());
    }

    [Fact]
    public async Task OversizedDocument_IsNotWritten_AndThePreviousFileStays()
    {
        var store = new FileWeatherCacheStore(CachePath);
        Assert.True(await store.SaveAsync(WeatherCacheDocument.Empty));
        var before = await File.ReadAllTextAsync(CachePath);
        var huge = new WeatherCacheDocument
        {
            Stations = new() { Value = Enumerable.Range(0, 5000).Select(i => new WeatherStation($"K{i:D4}", new string('n', 60), 35, -80)).ToArray() },
        };

        Assert.False(await store.SaveAsync(huge));
        Assert.Equal(before, await File.ReadAllTextAsync(CachePath));
    }

    [Fact]
    public void DefaultPath_IsLocalAppDataWeatherCacheJson()
    {
        Assert.Equal(Path.Combine(AppPaths.LocalApplicationDataDirectory, "weather-cache.json"), FileWeatherCacheStore.DefaultPath);
    }
}
