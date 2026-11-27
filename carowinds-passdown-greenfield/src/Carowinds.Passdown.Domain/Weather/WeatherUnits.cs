namespace Carowinds.Passdown.Domain.Weather;

/// <summary>Unit conversions and great-circle distance for weather values. NWS unit codes are WMO codes such as <c>wmoUnit:degC</c>.</summary>
public static class WeatherUnits
{
    public static double CelsiusToFahrenheit(double celsius) => celsius * 9.0 / 5.0 + 32.0;

    public static double KilometresPerHourToMph(double kmh) => kmh / 1.609344;

    public static double MetresPerSecondToMph(double ms) => ms * 3600.0 / 1609.344;

    /// <summary>Haversine distance in kilometres.</summary>
    public static double DistanceKm(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double EarthRadiusKm = 6371.0;
        var phi1 = DegreesToRadians(latitude1);
        var phi2 = DegreesToRadians(latitude2);
        var deltaPhi = DegreesToRadians(latitude2 - latitude1);
        var deltaLambda = DegreesToRadians(longitude2 - longitude1);
        var h = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2)
            + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
