using System.Text.Json;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Infrastructure.Weather.Nws;

/// <summary>Parses <c>/points/{lat},{lon}</c>. Every link must be an api.weather.gov address.</summary>
public static class NwsPointParser
{
    public static WeatherPointMetadata Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var p = NwsJson.Properties(document.RootElement);
        var gridId = NwsJson.String(p, "gridId") ?? throw new NwsFormatException("Point has no gridId.");
        var gridX = NwsJson.Int(p, "gridX") ?? throw new NwsFormatException("Point has no gridX.");
        var gridY = NwsJson.Int(p, "gridY") ?? throw new NwsFormatException("Point has no gridY.");

        return new WeatherPointMetadata(
            gridId,
            gridX,
            gridY,
            NwsJson.ApiUri(p, "forecast"),
            NwsJson.ApiUri(p, "forecastHourly"),
            NwsJson.ApiUri(p, "observationStations"),
            NwsJson.LastSegment(NwsJson.String(p, "forecastZone")),
            NwsJson.LastSegment(NwsJson.String(p, "county")),
            NwsJson.String(p, "timeZone"));
    }
}

/// <summary>
/// Parses the 12-hour and hourly forecasts. Accepts a temperature as a number with <c>temperatureUnit</c> or as
/// a quantity object (the <c>forecast_temperature_qv</c> form). Periods without valid start/end times are
/// skipped; a forecast with no usable period is a format error.
/// </summary>
public static class NwsForecastParser
{
    public static WeatherForecast ParseTwelveHour(string json, WeatherPointMetadata grid)
    {
        using var document = JsonDocument.Parse(json);
        var p = NwsJson.Properties(document.RootElement);
        var periods = new List<ForecastPeriod>();
        foreach (var period in NwsJson.Array(p, "periods"))
        {
            if (NwsJson.Time(period, "startTime") is not { } start || NwsJson.Time(period, "endTime") is not { } end || end <= start)
            {
                continue;
            }

            periods.Add(new ForecastPeriod(
                NwsJson.Int(period, "number") ?? periods.Count + 1,
                NwsJson.String(period, "name") ?? string.Empty,
                start,
                end,
                NwsJson.Bool(period, "isDaytime") ?? false,
                Temperature(period),
                NwsJson.Percent(NwsJson.Quantity(period, "probabilityOfPrecipitation")),
                NwsJson.String(period, "windSpeed"),
                NwsJson.String(period, "windDirection"),
                NwsJson.String(period, "shortForecast"),
                NwsJson.String(period, "detailedForecast")));
        }

        if (periods.Count == 0)
        {
            throw new NwsFormatException("Forecast has no usable periods.");
        }

        return new WeatherForecast(grid.GridId, grid.GridX, grid.GridY, NwsJson.Time(p, "updateTime"), periods.OrderBy(period => period.Start).ToArray());
    }

    public static HourlyWeatherForecast ParseHourly(string json, WeatherPointMetadata grid)
    {
        using var document = JsonDocument.Parse(json);
        var p = NwsJson.Properties(document.RootElement);
        var periods = new List<HourlyForecastPeriod>();
        foreach (var period in NwsJson.Array(p, "periods"))
        {
            if (NwsJson.Time(period, "startTime") is not { } start || NwsJson.Time(period, "endTime") is not { } end || end <= start)
            {
                continue;
            }

            var dewpoint = NwsJson.Fahrenheit(NwsJson.Quantity(period, "dewpoint"));
            periods.Add(new HourlyForecastPeriod(
                start,
                end,
                NwsJson.Bool(period, "isDaytime") ?? false,
                Temperature(period),
                NwsJson.Percent(NwsJson.Quantity(period, "probabilityOfPrecipitation")),
                NwsJson.Percent(NwsJson.Quantity(period, "relativeHumidity")),
                dewpoint is { } d ? Math.Round(d, 1) : null,
                NwsJson.String(period, "windSpeed"),
                NwsJson.String(period, "windDirection"),
                NwsJson.String(period, "shortForecast")));
        }

        if (periods.Count == 0)
        {
            throw new NwsFormatException("Hourly forecast has no usable periods.");
        }

        return new HourlyWeatherForecast(grid.GridId, grid.GridX, grid.GridY, NwsJson.Time(p, "updateTime"), periods.OrderBy(period => period.Start).ToArray());
    }

    private static int? Temperature(JsonElement period)
    {
        if (!period.TryGetProperty("temperature", out var temperature))
        {
            return null;
        }

        double? fahrenheit = temperature.ValueKind switch
        {
            JsonValueKind.Number when temperature.TryGetDouble(out var value) => NwsJson.String(period, "temperatureUnit") switch
            {
                "F" => value,
                "C" => WeatherUnits.CelsiusToFahrenheit(value),
                _ => null,
            },
            JsonValueKind.Object => NwsJson.Fahrenheit(NwsJson.Quantity(period, "temperature")),
            _ => null,
        };

        return fahrenheit is { } f ? (int)Math.Round(f, MidpointRounding.AwayFromZero) : null;
    }
}

/// <summary>Parses <c>/gridpoints/{wfo}/{x},{y}/stations</c>, keeping NWS's order (nearest to the grid point first).</summary>
public static class NwsStationParser
{
    public static IReadOnlyList<WeatherStation> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var stations = new List<WeatherStation>();
        foreach (var feature in NwsJson.Array(document.RootElement, "features"))
        {
            if (!feature.TryGetProperty("properties", out var p)
                || NwsJson.String(p, "stationIdentifier") is not { } id
                || !IsStationId(id)
                || !feature.TryGetProperty("geometry", out var geometry)
                || NwsJson.Array(geometry, "coordinates").ToArray() is not [{ ValueKind: JsonValueKind.Number } lon, { ValueKind: JsonValueKind.Number } lat, ..])
            {
                continue;
            }

            stations.Add(new WeatherStation(id, NwsJson.String(p, "name") ?? id, lat.GetDouble(), lon.GetDouble()));
        }

        if (stations.Count == 0)
        {
            throw new NwsFormatException("Station list has no usable stations.");
        }

        return stations;
    }

    /// <summary>Station identifiers are short upper-case letters and digits (KCLT); anything else is never put in a request path.</summary>
    public static bool IsStationId(string id) =>
        id.Length is >= 3 and <= 8 && id.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'));
}

/// <summary>
/// Parses <c>/stations/{id}/observations/latest</c> into US units. Null and QC-rejected values stay null; heat
/// index and wind chill are taken only as NWS supplies them.
/// </summary>
public static class NwsObservationParser
{
    public static WeatherObservation Parse(string json, WeatherStation station, double? distanceKm)
    {
        using var document = JsonDocument.Parse(json);
        var p = NwsJson.Properties(document.RootElement);
        var timestamp = NwsJson.Time(p, "timestamp") ?? throw new NwsFormatException("Observation has no timestamp.");

        return new WeatherObservation(
            NwsJson.String(p, "stationId") ?? station.Id,
            NwsJson.String(p, "stationName") ?? station.Name,
            distanceKm,
            timestamp,
            NwsJson.String(p, "textDescription"),
            Round(NwsJson.Fahrenheit(NwsJson.Quantity(p, "temperature"))),
            Round(NwsJson.Fahrenheit(NwsJson.Quantity(p, "dewpoint"))),
            NwsJson.Percent(NwsJson.Quantity(p, "relativeHumidity")),
            Round(NwsJson.Mph(NwsJson.Quantity(p, "windSpeed"))),
            Round(NwsJson.Mph(NwsJson.Quantity(p, "windGust"))),
            NwsJson.Degrees(NwsJson.Quantity(p, "windDirection")),
            Round(NwsJson.Fahrenheit(NwsJson.Quantity(p, "heatIndex"))),
            Round(NwsJson.Fahrenheit(NwsJson.Quantity(p, "windChill"))));
    }

    private static double? Round(double? value) => value is { } v ? Math.Round(v, 1) : null;
}

/// <summary>Result of parsing an alert collection: the alerts plus how many entries were skipped as unusable.</summary>
public sealed record NwsAlertParseResult(IReadOnlyList<WeatherAlert> Alerts, int SkippedCount);

/// <summary>
/// Parses <c>/alerts/active</c>. Entries without an id or event are skipped (they cannot be deduplicated or
/// named). Status, message type and references are kept as published, so <see cref="WeatherAlertRules"/> can
/// drop test messages and apply Updates and Cancels.
/// </summary>
public static class NwsAlertParser
{
    public static NwsAlertParseResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            throw new NwsFormatException("Alert response has no features array.");
        }

        var alerts = new List<WeatherAlert>();
        var skipped = 0;
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var p) || NwsJson.String(p, "id") is not { } id || NwsJson.String(p, "event") is not { } eventName)
            {
                skipped++;
                continue;
            }

            var references = NwsJson.Array(p, "references")
                .Select(reference => NwsJson.String(reference, "identifier"))
                .OfType<string>()
                .ToArray();
            var zones = NwsJson.Array(p, "affectedZones")
                .Where(zone => zone.ValueKind == JsonValueKind.String)
                .Select(zone => NwsJson.LastSegment(zone.GetString()))
                .OfType<string>()
                .ToArray();
            var link = NwsJson.String(p, "@id") ?? NwsJson.String(feature, "id");
            var sourceUrl = link is not null && Uri.TryCreate(link, UriKind.Absolute, out var uri) && NwsHttpTransport.IsApiUri(uri) ? uri : null;

            alerts.Add(new WeatherAlert(
                id,
                eventName,
                NwsJson.String(p, "status") ?? "Unknown",
                NwsJson.String(p, "messageType") ?? "Unknown",
                NwsJson.String(p, "severity") ?? "Unknown",
                NwsJson.String(p, "urgency") ?? "Unknown",
                NwsJson.String(p, "certainty") ?? "Unknown",
                NwsJson.Time(p, "sent"),
                NwsJson.Time(p, "effective"),
                NwsJson.Time(p, "onset"),
                NwsJson.Time(p, "expires"),
                NwsJson.Time(p, "ends"),
                NwsJson.String(p, "areaDesc"),
                NwsJson.String(p, "headline"),
                NwsJson.String(p, "description"),
                NwsJson.String(p, "instruction"),
                NwsJson.String(p, "senderName"),
                references,
                zones,
                sourceUrl));
        }

        return new NwsAlertParseResult(alerts, skipped);
    }
}
