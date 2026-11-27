using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Tests.Weather;

/// <summary>Synthetic weather values for coordinator and snapshot tests. The points are test-only and "verified" only here.</summary>
internal static class WeatherTestData
{
    public static readonly DateTimeOffset Start = new(2026, 7, 15, 13, 0, 0, TimeSpan.Zero); // 9:00 AM EDT

    public static readonly WeatherReferencePoint PointA = new("test-a", "Test A", 35.1050m, -80.9420m, "NC");
    public static readonly WeatherReferencePoint PointB = new("test-b", "Test B", 35.1002m, -80.9420m, "SC");

    public static WeatherLocation VerifiedLocation { get; } = new([PointA, PointB], WeatherReferenceVerification.Verified, "test only");

    public static WeatherLocation PendingLocation { get; } = new([PointA, PointB], WeatherReferenceVerification.PendingVerification, "test only");

    public static WeatherPointMetadata Grid(int x = 116, int y = 59) => new(
        "GSP",
        x,
        y,
        new Uri($"https://api.weather.gov/gridpoints/GSP/{x},{y}/forecast"),
        new Uri($"https://api.weather.gov/gridpoints/GSP/{x},{y}/forecast/hourly"),
        new Uri($"https://api.weather.gov/gridpoints/GSP/{x},{y}/stations"),
        "NCZ071",
        "NCC119",
        "America/New_York");

    public static WeatherForecast Forecast(DateTimeOffset from, int temperature = 88) => new(
        "GSP",
        116,
        59,
        from.AddHours(-1),
        [
            new ForecastPeriod(1, "Today", from, from.AddHours(9), true, temperature, 40, "5 mph", "S", "Chance Showers", null),
            new ForecastPeriod(2, "Tonight", from.AddHours(9), from.AddHours(21), false, 70, 20, "3 mph", "S", "Mostly Clear", null),
        ]);

    public static HourlyWeatherForecast Hourly(DateTimeOffset from) => new(
        "GSP",
        116,
        59,
        from.AddHours(-1),
        Enumerable.Range(0, 12).Select(i => new HourlyForecastPeriod(from.AddHours(i), from.AddHours(i + 1), true, 80 + i, i * 5, 60, 65, "5 mph", "S", "Sunny")).ToArray());

    public static IReadOnlyList<WeatherStation> Stations { get; } =
    [
        new("KCLT", "Charlotte/Douglas International Airport", 35.20833, -80.96139),
        new("KUZA", "Rock Hill - York County Airport", 34.98389, -81.05583),
        new("KAKH", "Gastonia Municipal Airport", 35.19687, -81.15545),
    ];

    public static WeatherObservation Observation(string stationId, DateTimeOffset timestamp, double? temperature = 85) =>
        new(stationId, stationId, 12, timestamp, "Partly Cloudy", temperature, 65, 55, 6.2, null, 200, 88, null);

    public static WeatherAlert Alert(string id, DateTimeOffset effective, DateTimeOffset ends, string severity = "Moderate") =>
        new(id, "Heat Advisory", "Actual", "Alert", severity, "Expected", "Likely", effective, effective, effective, ends, ends, "Mecklenburg; York", null, null, null, null, [], ["NCZ071"], null);
}
