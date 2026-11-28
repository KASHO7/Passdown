using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Weather;

/// <summary>
/// Keeps the weather cache small and honest: ended forecast periods, ended or end-less alerts and observations
/// from stations no longer used are dropped. When nothing of a forecast remains, its validators go too, so the
/// next request is unconditional rather than a "not modified" with nothing cached.
/// </summary>
public static class WeatherCacheRetention
{
    public static WeatherCacheDocument Prune(WeatherCacheDocument document, DateTimeOffset now, int observationStationCount)
    {
        ArgumentNullException.ThrowIfNull(document);

        var forecast = document.Forecast;
        if (forecast.Value is { } f)
        {
            var periods = f.Periods.Where(period => period.End > now).ToArray();
            forecast = periods.Length == 0
                ? forecast with { Value = null, ETag = null, LastModified = null }
                : forecast with { Value = f with { Periods = periods } };
        }

        var hourly = document.Hourly;
        if (hourly.Value is { } h)
        {
            var periods = h.Periods.Where(period => period.End > now).ToArray();
            hourly = periods.Length == 0
                ? hourly with { Value = null, ETag = null, LastModified = null }
                : hourly with { Value = h with { Periods = periods } };
        }

        var alerts = document.Alerts.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Value is { } list
                ? pair.Value with { Value = list.Where(alert => WeatherAlertRules.EndsAt(alert) is { } ends && ends > now).ToArray() }
                : pair.Value,
            StringComparer.Ordinal);

        var keptStations = new HashSet<string>(
            document.Stations.Value?.Take(observationStationCount).Select(station => station.Id) ?? [],
            StringComparer.Ordinal);
        var observations = document.Observations
            .Where(pair => keptStations.Count == 0 || keptStations.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return document with { Forecast = forecast, Hourly = hourly, Alerts = alerts, Observations = observations };
    }
}
