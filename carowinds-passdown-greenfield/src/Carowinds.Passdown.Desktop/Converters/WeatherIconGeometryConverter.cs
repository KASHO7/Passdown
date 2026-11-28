using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Carowinds.Passdown.Documents.Associate;

namespace Carowinds.Passdown.Desktop.Converters;

/// <summary>
/// Phase 37D: the Calendar Weather group's condition icons — the same locally defined 24-unit line icons the PDF
/// Weather Outlook prints (<c>PdfWeatherIcons</c>), as Avalonia geometries so they follow the theme's text colour
/// (Light and Dark) with no image file, font or emoji. Two layers per icon: <c>Back</c> (stroke only: sun, rays, drops)
/// and <c>Front</c> (a cloud, stroked and filled with the surface colour so it masks what it covers). The parameter
/// chooses the layer; an icon without that layer returns null. Geometries are parsed once and shared.
/// </summary>
public sealed class WeatherIconGeometryConverter : IValueConverter
{
    public static readonly WeatherIconGeometryConverter Instance = new();

    private const string Cloud = "M7 18h9.5a3.5 3.5 0 0 0 .4-6.98 5 5 0 0 0-9.65-1.15A3.5 3.5 0 0 0 7 18z";
    private const string LargeCloud = "M6.5 19h11a4 4 0 0 0 .6-7.96 6 6 0 0 0-11.6-1.38A4.5 4.5 0 0 0 6.5 19z";
    private const string RaisedCloud = "M6.5 15.5h11a4 4 0 0 0 .6-7.96 6 6 0 0 0-11.6-1.38A4.5 4.5 0 0 0 6.5 15.5z";

    private static readonly IReadOnlyDictionary<PdfWeatherIcon, (Geometry? Back, Geometry? Front)> Layers = new Dictionary<PdfWeatherIcon, (Geometry?, Geometry?)>
    {
        [PdfWeatherIcon.Sun] = (Parse("M16 12a4 4 0 1 1-8 0a4 4 0 1 1 8 0z M12 2v2.5M12 19.5V22M4.93 4.93l1.77 1.77M17.3 17.3l1.77 1.77M2 12h2.5M19.5 12H22M4.93 19.07l1.77-1.77M17.3 6.7l1.77-1.77"), null),
        [PdfWeatherIcon.Moon] = (Parse("M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z"), null),
        [PdfWeatherIcon.PartlyCloudy] = (Parse("M12 2v2M4.93 4.93l1.41 1.41M2 12h2M17.66 6.34l1.41-1.41 M10.5 7.5a4 4 0 0 1 5.4 3.5"), Parse(Cloud)),
        [PdfWeatherIcon.PartlyCloudyNight] = (Parse("M21 9.5A5 5 0 0 1 14.5 3a4 4 0 1 0 6.5 6.5z"), Parse(Cloud)),
        [PdfWeatherIcon.Cloudy] = (null, Parse(LargeCloud)),
        [PdfWeatherIcon.Rain] = (Parse("M8.5 18.5l-1 2.5M12.5 18.5l-1 2.5M16.5 18.5l-1 2.5"), Parse(RaisedCloud)),
        [PdfWeatherIcon.Thunderstorm] = (Parse("M13 16.5l-2 3h3l-2 3"), Parse(RaisedCloud)),
        [PdfWeatherIcon.Snow] = (Parse("M8.5 19.5h.01M12 19.5h.01M15.5 19.5h.01M10.25 22h.01M13.75 22h.01"), Parse(RaisedCloud)),
        [PdfWeatherIcon.Fog] = (Parse("M4 8h16M3 12h18M5 16h14M8 20h8"), null),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PdfWeatherIcon icon || !Layers.TryGetValue(icon, out var layers))
        {
            return null;
        }

        return string.Equals(parameter as string, "Front", StringComparison.Ordinal) ? layers.Front : layers.Back;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Geometry Parse(string data) => StreamGeometry.Parse(data);
}
