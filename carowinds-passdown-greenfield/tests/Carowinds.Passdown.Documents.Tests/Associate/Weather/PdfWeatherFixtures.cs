using Carowinds.Passdown.Documents.Associate;

namespace Carowinds.Passdown.Documents.Tests.Associate.Weather;

/// <summary>Phase 37F: synthetic Weather Outlooks for layout tests (never production values).</summary>
internal static class PdfWeatherFixtures
{
    public static PdfWeatherOutlook Normal(DateOnly date) => new(
        date, PdfWeatherIcon.PartlyCloudy, "63–74°F", "Partly cloudy", "Partly cloudy",
        "Rain chance up to 10%", "Rain up to 10%", "NWS forecast 6:05 AM", "NWS 6:05 AM");

    public static PdfWeatherOutlook RainLater(DateOnly date) => new(
        date, PdfWeatherIcon.Rain, "61–77°F", "Chance showers and thunderstorms", "Storms",
        "Storms possible after 5 PM (up to 60%)", "Rain up to 60%", "NWS forecast 6:05 AM", "NWS 6:05 AM");

    public static PdfWeatherOutlook LongCondition(DateOnly date) => new(
        date, PdfWeatherIcon.Thunderstorm, "−12° to 108°F", "Slight chance showers and thunderstorms then patchy dense fog and blowing dust", "Storms",
        "Storms possible after 11:30 PM (up to 100%)", "Rain up to 100%", "NWS forecast Dec 31, 11:59 PM · covers 11:59 PM–12:30 AM", "NWS Dec 31, 11:59 PM · 11:59 PM–12:30 AM");

    public static PdfWeatherOutlook NoTemperature(DateOnly date) => new(
        date, PdfWeatherIcon.Cloudy, null, "Cloudy", "Cloudy", "Rain chance up to 20%", "Rain up to 20%", "NWS forecast 6:05 AM", "NWS 6:05 AM");

    public static PdfWeatherOutlook NoRain(DateOnly date) => new(
        date, PdfWeatherIcon.Sun, "68–72°F", "Sunny", "Sunny", null, null, "NWS forecast 6:05 AM", "NWS 6:05 AM");
}
