using System.Globalization;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Weather;

/// <summary>Source attribution every weather surface shows (spec §6.6): the provider's name and its public forecast page.</summary>
public static class WeatherAttribution
{
    public const string ProviderName = "National Weather Service";

    /// <summary>The NWS point-forecast page for people (not the API).</summary>
    public static Uri ForecastPage(WeatherReferencePoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var lat = decimal.Round(point.Latitude, 4).ToString("0.0000", CultureInfo.InvariantCulture);
        var lon = decimal.Round(point.Longitude, 4).ToString("0.0000", CultureInfo.InvariantCulture);
        return new Uri($"https://forecast.weather.gov/MapClick.php?lat={lat}&lon={lon}");
    }
}
