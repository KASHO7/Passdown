namespace Carowinds.Passdown.Domain.Weather;

/// <summary>What the NWS <c>/points</c> lookup returned for one reference point: the forecast grid, links and zones.</summary>
public sealed record WeatherPointMetadata(
    string GridId,
    int GridX,
    int GridY,
    Uri ForecastUrl,
    Uri ForecastHourlyUrl,
    Uri ObservationStationsUrl,
    string? ForecastZone,
    string? County,
    string? TimeZone)
{
    public bool HasSameGrid(WeatherPointMetadata other) =>
        string.Equals(GridId, other.GridId, StringComparison.Ordinal) && GridX == other.GridX && GridY == other.GridY;
}

/// <summary>One 12-hour NWS forecast period ("Today", "Tonight", …). A forecast, never an observation.</summary>
public sealed record ForecastPeriod(
    int Number,
    string Name,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsDaytime,
    int? TemperatureF,
    int? PrecipitationChancePercent,
    string? WindSpeedText,
    string? WindDirection,
    string? ShortForecast,
    string? DetailedForecast);

/// <summary>One hourly NWS forecast period. A forecast, never an observation.</summary>
public sealed record HourlyForecastPeriod(
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsDaytime,
    int? TemperatureF,
    int? PrecipitationChancePercent,
    int? RelativeHumidityPercent,
    double? DewpointF,
    string? WindSpeedText,
    string? WindDirection,
    string? ShortForecast);

/// <summary>An NWS 12-hour forecast for one grid cell; <see cref="UpdateTime"/> is when NWS issued it.</summary>
public sealed record WeatherForecast(string GridId, int GridX, int GridY, DateTimeOffset? UpdateTime, IReadOnlyList<ForecastPeriod> Periods);

/// <summary>An NWS hourly forecast for one grid cell.</summary>
public sealed record HourlyWeatherForecast(string GridId, int GridX, int GridY, DateTimeOffset? UpdateTime, IReadOnlyList<HourlyForecastPeriod> Periods);

/// <summary>A nearby NWS observation station, in the order NWS lists them (nearest to the grid point first).</summary>
public sealed record WeatherStation(string Id, string Name, double Latitude, double Longitude);

/// <summary>
/// A station observation, converted to US units. Measured at the station, not at the park; every value is
/// null when NWS has none or flagged it as failing quality control. Heat index and wind chill are only the
/// values NWS supplies; the application never derives them.
/// </summary>
public sealed record WeatherObservation(
    string StationId,
    string StationName,
    double? DistanceKm,
    DateTimeOffset Timestamp,
    string? Description,
    double? TemperatureF,
    double? DewpointF,
    int? RelativeHumidityPercent,
    double? WindSpeedMph,
    double? WindGustMph,
    int? WindDirectionDegrees,
    double? HeatIndexF,
    double? WindChillF);

/// <summary>
/// One official NWS alert message (CAP fields as NWS publishes them). <see cref="ReferenceIds"/> lists the
/// earlier messages an Update or Cancel replaces.
/// </summary>
public sealed record WeatherAlert(
    string Id,
    string Event,
    string Status,
    string MessageType,
    string Severity,
    string Urgency,
    string Certainty,
    DateTimeOffset? Sent,
    DateTimeOffset? Effective,
    DateTimeOffset? Onset,
    DateTimeOffset? Expires,
    DateTimeOffset? Ends,
    string? AreaDescription,
    string? Headline,
    string? Description,
    string? Instruction,
    string? SenderName,
    IReadOnlyList<string> ReferenceIds,
    IReadOnlyList<string> AffectedZones,
    Uri? SourceUrl);
