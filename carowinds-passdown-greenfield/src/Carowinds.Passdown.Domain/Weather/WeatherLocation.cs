using System.Globalization;

namespace Carowinds.Passdown.Domain.Weather;

/// <summary>Whether a fixed weather reference location has been accepted for live retrieval (Phase 37A decision D8).</summary>
public enum WeatherReferenceVerification
{
    /// <summary>The coordinates are candidates only; no retrieval may run for them.</summary>
    PendingVerification,

    /// <summary>The owner accepted the coordinates and their NWS alert applicability.</summary>
    Verified,
}

/// <summary>
/// One fixed point at the park used to ask NWS for forecasts and alerts. Coordinates are held as decimals
/// and sent with exactly four decimal places, the precision NWS <c>/points</c> uses.
/// </summary>
public sealed record WeatherReferencePoint(string Id, string Label, decimal Latitude, decimal Longitude, string ExpectedState)
{
    /// <summary>"35.1050,-80.9420" — the form used in <c>/points/{lat},{lon}</c> and <c>?point=</c>.</summary>
    public string ToNwsPointText() =>
        string.Create(CultureInfo.InvariantCulture, $"{decimal.Round(Latitude, 4):0.0000},{decimal.Round(Longitude, 4):0.0000}");
}

/// <summary>
/// The park's weather location: the reference points (the first one supplies the forecast grid) and whether
/// they have been accepted. Spec §6.6 requires coverage of both the North Carolina and South Carolina
/// portions of the property, so alerts are requested for every point.
/// </summary>
public sealed record WeatherLocation(IReadOnlyList<WeatherReferencePoint> Points, WeatherReferenceVerification Verification, string VerificationNote)
{
    /// <summary>The point whose NWS grid supplies the forecast and nearby stations.</summary>
    public WeatherReferencePoint ForecastPoint => Points[0];

    /// <summary>False until the reference points are verified: no weather request may be made before then.</summary>
    public bool IsRetrievalPermitted => Verification == WeatherReferenceVerification.Verified && Points.Count > 0;
}

/// <summary>
/// Carowinds' candidate weather reference points (Phase 37B). Both are inside the OpenStreetMap park outline,
/// the U.S. Census Bureau geocoder places them in Mecklenburg County, NC and York County, SC, and NWS
/// <c>/points</c> returns the matching NC and SC zones. They stay <see cref="WeatherReferenceVerification.PendingVerification"/>
/// because no authoritative park boundary confirms they are inside the property
/// (docs/phase37b-weather-retrieval-and-cache.md). Do not change the status without the owner's acceptance.
/// </summary>
public static class CarowindsWeatherLocation
{
    public static WeatherReferencePoint NorthCarolina { get; } = new("carowinds-nc", "Carowinds (North Carolina side)", 35.1050m, -80.9420m, "NC");

    public static WeatherReferencePoint SouthCarolina { get; } = new("carowinds-sc", "Carowinds (South Carolina side)", 35.1002m, -80.9420m, "SC");

    public static WeatherLocation Candidate { get; } = new(
        [NorthCarolina, SouthCarolina],
        WeatherReferenceVerification.PendingVerification,
        "Candidate points; in-park position not yet confirmed against an authoritative park boundary (Phase 37 decision D8).");
}
