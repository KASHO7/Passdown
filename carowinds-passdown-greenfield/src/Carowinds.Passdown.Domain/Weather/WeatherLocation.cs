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
/// Carowinds' two weather reference points, one on each side of the state line (spec §6.6, decision D8),
/// verified 2026-11-27 (docs/phase37b-weather-retrieval-and-cache.md, "D8 verification"):
/// NC 35.1050, −80.9420 is inside Mecklenburg County parcel PID 20315208 (CAROWINDS LLC, 14523 Carowinds Blvd,
/// land use "Rec Area"), 175 m from the parcel edge and 189 m from the Census state line, in the official park
/// map's Carousel Park / Thrill Zone area; SC 35.1002, −80.9420 is inside York County parcel 7230000001 (CAROWINDS
/// LLC, "CAROWINDS (219.73AC)"), 230 m from the parcel edge and 229 m from the state line, in the official park
/// map's Aeronautica area. NWS <c>/points</c> returns NCZ071/NCC119 and SCZ009/SCC091 respectively. Do not move a
/// point without repeating that verification.
/// </summary>
public static class CarowindsWeatherLocation
{
    public static WeatherReferencePoint NorthCarolina { get; } = new("carowinds-nc", "Carowinds (North Carolina side)", 35.1050m, -80.9420m, "NC");

    public static WeatherReferencePoint SouthCarolina { get; } = new("carowinds-sc", "Carowinds (South Carolina side)", 35.1002m, -80.9420m, "SC");

    public static WeatherLocation Park { get; } = new(
        [NorthCarolina, SouthCarolina],
        WeatherReferenceVerification.Verified,
        "Verified 2026-11-27 against Mecklenburg and York County parcel records, Census boundaries, the official park map and NWS (Phase 37 decision D8).");
}
