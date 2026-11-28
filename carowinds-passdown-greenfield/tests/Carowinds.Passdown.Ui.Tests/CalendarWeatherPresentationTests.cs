using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Weather;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37D: the Calendar's selected-date Weather group, built only from a cached snapshot, the selected date's
/// operating state and effective hours, and the current instant. Synthetic data only (Saturday, October 17, 2026, EDT;
/// <see cref="WeatherUiFixtures"/>): hourly periods run 9 AM Oct 17 – 9 PM Oct 18 at 70°F, "Sunny".
/// </summary>
public class CalendarWeatherPresentationTests
{
    private static readonly DateOnly Today = WeatherUiFixtures.Day;
    private static readonly DateOnly Tomorrow = Today.AddDays(1);
    private static readonly DateTimeOffset Now = WeatherUiFixtures.Morning; // 9:02 AM EDT

    private static OperationsCalendarSelectedDateSummary Open(DateOnly date, int open, int close, int closeMinute = 0) =>
        Summary(date, OperationsCalendarOperatingState.Open, ParkHoursCalculator.Calculate(false, new TimeOnly(open, 0), new TimeOnly(close, closeMinute), false, null, null, false, null, null));

    private static OperationsCalendarSelectedDateSummary Closed(DateOnly date) =>
        Summary(date, OperationsCalendarOperatingState.Closed, ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null));

    private static OperationsCalendarSelectedDateSummary Unknown(DateOnly date) =>
        Summary(date, OperationsCalendarOperatingState.NotChecked, ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null));

    private static OperationsCalendarSelectedDateSummary Summary(DateOnly date, OperationsCalendarOperatingState state, EffectiveHours hours) =>
        new(date, date == Today, OperationsCalendarPassdownStatus.NoDraft, null, null, 0, state,
            new AssociateParkHoursView(hours, AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: state == OperationsCalendarOperatingState.Closed, OfficialOpening: hours.Opening, OfficialClosing: hours.Closing));

    private static WeatherSnapshot WithHourly(WeatherSnapshot snapshot, Func<int, HourlyForecastPeriod, HourlyForecastPeriod> change) =>
        snapshot with { Hourly = snapshot.Hourly! with { Periods = snapshot.Hourly.Periods.Select((period, i) => change(i, period)).ToArray() } };

    [Fact]
    public void Today_WithParkHours_DescribesThoseHours_WithTheDayHighLow_AndTheNwsSource()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Open(Today, 10, 22), Now);

        Assert.Equal(CalendarWeatherState.Forecast, weather.State);
        Assert.Equal("Park hours, 10 AM–10 PM", weather.ScopeText);
        Assert.Equal("Sunny", weather.SummaryText);
        Assert.Equal(PdfWeatherIcon.Sun, weather.SummaryIcon);
        Assert.Contains(weather.Rows, row => row is { Label: "Temperature", Value: "70°F" });
        Assert.Contains(weather.Rows, row => row is { Label: "High / low", Value: "74° / 55°" });
        // Some park hours carry no probability in the fixture: the maximum is given and the gap is said, never hidden.
        Assert.Contains(weather.Rows, row => row is { Label: "Rain", Value: "Up to 20%" });
        Assert.Equal("NWS gave no rain chance for some of these hours.", weather.CoverageNoteText);
        Assert.Equal(12, weather.Hours.Count); // 10 AM … 9 PM: the operating window only, not the whole day
        Assert.Equal("10 AM", weather.Hours[0].TimeText);
        Assert.Equal("9 PM", weather.Hours[^1].TimeText);
        Assert.Equal("Hourly forecast (12 hours)", weather.HoursHeaderText);
        Assert.False(weather.IsStale);
        Assert.StartsWith("National Weather Service forecast · issued", weather.SourceText);
        Assert.Contains("updated", weather.SourceText);
        Assert.Contains("National Weather Service", weather.AutomationText);
    }

    [Fact]
    public void Today_AfterOpening_CoversTheRestOfTheParkHours()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Open(Today, 8, 20), Now);

        Assert.Equal("Rest of park hours, 9 AM–8 PM", weather.ScopeText);
        Assert.Equal("9 AM", weather.Hours[0].TimeText); // the forecast hour in progress, never an earlier one
        Assert.Equal(11, weather.Hours.Count);
    }

    [Fact]
    public void Tomorrow_ShowsTomorrowsForecast_AndSaysWhenTheHourlyForecastEndsBeforeClosing()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Open(Tomorrow, 10, 22), Now);

        Assert.Equal(CalendarWeatherState.Forecast, weather.State);
        Assert.Equal("Park hours, 10 AM–10 PM", weather.ScopeText);
        Assert.Contains(weather.Rows, row => row is { Label: "Day", Value: "High 77°" }); // Sunday's daytime period; no low yet
        Assert.Equal(11, weather.Hours.Count); // 10 AM … 8 PM; the hourly data ends at 9 PM
        Assert.StartsWith("Hourly forecast covers 10 AM–9 PM only of the park hours (closing 10 PM).", weather.CoverageNoteText);
    }

    [Fact]
    public void UpcomingDate_BeyondTheHourlyForecast_UsesTheLabelledDayForecastOnly()
    {
        var snapshot = WeatherUiFixtures.Normal();
        var monday = new ForecastPeriod(5, "Monday", new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero), new(2026, 10, 19, 22, 0, 0, TimeSpan.Zero), true, 69, 60, "10 mph", "N", "Chance Showers And Thunderstorms", "Synthetic.");
        snapshot = snapshot with { Forecast = snapshot.Forecast! with { Periods = [.. snapshot.Forecast.Periods, monday] } };

        var weather = CalendarWeatherPresentation.Build(snapshot, Open(new DateOnly(2026, 10, 19), 10, 20), Now);

        Assert.Equal(CalendarWeatherState.Forecast, weather.State);
        Assert.Equal("Chance showers and thunderstorms", weather.SummaryText);
        Assert.Equal(PdfWeatherIcon.Thunderstorm, weather.SummaryIcon);
        Assert.Contains(weather.Rows, row => row is { Label: "Day", Value: "High 69°" });
        Assert.Contains(weather.Rows, row => row is { Label: "Rain", Value: "Up to 60%" });
        Assert.DoesNotContain(weather.Rows, row => row.Label == "Temperature"); // no hourly data: no park-hours range
        Assert.Equal("No hourly forecast for the park hours yet; the values are the NWS day forecast.", weather.CoverageNoteText);
        Assert.False(weather.HasHours);
    }

    [Fact]
    public void PastDate_ShowsNoWeather_NotTodaysForecast()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Open(Today.AddDays(-1), 10, 22), Now);

        Assert.Equal(CalendarWeatherState.PastDate, weather.State);
        Assert.Equal(CalendarWeatherPresentation.PastDateText, weather.MessageText);
        Assert.Empty(weather.Rows);
        Assert.Empty(weather.Hours);
        Assert.Empty(weather.AlertLines);
        Assert.Null(weather.SummaryText);
    }

    [Fact]
    public void DateBeyondTheForecastHorizon_SaysSo_AndNamesTheLastForecastDate()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Open(new DateOnly(2026, 10, 25), 10, 22), Now);

        Assert.Equal(CalendarWeatherState.BeyondHorizon, weather.State);
        Assert.Equal("No NWS forecast this far ahead. The current forecast runs through Sunday, Oct 18.", weather.MessageText);
        Assert.Empty(weather.Rows);
        Assert.Empty(weather.Hours);
    }

    [Fact]
    public void ClosedDate_IsAGeneralForecast_ClearlyNotParkHours()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Closed(Today), Now);

        Assert.Equal("Park closed · general forecast for the rest of the day", weather.ScopeText);
        Assert.Contains(weather.Rows, row => row is { Label: "Temperature", Value: "70°F" });
        Assert.Equal("9 AM", weather.Hours[0].TimeText);
        Assert.Equal("11 PM", weather.Hours[^1].TimeText); // the rest of the calendar day only
        Assert.DoesNotContain("Park hours,", weather.AutomationText);

        var tomorrowClosed = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Closed(Tomorrow), Now);
        Assert.Equal("Park closed · general forecast for the day", tomorrowClosed.ScopeText);
        Assert.Equal("12 AM", tomorrowClosed.Hours[0].TimeText);
    }

    [Fact]
    public void UnknownParkHours_AreNeverInvented()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Unknown(Tomorrow), Now);

        Assert.Equal("Park hours not confirmed · general forecast for the day", weather.ScopeText);
        Assert.DoesNotContain("Park hours,", weather.AutomationText);
        Assert.Equal(21, weather.Hours.Count); // 12 AM … 8 PM: what NWS has for the date, not an assumed window
    }

    [Fact]
    public void OvernightParkHours_RunPastMidnight_IntoTheNextDay()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Open(Today, 18, 1), Now);

        Assert.Equal("Park hours, 6 PM–1 AM (closes after midnight)", weather.ScopeText);
        Assert.Equal(new[] { "6 PM", "7 PM", "8 PM", "9 PM", "10 PM", "11 PM", "12 AM" }, weather.Hours.Select(hour => hour.TimeText));
    }

    [Fact]
    public void HoursThatHaveEnded_ShowTheRestOfTheDay_NeverPastHours()
    {
        var snapshot = WeatherUiFixtures.EveningForecast(); // 7:01 PM
        var weather = CalendarWeatherPresentation.Build(snapshot, Open(Today, 10, 18), WeatherUiFixtures.Evening);

        Assert.Equal("Park hours have ended · forecast for the rest of the day", weather.ScopeText);
        Assert.Equal("7 PM", weather.Hours[0].TimeText);
        Assert.Contains(weather.Rows, row => row is { Label: "Day", Value: "Low 55° (high not in forecast)" });
    }

    [Theory]
    [InlineData(10, 20, 10)] // Sunday, November 1, 2026: clocks fall back at 2 AM; park hours are still ten hours
    public void DstTransitionDay_UsesEasternWallClockTimes(int open, int close, int expectedHours)
    {
        var now = new DateTimeOffset(2026, 10, 31, 16, 0, 0, TimeSpan.Zero); // noon EDT on Oct 31
        var start = now;
        var periods = Enumerable.Range(0, 60)
            .Select(i => new HourlyForecastPeriod(start.AddHours(i), start.AddHours(i + 1), true, 60, 10, null, null, "5 mph", "N", "Cloudy"))
            .ToArray();
        var snapshot = WeatherUiFixtures.Normal(now) with
        {
            Forecast = null,
            HourlyStatus = WeatherUiFixtures.Fresh(now),
            Hourly = new HourlyWeatherForecast("GSP", 116, 59, now, periods),
        };
        var day = new DateOnly(2026, 11, 1);

        var operating = CalendarWeatherPresentation.Build(snapshot, Summary(day, OperationsCalendarOperatingState.Open,
            ParkHoursCalculator.Calculate(false, new TimeOnly(open, 0), new TimeOnly(close, 0), false, null, null, false, null, null)), now);
        Assert.Equal(expectedHours, operating.Hours.Count);
        Assert.Equal("10 AM", operating.Hours[0].TimeText);

        var closed = CalendarWeatherPresentation.Build(snapshot, Summary(day, OperationsCalendarOperatingState.Closed,
            ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null)), now);
        Assert.Equal(25, closed.Hours.Count); // the 25-hour day, with 1 AM twice
        Assert.Equal(2, closed.Hours.Count(hour => hour.TimeText == "1 AM"));
    }

    [Fact]
    public void MissingTemperatures_AreLeftOut_NeverShownAsZero()
    {
        var snapshot = WithHourly(WeatherUiFixtures.Normal(), (i, period) => i == 3 ? period with { TemperatureF = null } : period); // 12 PM
        var weather = CalendarWeatherPresentation.Build(snapshot, Open(Today, 10, 22), Now);

        Assert.DoesNotContain(weather.Rows, row => row.Label == "Temperature"); // a range over hours with a gap would be invented
        var noon = weather.Hours.Single(hour => hour.TimeText == "12 PM");
        Assert.False(noon.HasTemperature);
        Assert.Equal("—", noon.TemperatureText);
        Assert.Contains("temperature not given", noon.AccessibleText);
        Assert.DoesNotContain(weather.Hours, hour => hour.TemperatureText.StartsWith('0'));
    }

    [Fact]
    public void MissingPrecipitationValues_AreSaidInWords()
    {
        var snapshot = WithHourly(WeatherUiFixtures.Normal(), (_, period) => period with { PrecipitationChancePercent = null });
        var weather = CalendarWeatherPresentation.Build(snapshot, Open(Today, 10, 22), Now);

        Assert.Contains(weather.Rows, row => row is { Label: "Rain", Value: "Not given in the forecast" });
        Assert.All(weather.Hours, hour => Assert.False(hour.HasRain));
        Assert.All(weather.Hours, hour => Assert.Contains("rain chance not given", hour.AccessibleText));
    }

    [Fact]
    public void CompleteHourlyValues_UseThePdfWording()
    {
        var snapshot = WithHourly(WeatherUiFixtures.Normal(), (i, period) => period with
        {
            PrecipitationChancePercent = i < 6 ? 10 : 40,
            ShortForecast = i < 6 ? "Partly Sunny" : "Chance Showers",
            TemperatureF = 60 + i,
        });
        var weather = CalendarWeatherPresentation.Build(snapshot, Open(Today, 10, 22), Now);

        Assert.Contains(weather.Rows, row => row is { Label: "Temperature", Value: "61–72°F" });
        Assert.Contains(weather.Rows, row => row is { Label: "Rain", Value: "Showers possible after 3 PM (up to 40%)" });
        Assert.Null(weather.CoverageNoteText);
        Assert.Equal("Chance showers", weather.SummaryText);
        Assert.Equal(PdfWeatherIcon.Rain, weather.SummaryIcon);
    }

    [Fact]
    public void PartialHourlyCoverage_WithAGap_IsDescribedOnlyUpToTheGap()
    {
        var snapshot = WeatherUiFixtures.Normal();
        snapshot = snapshot with { Hourly = snapshot.Hourly! with { Periods = snapshot.Hourly.Periods.Where((_, i) => i != 6).ToArray() } }; // 3 PM missing

        var weather = CalendarWeatherPresentation.Build(snapshot, Open(Today, 10, 22), Now);

        Assert.StartsWith("Hourly forecast covers 10 AM–3 PM only of the park hours (closing 10 PM).", weather.CoverageNoteText);
        Assert.DoesNotContain(weather.Hours, hour => hour.TimeText == "3 PM"); // no interpolated hour
    }

    [Fact]
    public void StaleForecast_IsMarked_AndNeverPresentedAsCurrent()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Stale(), Open(Today, 10, 22), Now);

        Assert.Equal(CalendarWeatherState.Forecast, weather.State); // offline with a cached forecast: values stay, marked
        Assert.True(weather.IsStale);
        Assert.All(weather.Rows, row => Assert.True(row.IsStale));
        Assert.Contains("may be out of date, last updated 6:37 AM", weather.SourceText);
        Assert.DoesNotContain(" · updated", weather.SourceText);
        Assert.Contains("May be out of date.", weather.AutomationText);
    }

    [Fact]
    public void NoWeatherAvailable_SaysWhy_AndShowsNoValues()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Unavailable(), Open(Today, 10, 22), Now);

        Assert.Equal(CalendarWeatherState.Unavailable, weather.State);
        Assert.Equal("Weather unavailable. The National Weather Service could not be reached.", weather.MessageText);
        Assert.Empty(weather.Rows);
        Assert.Empty(weather.Hours);

        var providerError = WeatherUiFixtures.Unavailable() with { ForecastStatus = new WeatherPartStatus(WeatherAvailability.Unavailable, null, Now, WeatherFailureKind.ProviderError) };
        Assert.Contains("returned an error", CalendarWeatherPresentation.Build(providerError, Open(Today, 10, 22), Now).MessageText);

        var notSetUp = WeatherUiFixtures.Normal() with { RetrievalPermitted = false };
        Assert.Equal(CalendarWeatherState.Unavailable, CalendarWeatherPresentation.Build(notSetUp, Open(Today, 10, 22), Now).State);
    }

    [Fact]
    public void BeforeTheFirstRead_IsLoading()
    {
        Assert.Equal(CalendarWeatherState.Loading, CalendarWeatherPresentation.Build(null, Open(Today, 10, 22), Now).State);
        Assert.Equal(CalendarWeatherPresentation.LoadingText, CalendarWeatherPresentation.Build(WeatherUiFixtures.NeverRetrieved(), Open(Today, 10, 22), Now).MessageText);
    }

    [Fact]
    public void HourLabels_AreEasternTime_WithWindWhenGiven()
    {
        var weather = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Open(Today, 10, 12), Now);

        Assert.Equal(new[] { "10 AM", "11 AM" }, weather.Hours.Select(hour => hour.TimeText)); // 14:00Z and 15:00Z
        Assert.Equal("SW 5 mph", weather.Hours[0].WindText);
        Assert.Equal("70°", weather.Hours[0].TemperatureText);
        Assert.Equal("10 AM, 70 degrees, Sunny, rain chance not given, wind SW 5 mph", weather.Hours[0].AccessibleText); // NWS gave no value for 10 AM
        Assert.Equal("11 AM, 70 degrees, Sunny, rain chance 2 percent, wind SW 5 mph", weather.Hours[1].AccessibleText);
    }

    [Fact]
    public void NoWrongDateLeakage_EachDateUsesOnlyItsOwnHours()
    {
        var snapshot = WithHourly(WeatherUiFixtures.Normal(), (_, period) => period with { TemperatureF = period.Start.Day == 18 && period.Start.Hour >= 4 ? 80 : 60 });

        var today = CalendarWeatherPresentation.Build(snapshot, Open(Today, 10, 22), Now);
        var tomorrow = CalendarWeatherPresentation.Build(snapshot, Open(Tomorrow, 10, 20), Now);

        Assert.All(today.Hours, hour => Assert.Equal("60°", hour.TemperatureText));
        Assert.All(tomorrow.Hours, hour => Assert.Equal("80°", hour.TemperatureText));
        Assert.Contains(tomorrow.Rows, row => row is { Label: "Temperature", Value: "80°F" });
        Assert.NotEqual(today, tomorrow);
    }

    [Fact]
    public void Alerts_AppearOnlyForDatesTheirOwnTimesCover_AndOnlyFromACurrentCheck()
    {
        var snapshot = WeatherUiFixtures.TwoAlerts(); // both end on October 17 (Eastern)

        var today = CalendarWeatherPresentation.Build(snapshot, Open(Today, 10, 22), Now);
        Assert.Equal(2, today.AlertLines.Count);
        Assert.StartsWith("NWS alert: Wind Advisory · until", today.AlertLines[0]);
        Assert.Equal(CalendarWeatherPresentation.AlertsElsewhereText, today.AlertsNoteText);

        Assert.Empty(CalendarWeatherPresentation.Build(snapshot, Open(Tomorrow, 10, 22), Now).AlertLines); // not projected onto tomorrow

        // An alert reaching into tomorrow applies there too; one with no end time is never active (domain rule), so it is
        // shown on no date at all.
        var long1 = WeatherUiFixtures.Alert("synthetic-long", "Heat Advisory", "Moderate", Now.AddHours(-1), new DateTimeOffset(2026, 10, 18, 20, 0, 0, TimeSpan.Zero));
        var open = WeatherUiFixtures.Alert("synthetic-open", "Special Weather Statement", "Minor", Now.AddHours(-1), Now) with { Ends = null, Expires = null };
        var mixed = snapshot with { ActiveAlerts = [long1, open] };
        Assert.Equal(new[] { "Heat Advisory" }, CalendarWeatherPresentation.Build(mixed, Open(Tomorrow, 10, 22), Now).AlertLines.Select(line => line.Split(':')[1].Split('·')[0].Trim()));
        Assert.Equal(new[] { "Heat Advisory" }, CalendarWeatherPresentation.Build(mixed, Open(Today, 10, 22), Now).AlertLines.Select(line => line.Split(':')[1].Split('·')[0].Trim()));

        // A stale alert check is never read as "no alerts", and nothing is shown from it.
        var stale = snapshot with { AlertsStatus = new WeatherPartStatus(WeatherAvailability.Stale, Now.AddMinutes(-40), Now, WeatherFailureKind.Network) };
        Assert.Empty(CalendarWeatherPresentation.Build(stale, Open(Today, 10, 22), Now).AlertLines);
        Assert.DoesNotContain("No active", CalendarWeatherPresentation.Build(stale, Open(Today, 10, 22), Now).AutomationText);

        // An expired alert is never active.
        var expired = snapshot with { ActiveAlerts = [WeatherUiFixtures.Alert("synthetic-old", "Wind Advisory", "Moderate", Now.AddHours(-5), Now.AddHours(-1))] };
        Assert.Empty(CalendarWeatherPresentation.Build(expired, Open(Today, 10, 22), Now).AlertLines);
    }

    [Fact]
    public void SameInputs_BuildEqualPresentations_SoAnUnchangedReportNeverRerenders()
    {
        var first = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Open(Today, 10, 22), Now);
        var second = CalendarWeatherPresentation.Build(WeatherUiFixtures.Normal(), Open(Today, 10, 22), Now);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }
}
