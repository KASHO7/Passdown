using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Weather;

public enum WeatherAvailability
{
    /// <summary>No value has ever been retrieved.</summary>
    NeverRetrieved,

    /// <summary>Within the freshness policy.</summary>
    Fresh,

    /// <summary>Older than the freshness policy but still shown, marked as older.</summary>
    Stale,

    /// <summary>Too old to show, or attempts failed before any value existed.</summary>
    Unavailable,
}

/// <summary>Freshness of one part of the snapshot plus the outcome of its latest attempt.</summary>
public sealed record WeatherPartStatus(WeatherAvailability Availability, DateTimeOffset? CheckedAt, DateTimeOffset? LastAttemptAt, WeatherFailureKind LastFailure)
{
    /// <summary>True when the most recent attempt failed (any shown value is last-known-good).</summary>
    public bool LastRefreshFailed => LastFailure != WeatherFailureKind.None;
}

/// <summary>
/// What a weather surface may show at one instant, built only from the local cache (no network). Forecast,
/// observation and alerts are separate parts with separate states, so a forecast can never stand in for an
/// observation. A value whose part is <see cref="WeatherAvailability.Unavailable"/> is null.
/// </summary>
public sealed record WeatherSnapshot(
    DateTimeOffset BuiltAt,
    bool RetrievalPermitted,
    WeatherPartStatus ForecastStatus,
    WeatherForecast? Forecast,
    WeatherPartStatus HourlyStatus,
    HourlyWeatherForecast? Hourly,
    WeatherPartStatus ObservationStatus,
    WeatherObservation? Observation,
    WeatherPartStatus AlertsStatus,
    IReadOnlyList<WeatherAlert> ActiveAlerts)
{
    /// <summary>
    /// Phase 37C: for each active alert id, the ids of the reference points whose NWS point query returned it (or
    /// a message it replaces), in <see cref="WeatherLocation.Points"/> order — which portion of the park it
    /// applies to. Never widened beyond what NWS returned for a point.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AlertCoverage { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
}

/// <summary>A forecast day in Eastern time: the daytime period's temperature as the high, the following night's as the low.</summary>
public sealed record WeatherDailySummary(
    DateOnly Date,
    int? HighF,
    int? LowF,
    int? MaxPrecipitationChancePercent,
    string? DaytimeForecast,
    string? NighttimeForecast);

public static class WeatherSnapshotBuilder
{
    public static WeatherSnapshot Build(WeatherCacheDocument document, DateTimeOffset now, WeatherRefreshPolicy policy, WeatherLocation location)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(location);

        var forecastStatus = ForecastStatus(document.Forecast, now, policy);
        var forecast = forecastStatus.Availability is WeatherAvailability.Fresh or WeatherAvailability.Stale && document.Forecast.Value is { } f
            ? f with { Periods = f.Periods.Where(period => period.End > now).ToArray() }
            : null;

        var hourlyStatus = ForecastStatus(document.Hourly, now, policy);
        var hourly = hourlyStatus.Availability is WeatherAvailability.Fresh or WeatherAvailability.Stale && document.Hourly.Value is { } h
            ? h with { Periods = h.Periods.Where(period => period.End > now).ToArray() }
            : null;

        var (observationStatus, observation) = BestObservation(document, now, policy);
        var (alertsStatus, alerts) = Alerts(document, now, policy, location);

        return new WeatherSnapshot(now, location.IsRetrievalPermitted, forecastStatus, forecast, hourlyStatus, hourly, observationStatus, observation, alertsStatus, alerts)
        {
            AlertCoverage = AlertCoverage(document, alerts, location),
        };
    }

    /// <summary>Daily high/low by Eastern date. Today's high is null once the forecast starts at "Tonight"; it is never filled from another source.</summary>
    public static IReadOnlyList<WeatherDailySummary> SummarizeDays(WeatherForecast forecast)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        return forecast.Periods
            .GroupBy(period => DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(period.Start).DateTime))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var day = group.FirstOrDefault(period => period.IsDaytime);
                var night = group.FirstOrDefault(period => !period.IsDaytime);
                var chances = group.Select(period => period.PrecipitationChancePercent).Where(chance => chance is not null).ToArray();
                return new WeatherDailySummary(
                    group.Key,
                    day?.TemperatureF,
                    night?.TemperatureF,
                    chances.Length == 0 ? null : chances.Max(),
                    day?.ShortForecast,
                    night?.ShortForecast);
            })
            .ToArray();
    }

    /// <summary>The largest hourly precipitation chance in [<paramref name="from"/>, <paramref name="to"/>), or null when no hour in range has one.</summary>
    public static int? MaxPrecipitationChance(HourlyWeatherForecast hourly, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(hourly);
        var chances = hourly.Periods
            .Where(period => period.Start < to && period.End > from && period.PrecipitationChancePercent is not null)
            .Select(period => period.PrecipitationChancePercent!.Value)
            .ToArray();
        return chances.Length == 0 ? null : chances.Max();
    }

    private static WeatherPartStatus ForecastStatus<T>(WeatherEndpointState<T> state, DateTimeOffset now, WeatherRefreshPolicy policy)
        where T : class
    {
        WeatherAvailability availability;
        if (state.Value is null || state.CheckedAt is null)
        {
            availability = state.LastAttemptAt is null ? WeatherAvailability.NeverRetrieved : WeatherAvailability.Unavailable;
        }
        else
        {
            var age = now - state.CheckedAt.Value;
            availability = age > policy.ForecastUnavailableAfter ? WeatherAvailability.Unavailable
                : age > policy.ForecastStaleAfter ? WeatherAvailability.Stale
                : WeatherAvailability.Fresh;
        }

        return new WeatherPartStatus(availability, state.CheckedAt, state.LastAttemptAt, state.LastFailure);
    }

    private static (WeatherPartStatus Status, WeatherObservation? Observation) BestObservation(WeatherCacheDocument document, DateTimeOffset now, WeatherRefreshPolicy policy)
    {
        var stationOrder = document.Stations.Value?.Select(station => station.Id).ToList() ?? [];
        var candidates = document.Observations
            .Where(pair => pair.Value.Value is not null)
            .OrderBy(pair => stationOrder.IndexOf(pair.Key) is var index && index >= 0 ? index : int.MaxValue)
            .Select(pair => (State: pair.Value, Observation: pair.Value.Value!))
            .Where(item => item.Observation.TemperatureF is not null && now - item.Observation.Timestamp <= policy.ObservationUnavailableAfter)
            .ToList();

        var lastAttempt = document.Observations.Values.Select(state => state.LastAttemptAt).Max();
        var lastFailure = document.Observations.Values.OrderByDescending(state => state.LastAttemptAt).Select(state => state.LastFailure).FirstOrDefault();

        if (candidates.Count == 0)
        {
            var availability = lastAttempt is null ? WeatherAvailability.NeverRetrieved : WeatherAvailability.Unavailable;
            return (new WeatherPartStatus(availability, null, lastAttempt, lastFailure), null);
        }

        // The nearest station while it is fresh; otherwise the most recent observation among the stations used.
        var chosen = candidates.FirstOrDefault(item => now - item.Observation.Timestamp <= policy.ObservationStaleAfter);
        if (chosen.Observation is null)
        {
            chosen = candidates.OrderByDescending(item => item.Observation.Timestamp).First();
        }

        var fresh = now - chosen.Observation.Timestamp <= policy.ObservationStaleAfter;
        return (new WeatherPartStatus(fresh ? WeatherAvailability.Fresh : WeatherAvailability.Stale, chosen.State.CheckedAt, lastAttempt, lastFailure), chosen.Observation);
    }

    private static Dictionary<string, IReadOnlyList<string>> AlertCoverage(WeatherCacheDocument document, IReadOnlyList<WeatherAlert> active, WeatherLocation location)
    {
        var coverage = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var alert in active)
        {
            // An Update that replaced an older message still covers the points that returned the older one.
            var ids = new HashSet<string>(alert.ReferenceIds, StringComparer.Ordinal) { alert.Id };
            coverage[alert.Id] = location.Points
                .Where(point => document.Alerts.TryGetValue(point.Id, out var state)
                    && state.Value is { } listed
                    && listed.Any(candidate => ids.Contains(candidate.Id)))
                .Select(point => point.Id)
                .ToArray();
        }

        return coverage;
    }

    private static (WeatherPartStatus Status, IReadOnlyList<WeatherAlert> Alerts) Alerts(WeatherCacheDocument document, DateTimeOffset now, WeatherRefreshPolicy policy, WeatherLocation location)
    {
        var states = location.Points
            .Select(point => document.Alerts.TryGetValue(point.Id, out var state) ? state : new WeatherEndpointState<IReadOnlyList<WeatherAlert>>())
            .ToArray();

        var active = WeatherAlertRules.ResolveActive(states.SelectMany(state => state.Value ?? []), now);
        var lastAttempt = states.Select(state => state.LastAttemptAt).Max();
        var lastFailure = states.OrderByDescending(state => state.LastAttemptAt).Select(state => state.LastFailure).FirstOrDefault();

        // Coverage needs every point: the oldest point's check is the alert check time.
        WeatherAvailability availability;
        DateTimeOffset? checkedAt = null;
        if (states.Length == 0 || states.Any(state => state.CheckedAt is null))
        {
            availability = lastAttempt is null ? WeatherAvailability.NeverRetrieved : WeatherAvailability.Unavailable;
        }
        else
        {
            checkedAt = states.Min(state => state.CheckedAt!.Value);
            availability = now - checkedAt.Value > policy.AlertCheckStaleAfter ? WeatherAvailability.Stale : WeatherAvailability.Fresh;
        }

        return (new WeatherPartStatus(availability, checkedAt, lastAttempt, lastFailure), active);
    }
}
