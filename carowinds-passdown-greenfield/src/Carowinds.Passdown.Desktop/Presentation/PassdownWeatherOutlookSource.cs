using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Documents.Associate;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Phase 37F: the one place the Passdown turns the weather monitor's current snapshot into the PDF Weather Outlook for
/// a captured workspace. Pure: reads an immutable, already-cached <see cref="WeatherSnapshot"/> (never the network),
/// selects the forecast for that workspace's own operating date and effective park hours, and words it. Null whenever
/// no valid forecast exists, so the PDF simply prints no weather.
/// </summary>
public static class PassdownWeatherOutlookSource
{
    public static PdfWeatherOutlook? For(WeatherSnapshot? snapshot, AssociateWorkspace workspace, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (snapshot is null)
        {
            return null;
        }

        var selection = OperatingHoursForecastSelector.Select(snapshot, workspace.OperatingDate, workspace.ParkHours.Effective, now);
        return PdfWeatherOutlookBuilder.Build(selection.Forecast, now);
    }
}
