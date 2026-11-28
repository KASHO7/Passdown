using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37C: the Dashboard Weather card's content for every approved state (D4 mock-up), from synthetic snapshots.
/// Observed, forecast and alert values stay separate and attributed; stale and unavailable are said in words; alert
/// status is "unknown", never "none", without a current check.
/// </summary>
public class DashboardWeatherPresentationTests
{
    private static readonly DashboardParkStatusSummary OpenTenToTen = Phase22CDashboardFixtures.VerifiedOperatingDay(WeatherUiFixtures.Day).ParkStatus;
    private static readonly DashboardParkStatusSummary Closed = Phase22CDashboardFixtures.ClosedPark(WeatherUiFixtures.Day).ParkStatus;

    private static DashboardWeatherPresentation Build(WeatherSnapshot? snapshot, DateTimeOffset? now = null, DashboardParkStatusSummary? park = null, DateOnly? operatingDate = null) =>
        DashboardWeatherPresentation.Build(snapshot, park ?? OpenTenToTen, operatingDate ?? WeatherUiFixtures.Day, now ?? WeatherUiFixtures.Morning);

    [Fact]
    public void Normal_ShowsObservedAndForecastSeparately_WithSourceAndTime()
    {
        var card = Build(WeatherUiFixtures.Normal());

        Assert.False(card.IsLoading);
        Assert.Equal("National Weather Service", card.HeaderMetaText);
        Assert.False(card.HeaderIsStale);
        Assert.False(card.HasStatusStrip);
        Assert.False(card.HasAlertStrip);

        Assert.Equal("OBSERVED · CHARLOTTE AIRPORT", card.ObservedKindText);
        Assert.Equal("68°F", card.TemperatureText);
        Assert.Equal("Partly Cloudy", card.ConditionText);
        Assert.Equal("Wind SW 9 mph", card.WindText);
        Assert.Equal("KCLT, 12 km from the park · 8:52 AM ET", card.ObservationSourceText);

        Assert.Equal("NWS FORECAST · TODAY, OCT 17", card.ForecastKindText);
        Assert.Equal("74°", card.HighText);
        Assert.Equal("55°", card.LowText);
        Assert.Equal("20%", card.RainChanceText);
        Assert.Equal("Highest hourly rain chance, 10 AM–10 PM park hours · issued 6:13 AM ET", card.ForecastSourceText);

        Assert.Equal("No active NWS alerts · checked 9:02 AM ET", card.FooterText);
        Assert.True(card.CanExpand);
        Assert.Contains("National Weather Service", card.AutomationText, StringComparison.Ordinal);
    }

    [Fact]
    public void Observation_NeverClaimsToBeAtThePark_AndNamesTheStationThatSuppliedIt()
    {
        var card = Build(WeatherUiFixtures.Normal());

        Assert.DoesNotContain("at Carowinds", card.ObservationSourceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not from inside Carowinds", card.ObservationNoteText, StringComparison.Ordinal);
        Assert.Contains("Charlotte airport station (KCLT)", card.ObservationNoteText, StringComparison.Ordinal);
        Assert.Contains(card.DetailRows, row => row.Label == "Humidity (KCLT)" && row.Value == "58%");
        Assert.Contains(card.DetailRows, row => row.Label == "Gusts (KCLT)" && row.Value == "Not reported");
    }

    [Fact]
    public void Observation_FromAnotherStation_IsLabelledWithThatStation_NotCharlotte()
    {
        var snapshot = WeatherUiFixtures.Normal() with { Observation = WeatherUiFixtures.Observation(WeatherUiFixtures.Morning.AddMinutes(-10), "KUZA", "Rock Hill, Rock Hill - York County Airport", 16.8) };

        var card = Build(snapshot);

        Assert.Equal("OBSERVED · ROCK HILL AIRPORT", card.ObservedKindText);
        Assert.Equal("KUZA, 17 km from the park · 8:52 AM ET", card.ObservationSourceText);
        Assert.DoesNotContain("Charlotte", card.ObservationNoteText, StringComparison.Ordinal);
    }

    [Fact]
    public void Observation_FromAnUnlistedStation_UsesTheNwsStationName()
    {
        var snapshot = WeatherUiFixtures.Normal() with { Observation = WeatherUiFixtures.Observation(WeatherUiFixtures.Morning.AddMinutes(-10), "KJQF", "Concord-Padgett Regional Airport", 30) };

        Assert.Equal("OBSERVED · CONCORD-PADGETT REGIONAL AIRPORT", Build(snapshot).ObservedKindText);
    }

    [Fact]
    public void NoObservation_ShowsUnavailable_AndNeverTheForecastTemperature()
    {
        var unavailable = new WeatherPartStatus(WeatherAvailability.Unavailable, null, WeatherUiFixtures.Morning, WeatherFailureKind.ProviderError);
        var snapshot = WeatherUiFixtures.Normal() with { Observation = null, ObservationStatus = unavailable };

        var card = Build(snapshot);

        Assert.Null(card.TemperatureText);
        Assert.True(card.ShowObservationUnavailable);
        Assert.Equal("OBSERVED", card.ObservedKindText);
        Assert.True(card.HasForecast);
        Assert.Equal("74°", card.HighText); // the forecast stays in its own section
        Assert.False(card.HasStatusStrip);
    }

    [Fact]
    public void Wind_CalmAndGusts()
    {
        var calm = Build(WeatherUiFixtures.Normal() with { Observation = WeatherUiFixtures.Observation(WeatherUiFixtures.Morning.AddMinutes(-10)) with { WindSpeedMph = 0.2 } });
        var gusty = Build(WeatherUiFixtures.Normal() with { Observation = WeatherUiFixtures.Observation(WeatherUiFixtures.Morning.AddMinutes(-10)) with { WindSpeedMph = 24, WindGustMph = 38 } });
        var none = Build(WeatherUiFixtures.Normal() with { Observation = WeatherUiFixtures.Observation(WeatherUiFixtures.Morning.AddMinutes(-10)) with { WindSpeedMph = null } });

        Assert.Equal("Wind calm", calm.WindText);
        Assert.Equal("Wind SW 24 mph, gusts 38", gusty.WindText);
        Assert.Contains(gusty.DetailRows, row => row.Label == "Gusts (KCLT)" && row.Value == "38 mph");
        Assert.Null(none.WindText);
    }

    [Fact]
    public void RainChance_CoversOnlyTheParksOperatingHours()
    {
        var hours = DashboardWeatherPresentation.PrecipitationWindow(OpenTenToTen, WeatherUiFixtures.Day, WeatherUiFixtures.Day, WeatherUiFixtures.Morning);

        Assert.Equal(new DateTimeOffset(2026, 10, 17, 14, 0, 0, TimeSpan.Zero), hours.From); // 10 AM EDT
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 2, 0, 0, TimeSpan.Zero), hours.To); // 10 PM EDT
        Assert.Equal("20%", Build(WeatherUiFixtures.Normal()).RainChanceText); // the 40% at 11 PM is after closing
    }

    [Fact]
    public void RainChance_OnAClosedDay_CoversTheRestOfToday_AndSaysSo()
    {
        var card = Build(WeatherUiFixtures.Normal(), park: Closed);

        Assert.Equal("40%", card.RainChanceText);
        Assert.StartsWith("Highest hourly rain chance, rest of today (park closed)", card.ForecastSourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void RainChance_WhenTheDashboardShowsAnotherOperatingDate_DoesNotUseThoseHours()
    {
        var card = Build(WeatherUiFixtures.Normal(), operatingDate: WeatherUiFixtures.Day.AddDays(-1));

        Assert.StartsWith("Highest hourly rain chance, rest of today (no park hours)", card.ForecastSourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void RainChance_WithoutHourlyForecast_IsNotAvailable_NotZero()
    {
        var card = Build(WeatherUiFixtures.Normal() with { Hourly = null });

        Assert.Null(card.RainChanceText);
        Assert.True(card.ShowRainChanceUnavailable);
    }

    [Fact]
    public void Evening_TodaysHighHasLeftTheForecast_IsSaid_AndNothingIsSubstituted()
    {
        var card = Build(WeatherUiFixtures.EveningForecast(), WeatherUiFixtures.Evening);

        Assert.Equal("NWS FORECAST · TONIGHT, OCT 17", card.ForecastKindText);
        Assert.Null(card.HighText);
        Assert.True(card.ShowHighNotInForecast);
        Assert.Equal("55°", card.LowText);
        Assert.Equal("5%", card.RainChanceText);
        Assert.Equal("Highest hourly rain chance, 7 PM–10 PM (rest of park hours) · issued 4:02 PM ET", card.ForecastSourceText);
        Assert.Contains("today's high not in current forecast", card.AutomationText, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterClosing_RainChanceCoversTheRestOfToday()
    {
        var lateNight = new DateTimeOffset(2026, 10, 18, 2, 30, 0, TimeSpan.Zero); // 10:30 PM EDT
        var window = DashboardWeatherPresentation.PrecipitationWindow(OpenTenToTen, WeatherUiFixtures.Day, WeatherUiFixtures.Day, lateNight);

        Assert.Equal("rest of today (after park hours)", window.Label);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 4, 0, 0, TimeSpan.Zero), window.To); // midnight EDT
    }

    [Fact]
    public void Alerts_MostSevereInTheStrip_EveryAlertWithItsPortionInTheDetails()
    {
        var card = Build(WeatherUiFixtures.TwoAlerts());

        Assert.Equal("2 active NWS alerts", card.AlertHeadline);
        Assert.Equal("Wind Advisory · until 8:00 PM ET", card.AlertPrimaryText);
        Assert.Equal("Also: Special Weather Statement (SC portion)", card.AlertSecondaryText);
        Assert.Equal("Alerts checked 9:02 AM ET", card.FooterText);

        Assert.Equal(2, card.Alerts.Count);
        Assert.Equal("Wind Advisory", card.Alerts[0].Event);
        Assert.Equal("Moderate · until 8:00 PM ET", card.Alerts[0].SeverityAndTimingText);
        Assert.Equal("Applies to NC and SC portions", card.Alerts[0].AppliesToText);
        Assert.Equal("NWS Greenville-Spartanburg SC, 6:02 AM ET", card.Alerts[0].SourceText);
        Assert.Equal("Applies to SC portion only", card.Alerts[1].AppliesToText);
        Assert.Equal("ACTIVE NWS ALERTS (2) · MOST SEVERE FIRST", card.AlertsHeading);
    }

    [Fact]
    public void Alerts_LinkOnlyToNwsPagesForThePointTheyApplyTo()
    {
        var card = Build(WeatherUiFixtures.TwoAlerts());

        Assert.All(card.Alerts, alert => Assert.True(Carowinds.Passdown.Application.Links.WeatherSourceLinkPolicy.TryValidate(alert.Link, out _)));
        Assert.Equal("https://forecast.weather.gov/MapClick.php?lat=35.1002&lon=-80.9420", card.Alerts[1].Link!.AbsoluteUri);
        Assert.True(Carowinds.Passdown.Application.Links.WeatherSourceLinkPolicy.TryValidate(card.ForecastPage, out _));
    }

    [Fact]
    public void SingleAlert_SaysWhichPortionItAppliesTo()
    {
        var two = WeatherUiFixtures.TwoAlerts();
        var card = Build(two with { ActiveAlerts = [two.ActiveAlerts[1]] });

        Assert.Equal("1 active NWS alert", card.AlertHeadline);
        Assert.Equal("Applies to SC portion only", card.AlertSecondaryText);
        Assert.Equal("ACTIVE NWS ALERT", card.AlertsHeading);
    }

    [Fact]
    public void ExpiredAlert_IsNeverShownAsActive()
    {
        var expired = WeatherUiFixtures.Alert("old", "Wind Advisory", "Moderate", WeatherUiFixtures.Morning.AddHours(-6), WeatherUiFixtures.Morning.AddMinutes(-1));
        var card = Build(WeatherUiFixtures.Normal() with { ActiveAlerts = [expired] });

        Assert.False(card.HasAlertStrip);
        Assert.Empty(card.Alerts);
        Assert.Equal("No active NWS alerts · checked 9:02 AM ET", card.FooterText);
    }

    [Fact]
    public void Stale_SaysSoInWords_ShowsAges_AndAlertStatusIsUnknown()
    {
        var card = Build(WeatherUiFixtures.Stale());

        Assert.Equal("May be out of date", card.HeaderMetaText);
        Assert.True(card.HeaderIsStale);
        Assert.Equal("Weather could not be refreshed.", card.StatusHeadline);
        Assert.Equal("Showing the last NWS data received.", card.StatusDetail);
        Assert.Equal("Alert status unknown — alerts last checked 6:37 AM ET", card.StatusAlertText);
        Assert.True(card.StatusIsStale);

        Assert.Equal("61°F", card.TemperatureText);
        Assert.True(card.ObservationIsStale);
        Assert.Equal("KCLT, 12 km from the park · 6:52 AM ET (2 h 10 min ago)", card.ObservationSourceText);
        Assert.True(card.ForecastIsStale);
        Assert.EndsWith("· last checked 6:37 AM ET", card.ForecastSourceText, StringComparison.Ordinal);

        Assert.Equal("Last updated 6:37 AM ET", card.FooterText);
        Assert.True(card.FooterIsStale);
        Assert.DoesNotContain("No active", card.FooterText, StringComparison.Ordinal);
        Assert.Contains(card.DetailRows, row => row.Label == "Alerts" && row.Value == "Not checked since 6:37 AM ET" && row.IsStale);
        Assert.Equal("Last attempt 9:02 AM ET failed (NWS did not respond). Retrying automatically.", card.LastUpdatedText);
    }

    [Fact]
    public void AlertsNotCheckedRecently_NeverSayNoActiveAlerts()
    {
        var staleAlerts = new WeatherPartStatus(WeatherAvailability.Stale, WeatherUiFixtures.Morning.AddMinutes(-40), WeatherUiFixtures.Morning.AddMinutes(-40), WeatherFailureKind.None);
        var card = Build(WeatherUiFixtures.Normal() with { AlertsStatus = staleAlerts });

        Assert.Equal("Weather has not been updated recently.", card.StatusHeadline);
        Assert.Equal("Alert status unknown — alerts last checked 8:22 AM ET", card.StatusAlertText);
        Assert.DoesNotContain("No active", card.FooterText, StringComparison.Ordinal);
        Assert.Contains("Alert status unknown.", card.AutomationText, StringComparison.Ordinal);
    }

    [Fact]
    public void AlertsUnavailableForOnePoint_StatusIsUnknown()
    {
        var unavailable = new WeatherPartStatus(WeatherAvailability.Unavailable, null, WeatherUiFixtures.Morning, WeatherFailureKind.Timeout);
        var card = Build(WeatherUiFixtures.Normal() with { AlertsStatus = unavailable });

        Assert.Equal("Alert status unknown — no successful check yet.", card.StatusAlertText);
        Assert.Contains(card.DetailRows, row => row.Label == "Alerts" && row.Value == "Status unknown");
    }

    [Fact]
    public void Unavailable_ShowsNoValues_AndSaysWhy()
    {
        var card = Build(WeatherUiFixtures.Unavailable());

        Assert.Equal("Weather unavailable.", card.StatusHeadline);
        Assert.Equal("The National Weather Service could not be reached.", card.StatusDetail);
        Assert.False(card.StatusIsStale);
        Assert.Equal("Alert status unknown — no successful check yet.", card.StatusAlertText);
        Assert.Null(card.TemperatureText);
        Assert.False(card.HasForecast);
        Assert.Null(card.HighText);
        Assert.Null(card.RainChanceText);
        Assert.Equal("Last attempt 9:02 AM ET", card.FooterText);
        Assert.Empty(card.DetailRows);
        Assert.True(card.CanExpand); // Refresh Weather stays reachable
        Assert.Equal("National Weather Service", card.HeaderMetaText);
    }

    [Fact]
    public void ProviderError_IsNotDescribedAsANetworkProblem()
    {
        var failed = new WeatherPartStatus(WeatherAvailability.Unavailable, null, WeatherUiFixtures.Morning, WeatherFailureKind.ProviderError);
        var snapshot = WeatherUiFixtures.Unavailable() with { ObservationStatus = failed, ForecastStatus = failed, AlertsStatus = failed };

        Assert.Equal("The National Weather Service returned an error.", Build(snapshot).StatusDetail);
    }

    [Fact]
    public void Loading_BeforeTheCacheIsRead_OrBeforeAnyCheck()
    {
        foreach (var card in new[] { Build(null), Build(WeatherUiFixtures.NeverRetrieved()) })
        {
            Assert.True(card.IsLoading);
            Assert.False(card.IsContentVisible);
            Assert.False(card.CanExpand);
            Assert.Equal("National Weather Service", card.HeaderMetaText);
            Assert.Contains("Alert status not yet checked.", card.AutomationText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CachedWeather_IsShownWithoutAnyRequest_FromTheSnapshotAlone()
    {
        // The builder takes a snapshot and the time only: it has no way to make a request.
        var parameters = typeof(DashboardWeatherPresentation).GetMethod(nameof(DashboardWeatherPresentation.Build))!.GetParameters();

        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(IWeatherMonitor) || parameter.ParameterType == typeof(WeatherRefreshCoordinator));
        Assert.Equal("68°F", Build(WeatherUiFixtures.Normal()).TemperatureText);
    }

    [Fact]
    public void SafetyNote_StatesInformationalOnly_AndTheAlertInterval()
    {
        Assert.Contains("Informational only, not a warning system.", DashboardWeatherPresentation.SafetyNoteText, StringComparison.Ordinal);
        Assert.Contains("about every 5 minutes while the app is open", DashboardWeatherPresentation.SafetyNoteText, StringComparison.Ordinal);
        Assert.Contains("Park weather procedures and official warning channels govern operations.", DashboardWeatherPresentation.SafetyNoteText, StringComparison.Ordinal);
    }
}
