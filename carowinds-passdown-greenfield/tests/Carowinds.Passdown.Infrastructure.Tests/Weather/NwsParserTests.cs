using System.Text.Json;
using Carowinds.Passdown.Domain.Weather;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Carowinds.Passdown.Infrastructure.Weather.Nws;

namespace Carowinds.Passdown.Infrastructure.Tests.Weather;

/// <summary>Parser contracts against the committed NWS fixtures (fixtures/weather/nws/README.md).</summary>
public class NwsParserTests
{
    private static string Fixture(string name) => FixtureFile.ReadText($"weather/nws/{name}");

    private static WeatherPointMetadata Grid => NwsPointParser.Parse(Fixture("points-nc-check-35.1037-80.9426-2026-11-27.json"));

    [Fact]
    public void Point_NorthCarolinaSide_ReturnsGridLinksAndMecklenburgZones()
    {
        var point = Grid;

        Assert.Equal(("GSP", 116, 59), (point.GridId, point.GridX, point.GridY));
        Assert.Equal("https://api.weather.gov/gridpoints/GSP/116,59/forecast", point.ForecastUrl.AbsoluteUri);
        Assert.Equal("https://api.weather.gov/gridpoints/GSP/116,59/forecast/hourly", point.ForecastHourlyUrl.AbsoluteUri);
        Assert.Equal("https://api.weather.gov/gridpoints/GSP/116,59/stations", point.ObservationStationsUrl.AbsoluteUri);
        Assert.Equal(("NCZ071", "NCC119", "America/New_York"), (point.ForecastZone, point.County, point.TimeZone));
    }

    [Fact]
    public void Point_SouthCarolinaSide_SameGridButYorkZones()
    {
        var point = NwsPointParser.Parse(Fixture("points-sc-check-35.09-80.94-2026-11-27.json"));

        Assert.True(point.HasSameGrid(Grid));
        Assert.Equal(("SCZ009", "SCC091"), (point.ForecastZone, point.County));
    }

    [Fact]
    public void Point_WithAForeignLink_IsRejected()
    {
        var json = Fixture("points-nc-check-35.1037-80.9426-2026-11-27.json")
            .Replace("https://api.weather.gov/gridpoints/GSP/116,59/forecast\"", "https://evil.example/forecast\"", StringComparison.Ordinal);

        Assert.Throws<NwsFormatException>(() => NwsPointParser.Parse(json));
    }

    [Fact]
    public void Point_WithoutGrid_IsRejected() =>
        Assert.Throws<NwsFormatException>(() => NwsPointParser.Parse("""{ "properties": { "gridX": 1, "gridY": 2 } }"""));

    [Fact]
    public void TwelveHourForecast_ParsesAllFourteenPeriods()
    {
        var forecast = NwsForecastParser.ParseTwelveHour(Fixture("forecast-gsp-116-59-2026-11-27.json"), Grid);

        Assert.Equal(14, forecast.Periods.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-11-27T11:13:44+00:00"), forecast.UpdateTime);
        var today = forecast.Periods[0];
        Assert.Equal(("Today", true, 51, 1), (today.Name, today.IsDaytime, today.TemperatureF, today.PrecipitationChancePercent));
        Assert.Equal(("16 to 21 mph", "SW", "Mostly Cloudy"), (today.WindSpeedText, today.WindDirection, today.ShortForecast));
        Assert.Equal(DateTimeOffset.Parse("2026-11-27T08:00:00-05:00"), today.Start);
        var last = forecast.Periods[^1];
        Assert.Equal(("Thursday Night", 50, 30), (last.Name, last.TemperatureF, last.PrecipitationChancePercent));
        Assert.Equal(DateTimeOffset.Parse("2026-12-04T06:00:00-05:00"), last.End);
    }

    [Fact]
    public void HourlyForecast_ConvertsDewpointAndKeepsHumidity()
    {
        var hourly = NwsForecastParser.ParseHourly(Fixture("forecast-hourly-gsp-116-59-2026-11-27-first30.json"), Grid);

        Assert.Equal(30, hourly.Periods.Count);
        var first = hourly.Periods[0];
        Assert.Equal((37, 1, 97), (first.TemperatureF, first.PrecipitationChancePercent, first.RelativeHumidityPercent));
        Assert.Equal(36.0, first.DewpointF);
        Assert.Equal("17 mph", first.WindSpeedText);
        Assert.Equal(TimeSpan.FromHours(1), first.End - first.Start);
    }

    [Fact]
    public void Forecast_TemperatureAsQuantity_UnknownUnitsAndBadPeriods()
    {
        const string json = """
            { "properties": { "updateTime": "2026-07-15T10:00:00+00:00", "periods": [
              { "number": 1, "name": "Today", "startTime": "2026-07-15T06:00:00-04:00", "endTime": "2026-07-15T18:00:00-04:00", "isDaytime": true,
                "temperature": { "unitCode": "wmoUnit:degC", "value": 30 }, "probabilityOfPrecipitation": { "unitCode": "wmoUnit:percent", "value": null } },
              { "number": 2, "name": "Tonight", "startTime": "2026-07-15T18:00:00-04:00", "endTime": "2026-07-16T06:00:00-04:00", "isDaytime": false,
                "temperature": 20, "temperatureUnit": "K" },
              { "number": 3, "name": "Broken", "startTime": "not a time", "endTime": "2026-07-16T18:00:00-04:00" }
            ] } }
            """;

        var forecast = NwsForecastParser.ParseTwelveHour(json, Grid);

        Assert.Equal(2, forecast.Periods.Count);
        Assert.Equal(86, forecast.Periods[0].TemperatureF);
        Assert.Null(forecast.Periods[0].PrecipitationChancePercent);
        Assert.Null(forecast.Periods[1].TemperatureF);
    }

    [Theory]
    [InlineData("""{ "properties": { "periods": [] } }""")]
    [InlineData("""{ "properties": { } }""")]
    [InlineData("""{ "type": "Feature" }""")]
    public void Forecast_WithoutUsablePeriods_IsRejected(string json) =>
        Assert.Throws<NwsFormatException>(() => NwsForecastParser.ParseTwelveHour(json, Grid));

    [Fact]
    public void Forecast_MalformedJson_Throws() =>
        Assert.ThrowsAny<JsonException>(() => NwsForecastParser.ParseHourly("{ not json", Grid));

    [Fact]
    public void Stations_KeepNwsOrder()
    {
        var stations = NwsStationParser.Parse(Fixture("stations-gsp-116-59-2026-11-27-first3.json"));

        Assert.Equal(["KCLT", "KUZA", "KAKH"], stations.Select(station => station.Id));
        Assert.Equal((35.20833, -80.96139), (stations[0].Latitude, stations[0].Longitude));
        Assert.Equal("Charlotte/Douglas International Airport", stations[0].Name);
    }

    [Theory]
    [InlineData("KCLT", true)]
    [InlineData("K1A5", true)]
    [InlineData("../x", false)]
    [InlineData("kclt", false)]
    [InlineData("AB", false)]
    public void StationIds_AreRestricted(string id, bool expected) => Assert.Equal(expected, NwsStationParser.IsStationId(id));

    [Fact]
    public void Observation_ConvertsToUsUnits_AndKeepsNullHeatIndex()
    {
        var station = new WeatherStation("KCLT", "Charlotte/Douglas International Airport", 35.20833, -80.96139);

        var observation = NwsObservationParser.Parse(Fixture("observation-latest-kclt-2026-11-27.json"), station, 11.6);

        Assert.Equal(DateTimeOffset.Parse("2026-11-27T13:10:00+00:00"), observation.Timestamp);
        Assert.Equal(("KCLT", "Cloudy", 11.6), (observation.StationId, observation.Description, observation.DistanceKm));
        Assert.Equal(41.0, observation.TemperatureF);
        Assert.Equal(33.8, observation.DewpointF);
        Assert.Equal(75, observation.RelativeHumidityPercent);
        Assert.Equal(13.8, observation.WindSpeedMph);
        Assert.Equal(20.7, observation.WindGustMph);
        Assert.Equal(220, observation.WindDirectionDegrees);
        Assert.Null(observation.HeatIndexF);
        Assert.Equal(33.5, observation.WindChillF);
    }

    [Fact]
    public void Observation_RejectsQualityControlFailuresAndUnknownUnits()
    {
        const string json = """
            { "properties": { "timestamp": "2026-07-15T17:52:00+00:00", "textDescription": "",
              "temperature": { "unitCode": "wmoUnit:degC", "value": 33, "qualityControl": "X" },
              "dewpoint": { "unitCode": "wmoUnit:degC", "value": 22, "qualityControl": "Q" },
              "windSpeed": { "unitCode": "wmoUnit:furlong_fortnight-1", "value": 10, "qualityControl": "V" },
              "relativeHumidity": { "unitCode": "wmoUnit:percent", "value": null, "qualityControl": "Z" },
              "heatIndex": { "unitCode": "wmoUnit:degC", "value": 38, "qualityControl": "V" } } }
            """;

        var observation = NwsObservationParser.Parse(json, new WeatherStation("KCLT", "KCLT", 0, 0), null);

        Assert.Null(observation.TemperatureF);
        Assert.Null(observation.DewpointF);
        Assert.Null(observation.WindSpeedMph);
        Assert.Null(observation.RelativeHumidityPercent);
        Assert.Null(observation.Description);
        Assert.Equal(100.4, observation.HeatIndexF);
    }

    [Fact]
    public void Observation_WithoutTimestamp_IsRejected() =>
        Assert.Throws<NwsFormatException>(() => NwsObservationParser.Parse("""{ "properties": { } }""", new WeatherStation("KCLT", "KCLT", 0, 0), null));

    [Fact]
    public void Alerts_None_ParsesToAnEmptyList()
    {
        var result = NwsAlertParser.Parse(Fixture("alerts-active-none-2026-11-27.json"));

        Assert.Empty(result.Alerts);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public void Alerts_Synthetic_ParsesFieldsAndSkipsAnEntryWithoutId()
    {
        var result = NwsAlertParser.Parse(Fixture("alerts-active-synthetic-multiple.json"));

        Assert.Equal(8, result.Alerts.Count);
        Assert.Equal(1, result.SkippedCount);
        var update = result.Alerts.Single(alert => alert.Id.EndsWith("svr.2", StringComparison.Ordinal));
        Assert.Equal(("Severe Thunderstorm Warning", "Update", "Severe", "Immediate", "Observed"), (update.Event, update.MessageType, update.Severity, update.Urgency, update.Certainty));
        Assert.Equal(["urn:oid:2.49.0.1.840.0.synthetic.svr.1"], update.ReferenceIds);
        Assert.Equal(["NCC119", "SCC091"], update.AffectedZones);
        Assert.Equal(DateTimeOffset.Parse("2026-07-15T15:00:00-04:00"), update.Ends);
        Assert.Equal("https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.synthetic.svr.2", update.SourceUrl!.AbsoluteUri);
    }

    [Fact]
    public void Alerts_Synthetic_ResolveToTheUpdatedWarningAndTheHeatAdvisory()
    {
        var alerts = NwsAlertParser.Parse(Fixture("alerts-active-synthetic-multiple.json")).Alerts;

        var active = WeatherAlertRules.ResolveActive(alerts, DateTimeOffset.Parse("2026-07-15T14:40:00-04:00"));

        Assert.Equal(["Severe Thunderstorm Warning", "Heat Advisory"], active.Select(alert => alert.Event));
        Assert.Equal("urn:oid:2.49.0.1.840.0.synthetic.svr.2", active[0].Id);

        Assert.Equal(["Heat Advisory"], WeatherAlertRules.ResolveActive(alerts, DateTimeOffset.Parse("2026-07-15T15:00:00-04:00")).Select(alert => alert.Event));
        Assert.Empty(WeatherAlertRules.ResolveActive(alerts, DateTimeOffset.Parse("2026-07-15T20:00:00-04:00")));
    }

    [Fact]
    public void Alerts_WithoutFeatures_AreRejected() =>
        Assert.Throws<NwsFormatException>(() => NwsAlertParser.Parse("""{ "type": "FeatureCollection" }"""));
}
