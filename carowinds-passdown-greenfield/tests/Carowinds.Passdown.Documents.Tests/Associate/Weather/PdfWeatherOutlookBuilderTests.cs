using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Documents.Tests.Associate.Weather;

/// <summary>Phase 37F: deterministic summary wording from selected hourly forecasts (synthetic data only).</summary>
public class PdfWeatherOutlookBuilderTests
{
    private static readonly DateOnly Date = new(2026, 7, 18);
    private static readonly DateTimeOffset Open = new(2026, 7, 18, 16, 0, 0, TimeSpan.Zero); // 12 PM EDT
    private static readonly DateTimeOffset Issued = new(2026, 7, 18, 10, 5, 0, TimeSpan.Zero); // 6:05 AM EDT
    private static readonly DateTimeOffset Morning = new(2026, 7, 18, 11, 0, 0, TimeSpan.Zero); // 7 AM EDT

    private static OperatingHoursForecast Forecast(int hours, Func<int, int?> temperature, Func<int, int?> chance, Func<int, string?> text, DateTimeOffset? coverageStart = null, DateTimeOffset? issued = null, bool daytime = true) =>
        new(Date, Open, Open.AddHours(hours), coverageStart ?? Open,
            Enumerable.Range(0, hours).Select(i => new HourlyForecastPeriod(Open.AddHours(i), Open.AddHours(i + 1), daytime, temperature(i), chance(i), null, null, null, null, text(i))).ToArray(),
            issued ?? Issued, Morning);

    private static PdfWeatherOutlook Build(OperatingHoursForecast forecast) => PdfWeatherOutlookBuilder.Build(forecast, Morning)!;

    [Fact]
    public void StableConditions_RangeConditionAndMaximumHourlyChance()
    {
        var outlook = Build(Forecast(8, i => 70 + (i % 3), i => i == 4 ? 10 : 5, _ => "Sunny"));

        Assert.Equal(Date, outlook.OperatingDate);
        Assert.Equal("70–72°F", outlook.TemperatureText);
        Assert.Equal("Sunny", outlook.ConditionText);
        Assert.Equal(PdfWeatherIcon.Sun, outlook.Icon);
        Assert.Equal("Rain chance up to 10%", outlook.RainText); // the highest hourly value, labelled "up to"
        Assert.Equal("Rain up to 10%", outlook.ShortRainText);
        Assert.Equal("NWS forecast 6:05 AM", outlook.AttributionText);
        Assert.Equal("NWS 6:05 AM", outlook.ShortAttributionText);
    }

    [Fact]
    public void RainLater_NamesTheFirstHourReachingThirtyPercent_AsPossibleAfter()
    {
        // 12 PM–8 PM; dry until 6 PM, then showers.
        var outlook = Build(Forecast(8, i => 72 - i, i => i < 6 ? 10 : 40, i => i < 6 ? "Partly Cloudy" : "Chance Showers"));

        Assert.Equal("65–72°F", outlook.TemperatureText);
        Assert.Equal("Partly cloudy", outlook.ConditionText);
        Assert.Equal("Showers possible after 6 PM (up to 40%)", outlook.RainText);
        Assert.Equal("Rain up to 40%", outlook.ShortRainText);
    }

    [Fact]
    public void ThunderstormsLater_UseStorms()
    {
        var outlook = Build(Forecast(8, _ => 88, i => i < 5 ? 20 : 60, i => i < 5 ? "Mostly Sunny" : "Chance Showers And Thunderstorms"));

        Assert.Equal("Storms possible after 5 PM (up to 60%)", outlook.RainText);
        Assert.Equal("88°F", outlook.TemperatureText);
    }

    [Fact]
    public void WetFromTheStart_GivesNoTransitionTime()
    {
        var outlook = Build(Forecast(6, _ => 64, i => 70 - i, _ => "Rain Showers"));

        Assert.Equal("Rain chance up to 70%", outlook.RainText);
        Assert.DoesNotContain("after", outlook.RainText, StringComparison.Ordinal);
        Assert.Equal(PdfWeatherIcon.Rain, outlook.Icon);
    }

    [Fact]
    public void BriefBlipBelowThreshold_GivesNoInventedTime()
    {
        var outlook = Build(Forecast(8, _ => 75, i => i == 3 ? 25 : 10, _ => "Partly Sunny"));

        Assert.Equal("Rain chance up to 25%", outlook.RainText);
    }

    [Fact]
    public void ShortWindow_OneHour_HasOneTemperatureAndOneChance()
    {
        var outlook = Build(Forecast(1, _ => 65, _ => 20, _ => "Cloudy"));

        Assert.Equal("65°F", outlook.TemperatureText);
        Assert.Equal("Rain chance 20%", outlook.RainText);
        Assert.Equal("Rain 20%", outlook.ShortRainText);
        Assert.Equal(PdfWeatherIcon.Cloudy, outlook.Icon);
    }

    [Fact]
    public void NoMeaningfulRain_AllZero_SaysZeroWithoutUpTo()
    {
        var outlook = Build(Forecast(8, _ => 70, _ => 0, _ => "Sunny"));

        Assert.Equal("Rain chance 0%", outlook.RainText);
    }

    [Fact]
    public void MissingPrecipitation_InAnyHour_OmitsRainEntirely()
    {
        var outlook = Build(Forecast(8, _ => 70, i => i == 7 ? null : 10, _ => "Partly Cloudy"));

        Assert.Null(outlook.RainText);
        Assert.Null(outlook.ShortRainText);
        Assert.Equal("70°F", outlook.TemperatureText);
    }

    [Fact]
    public void MissingTemperature_InAnyHour_OmitsTemperature_NeverGuessesARange()
    {
        var outlook = Build(Forecast(8, i => i == 2 ? null : 70, _ => 10, _ => "Cloudy"));

        Assert.Null(outlook.TemperatureText);
        Assert.Equal("Cloudy", outlook.ConditionText);
    }

    [Fact]
    public void NoTemperatureAndNoCondition_ProducesNoOutlook()
    {
        Assert.Null(PdfWeatherOutlookBuilder.Build(Forecast(4, _ => null, _ => 10, _ => null), Morning));
        Assert.Null(PdfWeatherOutlookBuilder.Build(null, Morning));
    }

    [Fact]
    public void DominantCondition_IsTheOneCoveringMostHours_TiesGoToTheEarliest()
    {
        var mostly = Build(Forecast(8, _ => 70, _ => 10, i => i < 3 ? "Sunny" : "Mostly Cloudy"));
        var tie = Build(Forecast(4, _ => 70, _ => 10, i => i < 2 ? "Sunny" : "Cloudy"));

        Assert.Equal("Mostly cloudy", mostly.ConditionText);
        Assert.Equal(PdfWeatherIcon.Cloudy, mostly.Icon);
        Assert.Equal("Sunny", tie.ConditionText);
    }

    [Fact]
    public void LongCondition_HasACategoryShortForm()
    {
        var outlook = Build(Forecast(4, _ => 80, _ => 50, _ => "Chance Showers And Thunderstorms"));

        Assert.Equal("Chance showers and thunderstorms", outlook.ConditionText);
        Assert.Equal("Storms", outlook.ShortConditionText);
        Assert.Equal(PdfWeatherIcon.Thunderstorm, outlook.Icon);
    }

    [Fact]
    public void UnknownLongCondition_HasNoShortFormAndNoIcon()
    {
        var outlook = Build(Forecast(4, _ => 80, _ => 10, _ => "Areas Of Blowing Dust Later"));

        Assert.Null(outlook.ShortConditionText);
        Assert.Equal(PdfWeatherIcon.None, outlook.Icon);
    }

    [Theory]
    [InlineData("Clear", false, PdfWeatherIcon.Moon)]
    [InlineData("Mostly Clear", false, PdfWeatherIcon.PartlyCloudyNight)]
    [InlineData("Partly Cloudy", true, PdfWeatherIcon.PartlyCloudy)]
    [InlineData("Patchy Fog", true, PdfWeatherIcon.Fog)]
    [InlineData("Chance Light Snow", true, PdfWeatherIcon.Snow)]
    [InlineData("Freezing Drizzle", true, PdfWeatherIcon.Snow)]
    [InlineData("Slight Chance Rain Showers", true, PdfWeatherIcon.Rain)]
    [InlineData("Breezy", true, PdfWeatherIcon.None)]
    public void Icons_FollowTheCondition(string condition, bool daytime, PdfWeatherIcon expected) =>
        Assert.Equal(expected, Build(Forecast(3, _ => 50, _ => 10, _ => condition, daytime: daytime)).Icon);

    [Theory]
    [InlineData(-12, 5, "−12° to 5°F")]
    [InlineData(-3, -1, "−3° to −1°F")]
    [InlineData(99, 108, "99–108°F")]
    [InlineData(0, 0, "0°F")]
    public void ExtremeTemperatures_FormatClearly(int low, int high, string expected) =>
        Assert.Equal(expected, Build(Forecast(2, i => i == 0 ? low : high, _ => 0, _ => "Sunny")).TemperatureText);

    [Fact]
    public void ParkAlreadyOpen_AttributionStatesTheCoveredPeriod()
    {
        var outlook = Build(Forecast(8, _ => 70, _ => 10, _ => "Sunny", coverageStart: Open.AddHours(3).AddMinutes(12)));

        Assert.Equal("NWS forecast 6:05 AM · covers 3:12 PM–8 PM", outlook.AttributionText);
        Assert.Equal("NWS 6:05 AM · 3:12 PM–8 PM", outlook.ShortAttributionText);
    }

    [Fact]
    public void IssueTimeOnAnotherDay_IncludesItsDate()
    {
        var outlook = Build(Forecast(8, _ => 70, _ => 10, _ => "Sunny", issued: new DateTimeOffset(2026, 7, 18, 2, 40, 0, TimeSpan.Zero)));

        Assert.Equal("NWS forecast Jul 17, 10:40 PM", outlook.AttributionText);
    }

    [Fact]
    public void NoIssueTime_SaysRetrieved_NeverPresentsItAsTheIssueTime()
    {
        var forecast = Forecast(8, _ => 70, _ => 10, _ => "Sunny") with { IssuedAt = null };

        var outlook = Build(forecast);

        Assert.Equal("NWS · retrieved 7:00 AM", outlook.AttributionText);
        Assert.DoesNotContain("forecast", outlook.AttributionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Wording_NeverMentionsAlertsWatchesOrWarnings()
    {
        var outlook = Build(Forecast(8, _ => 95, i => i < 4 ? 10 : 80, i => i < 4 ? "Hot" : "Severe Thunderstorms"));
        var all = string.Join(" ", outlook.TemperatureText, outlook.ConditionText, outlook.ShortConditionText, outlook.RainText, outlook.ShortRainText, outlook.AttributionText, outlook.ShortAttributionText);

        foreach (var word in new[] { "Alert", "Warning", "Watch", "Advisory", "▲" })
        {
            Assert.DoesNotContain(word, all, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SameForecast_SameWording_ValueEqual()
    {
        var forecast = Forecast(8, i => 70 + i, i => i * 5, _ => "Partly Sunny");

        Assert.Equal(Build(forecast), Build(forecast));
    }

    [Theory]
    [InlineData("Chance Showers And Thunderstorms", "Chance showers and thunderstorms")]
    [InlineData("Mostly Sunny", "Mostly sunny")]
    [InlineData("Patchy Fog then NWS Test", "Patchy fog then NWS test")]
    public void SentenceCase_KeepsAbbreviations(string input, string expected) =>
        Assert.Equal(expected, PdfWeatherOutlookBuilder.SentenceCase(input));
}
