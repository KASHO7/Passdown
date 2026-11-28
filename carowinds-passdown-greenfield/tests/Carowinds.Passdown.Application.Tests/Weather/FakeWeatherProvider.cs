using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Tests.Weather;

/// <summary>Scriptable <see cref="IWeatherProvider"/> that records every call and the validators it was given.</summary>
internal sealed class FakeWeatherProvider
{
    public List<string> Calls { get; } = [];

    public List<(string Endpoint, WeatherConditionalHeaders Conditional)> Conditionals { get; } = [];

    public Func<WeatherReferencePoint, Task<WeatherFetchResult<WeatherPointMetadata>>> Point { get; set; } = null!;

    public Func<Task<WeatherFetchResult<IReadOnlyList<WeatherStation>>>> Stations { get; set; } = null!;

    public Func<WeatherConditionalHeaders, Task<WeatherFetchResult<WeatherForecast>>> Forecast { get; set; } = null!;

    public Func<WeatherConditionalHeaders, Task<WeatherFetchResult<HourlyWeatherForecast>>> Hourly { get; set; } = null!;

    public Func<WeatherStation, Task<WeatherFetchResult<WeatherObservation>>> Observation { get; set; } = null!;

    public Func<WeatherReferencePoint, WeatherConditionalHeaders, Task<WeatherFetchResult<IReadOnlyList<WeatherAlert>>>> Alerts { get; set; } = null!;

    /// <summary>Every endpoint succeeds with typical NWS cache lifetimes.</summary>
    public static FakeWeatherProvider AllSucceed(Func<DateTimeOffset> now)
    {
        var provider = new FakeWeatherProvider();
        provider.Point = _ => Ok<WeatherPointMetadata>(WeatherTestData.Grid(), now().AddHours(24));
        provider.Stations = () => Ok(WeatherTestData.Stations, now().AddHours(24));
        provider.Forecast = _ => Ok(WeatherTestData.Forecast(now()), now().AddHours(1), lastModified: now().AddHours(-1));
        provider.Hourly = _ => Ok(WeatherTestData.Hourly(now()), now().AddHours(1), lastModified: now().AddHours(-1));
        provider.Observation = station => Ok(WeatherTestData.Observation(station.Id, now().AddMinutes(-10)), now().AddMinutes(5));
        provider.Alerts = (_, _) => Ok<IReadOnlyList<WeatherAlert>>([], now().AddSeconds(5), etag: "W/\"alerts-1\"");
        return provider;
    }

    public static Task<WeatherFetchResult<T>> Ok<T>(T value, DateTimeOffset expires, string? etag = null, DateTimeOffset? lastModified = null)
        where T : class =>
        Task.FromResult(WeatherFetchResult<T>.Retrieved(value, etag, lastModified, expires));

    public static Task<WeatherFetchResult<T>> Fail<T>(WeatherFailureKind kind, TimeSpan? retryAfter = null)
        where T : class =>
        Task.FromResult(WeatherFetchResult<T>.Failed(kind, retryAfter));

    public IWeatherProvider AsProvider() => new Adapter(this);

    private sealed class Adapter(FakeWeatherProvider fake) : IWeatherProvider
    {
        public Task<WeatherFetchResult<WeatherPointMetadata>> GetPointAsync(WeatherReferencePoint point, CancellationToken cancellationToken)
        {
            fake.Calls.Add($"points:{point.Id}");
            return fake.Point(point);
        }

        public Task<WeatherFetchResult<WeatherForecast>> GetForecastAsync(WeatherPointMetadata point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken)
        {
            fake.Calls.Add("forecast");
            fake.Conditionals.Add(("forecast", conditional));
            return fake.Forecast(conditional);
        }

        public Task<WeatherFetchResult<HourlyWeatherForecast>> GetHourlyForecastAsync(WeatherPointMetadata point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken)
        {
            fake.Calls.Add("hourly");
            fake.Conditionals.Add(("hourly", conditional));
            return fake.Hourly(conditional);
        }

        public Task<WeatherFetchResult<IReadOnlyList<WeatherStation>>> GetStationsAsync(WeatherPointMetadata point, CancellationToken cancellationToken)
        {
            fake.Calls.Add("stations");
            return fake.Stations();
        }

        public Task<WeatherFetchResult<WeatherObservation>> GetLatestObservationAsync(WeatherStation station, WeatherReferencePoint distanceFrom, WeatherConditionalHeaders conditional, CancellationToken cancellationToken)
        {
            fake.Calls.Add($"observation:{station.Id}");
            return fake.Observation(station);
        }

        public Task<WeatherFetchResult<IReadOnlyList<WeatherAlert>>> GetActiveAlertsAsync(WeatherReferencePoint point, WeatherConditionalHeaders conditional, CancellationToken cancellationToken)
        {
            fake.Calls.Add($"alerts:{point.Id}");
            fake.Conditionals.Add(($"alerts:{point.Id}", conditional));
            return fake.Alerts(point, conditional);
        }
    }
}

/// <summary>In-memory <see cref="IWeatherCacheStore"/> that counts saves and can be made to fail.</summary>
internal sealed class InMemoryWeatherCacheStore(WeatherCacheDocument? initial = null) : IWeatherCacheStore
{
    public WeatherCacheDocument? Saved { get; private set; } = initial;

    public int SaveCount { get; private set; }

    public bool FailSaves { get; set; }

    public Task<WeatherCacheDocument?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);

    public Task<bool> SaveAsync(WeatherCacheDocument document, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        if (FailSaves)
        {
            return Task.FromResult(false);
        }

        Saved = document;
        return Task.FromResult(true);
    }
}
