using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>
/// Phase 37C: synthetic weather snapshots for the Dashboard Weather card (no NWS data; every value is invented).
/// The day is Saturday, October 17, 2026 (EDT, UTC−4), matching the Dashboard fixtures' operating day.
/// </summary>
internal static class WeatherUiFixtures
{
    public static readonly DateOnly Day = new(2026, 10, 17);

    /// <summary>9:02 AM EDT.</summary>
    public static readonly DateTimeOffset Morning = new(2026, 10, 17, 13, 2, 0, TimeSpan.Zero);

    /// <summary>7:01 PM EDT.</summary>
    public static readonly DateTimeOffset Evening = new(2026, 10, 17, 23, 1, 0, TimeSpan.Zero);

    private static readonly WeatherPartStatus NeverStatus = new(WeatherAvailability.NeverRetrieved, null, null, WeatherFailureKind.None);

    public static WeatherPartStatus Fresh(DateTimeOffset checkedAt) => new(WeatherAvailability.Fresh, checkedAt, checkedAt, WeatherFailureKind.None);

    public static WeatherObservation Observation(DateTimeOffset at, string station = "KCLT", string name = "Charlotte/Douglas International Airport", double km = 12.3) =>
        new(station, name, km, at, "Partly Cloudy", 68.4, 52, 58, 9.3, null, 225, null, null);

    /// <summary>Today 6 AM–6 PM high 74°, tonight low 55°; issued 6:13 AM. With <paramref name="eveningOnly"/> only "Tonight" remains.</summary>
    public static WeatherForecast Forecast(bool eveningOnly = false)
    {
        var today = new ForecastPeriod(1, "Today", new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero), new(2026, 10, 17, 22, 0, 0, TimeSpan.Zero), true, 74, 20, "5 to 10 mph", "SW", "Mostly Sunny", "Synthetic.");
        var tonight = new ForecastPeriod(2, "Tonight", new(2026, 10, 17, 22, 0, 0, TimeSpan.Zero), new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero), false, 55, 10, "5 mph", "S", "Mostly Clear", "Synthetic.");
        var tomorrow = new ForecastPeriod(3, "Sunday", new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero), new(2026, 10, 18, 22, 0, 0, TimeSpan.Zero), true, 77, 30, "5 mph", "S", "Partly Sunny", "Synthetic.");
        var issued = eveningOnly ? new DateTimeOffset(2026, 10, 17, 20, 2, 0, TimeSpan.Zero) : new DateTimeOffset(2026, 10, 17, 10, 13, 0, TimeSpan.Zero);
        return new WeatherForecast("GSP", 116, 59, issued, eveningOnly ? [tonight, tomorrow] : [today, tonight, tomorrow]);
    }

    /// <summary>
    /// Hourly chances from 9 AM EDT for 36 h: 20% at 3 PM, 5% at 8 PM and 40% at 11 PM (after the 10 AM–10 PM park
    /// hours), so the park-hours window and the rest-of-day window give different answers.
    /// </summary>
    public static HourlyWeatherForecast Hourly()
    {
        var start = new DateTimeOffset(2026, 10, 17, 13, 0, 0, TimeSpan.Zero);
        var periods = Enumerable.Range(0, 36).Select(i =>
        {
            var from = start.AddHours(i);
            var easternHour = (9 + i) % 24;
            int? chance = easternHour switch { 15 => 20, 20 => 5, 23 => 40, _ => i % 2 == 0 ? 2 : null };
            return new HourlyForecastPeriod(from, from.AddHours(1), easternHour is >= 7 and < 19, 70, chance, 58, 52, "5 mph", "SW", "Sunny");
        }).ToArray();
        return new HourlyWeatherForecast("GSP", 116, 59, start.AddHours(-3), periods);
    }

    public static WeatherAlert Alert(string id, string @event, string severity, DateTimeOffset effective, DateTimeOffset ends) =>
        new(id, @event, "Actual", "Alert", severity, "Expected", "Likely", effective, effective, effective, ends, ends,
            "Synthetic area", $"{@event} (synthetic)", null, null, "NWS Greenville-Spartanburg SC", [], ["NCZ071"], new Uri($"https://api.weather.gov/alerts/{id}"));

    /// <summary>Fresh observation, forecast, hourly and alerts (none) at <see cref="Morning"/>.</summary>
    public static WeatherSnapshot Normal(DateTimeOffset? now = null)
    {
        var at = now ?? Morning;
        return new WeatherSnapshot(at, true, Fresh(at.AddMinutes(-20)), Forecast(), Fresh(at.AddMinutes(-20)), Hourly(),
            Fresh(at.AddMinutes(-5)), Observation(at.AddMinutes(-10)), Fresh(at), []);
    }

    /// <summary>A moderate Wind Advisory for both points and a minor Special Weather Statement for the SC point only.</summary>
    public static WeatherSnapshot TwoAlerts()
    {
        var wind = Alert("synthetic-wind", "Wind Advisory", "Moderate", Morning.AddHours(-3), new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));
        var sws = Alert("synthetic-sws", "Special Weather Statement", "Minor", Morning.AddMinutes(-40), new DateTimeOffset(2026, 10, 17, 16, 0, 0, TimeSpan.Zero));
        return Normal() with
        {
            ActiveAlerts = [wind, sws],
            AlertCoverage = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["synthetic-wind"] = ["carowinds-nc", "carowinds-sc"],
                ["synthetic-sws"] = ["carowinds-sc"],
            },
        };
    }

    /// <summary>Refresh failing since 6:37 AM: stale observation (6:52 AM), forecast last checked 6:37 AM, alerts not checked since 6:37 AM.</summary>
    public static WeatherSnapshot Stale()
    {
        var lastGood = new DateTimeOffset(2026, 10, 17, 10, 37, 0, TimeSpan.Zero);
        var failedPart = new WeatherPartStatus(WeatherAvailability.Stale, lastGood, Morning, WeatherFailureKind.Network);
        return new WeatherSnapshot(Morning, true, failedPart, Forecast(), failedPart, Hourly(),
            failedPart, Observation(new DateTimeOffset(2026, 10, 17, 10, 52, 0, TimeSpan.Zero)) with { Description = "Clear", TemperatureF = 61 },
            failedPart, []);
    }

    /// <summary>Nothing usable: every attempt failed before any value (network).</summary>
    public static WeatherSnapshot Unavailable()
    {
        var failed = new WeatherPartStatus(WeatherAvailability.Unavailable, null, Morning, WeatherFailureKind.Network);
        return new WeatherSnapshot(Morning, true, failed, null, failed, null, failed, null, failed, []);
    }

    /// <summary>Never retrieved (first run, before the first check).</summary>
    public static WeatherSnapshot NeverRetrieved() =>
        new(Morning, true, NeverStatus, null, NeverStatus, null, NeverStatus, null, NeverStatus, []);

    /// <summary>At 7:01 PM: the forecast now starts with "Tonight", so today's high is gone.</summary>
    public static WeatherSnapshot EveningForecast() =>
        new(Evening, true, Fresh(Evening.AddMinutes(-30)), Forecast(eveningOnly: true), Fresh(Evening.AddMinutes(-30)), Hourly(),
            Fresh(Evening.AddMinutes(-5)), Observation(Evening.AddMinutes(-9)) with { TemperatureF = 62, Description = "Clear", WindSpeedMph = 3.5, WindDirectionDegrees = 180 },
            Fresh(Evening), []);
}

/// <summary>A scriptable <see cref="IWeatherMonitor"/>: no loop, no provider, counts manual refreshes.</summary>
internal sealed class FakeWeatherMonitor : IWeatherMonitor
{
    public WeatherSnapshot? Current { get; set; }

    public bool IsRefreshing { get; set; }

    public int StartCount { get; private set; }

    public int RefreshNowCount { get; private set; }

    public Func<Task<WeatherManualRefreshResult>> OnRefreshNow { get; set; } =
        () => Task.FromResult(new WeatherManualRefreshResult(WeatherManualRefreshOutcome.Completed));

    public event EventHandler? Changed;

    public int ChangedSubscriberCount => Changed?.GetInvocationList().Length ?? 0;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Start() => StartCount++;

    public Task StopAsync() => Task.CompletedTask;

    public Task<WeatherManualRefreshResult> RefreshNowAsync()
    {
        RefreshNowCount++;
        return OnRefreshNow();
    }
}
