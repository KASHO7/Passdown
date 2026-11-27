using Carowinds.Passdown.Application.Links;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Tests.Weather;

public class WeatherSnapshotAndPolicyTests
{
    private static readonly DateTimeOffset T0 = WeatherTestData.Start;
    private static readonly WeatherRefreshPolicy Policy = new();

    private static WeatherSnapshot Build(WeatherCacheDocument document, DateTimeOffset now) =>
        WeatherSnapshotBuilder.Build(document, now, Policy, WeatherTestData.VerifiedLocation);

    private static WeatherCacheDocument WithForecastCheckedAt(DateTimeOffset checkedAt) => new()
    {
        Forecast = new() { Value = WeatherTestData.Forecast(T0), CheckedAt = checkedAt, LastAttemptAt = checkedAt },
    };

    [Fact]
    public void EmptyCache_IsNeverRetrieved_Everywhere()
    {
        var snapshot = Build(WeatherCacheDocument.Empty, T0);

        Assert.Equal(WeatherAvailability.NeverRetrieved, snapshot.ForecastStatus.Availability);
        Assert.Equal(WeatherAvailability.NeverRetrieved, snapshot.HourlyStatus.Availability);
        Assert.Equal(WeatherAvailability.NeverRetrieved, snapshot.ObservationStatus.Availability);
        Assert.Equal(WeatherAvailability.NeverRetrieved, snapshot.AlertsStatus.Availability);
        Assert.Null(snapshot.Forecast);
        Assert.Null(snapshot.Observation);
        Assert.Empty(snapshot.ActiveAlerts);
    }

    [Theory]
    [InlineData(180, WeatherAvailability.Fresh)]
    [InlineData(181, WeatherAvailability.Stale)]
    [InlineData(1440, WeatherAvailability.Stale)]
    [InlineData(1441, WeatherAvailability.Unavailable)]
    public void ForecastAge_SetsAvailability(int minutesSinceCheck, WeatherAvailability expected)
    {
        var snapshot = Build(WithForecastCheckedAt(T0), T0.AddMinutes(minutesSinceCheck));

        Assert.Equal(expected, snapshot.ForecastStatus.Availability);
        Assert.Equal(expected == WeatherAvailability.Unavailable, snapshot.Forecast is null);
    }

    [Fact]
    public void EndedForecastPeriods_AreNotShown()
    {
        var snapshot = Build(WithForecastCheckedAt(T0), T0.AddHours(10));

        Assert.Equal(["Tonight"], snapshot.Forecast!.Periods.Select(period => period.Name));
    }

    [Theory]
    [InlineData(75, WeatherAvailability.Fresh)]
    [InlineData(76, WeatherAvailability.Stale)]
    [InlineData(180, WeatherAvailability.Stale)]
    [InlineData(181, WeatherAvailability.Unavailable)]
    public void ObservationAge_SetsAvailability(int minutesOld, WeatherAvailability expected)
    {
        var document = new WeatherCacheDocument
        {
            Stations = new() { Value = WeatherTestData.Stations, CheckedAt = T0 },
            Observations = new Dictionary<string, WeatherEndpointState<WeatherObservation>>
            {
                ["KCLT"] = new() { Value = WeatherTestData.Observation("KCLT", T0), CheckedAt = T0, LastAttemptAt = T0 },
            },
        };

        var snapshot = Build(document, T0.AddMinutes(minutesOld));

        Assert.Equal(expected, snapshot.ObservationStatus.Availability);
        Assert.Equal(expected == WeatherAvailability.Unavailable, snapshot.Observation is null);
    }

    [Fact]
    public void Observation_IsNeverTakenFromTheForecast()
    {
        var snapshot = Build(WithForecastCheckedAt(T0), T0);

        Assert.NotNull(snapshot.Forecast);
        Assert.Null(snapshot.Observation);
        Assert.Equal(WeatherAvailability.NeverRetrieved, snapshot.ObservationStatus.Availability);
    }

    [Fact]
    public void Alerts_NeedBothPointsChecked_AndExpireOnTime()
    {
        var alert = WeatherTestData.Alert("heat", T0.AddHours(-1), T0.AddHours(2));
        var onlyA = new WeatherCacheDocument
        {
            Alerts = new Dictionary<string, WeatherEndpointState<IReadOnlyList<WeatherAlert>>>
            {
                ["test-a"] = new() { Value = [alert], CheckedAt = T0, LastAttemptAt = T0 },
            },
        };

        var partial = Build(onlyA, T0);
        Assert.Equal(WeatherAvailability.Unavailable, partial.AlertsStatus.Availability);

        var both = onlyA with
        {
            Alerts = new Dictionary<string, WeatherEndpointState<IReadOnlyList<WeatherAlert>>>
            {
                ["test-a"] = new() { Value = [alert], CheckedAt = T0, LastAttemptAt = T0 },
                ["test-b"] = new() { Value = [alert], CheckedAt = T0.AddMinutes(-20), LastAttemptAt = T0 },
            },
        };

        var current = Build(both, T0);
        Assert.Single(current.ActiveAlerts);
        Assert.Equal(T0.AddMinutes(-20), current.AlertsStatus.CheckedAt);
        Assert.Equal(WeatherAvailability.Stale, current.AlertsStatus.Availability);

        var afterEnd = Build(both, T0.AddHours(2));
        Assert.Empty(afterEnd.ActiveAlerts);
    }

    [Fact]
    public void SummarizeDays_PairsDayHighWithFollowingNightLow_InEasternDates()
    {
        var forecast = WeatherTestData.Forecast(T0);

        var day = Assert.Single(WeatherSnapshotBuilder.SummarizeDays(forecast));

        Assert.Equal(new DateOnly(2026, 7, 15), day.Date);
        Assert.Equal(88, day.HighF);
        Assert.Equal(70, day.LowF);
        Assert.Equal(40, day.MaxPrecipitationChancePercent);
    }

    [Fact]
    public void SummarizeDays_ForecastStartingTonight_HasNoHighForToday()
    {
        var evening = new DateTimeOffset(2026, 7, 15, 22, 0, 0, TimeSpan.Zero); // 6 PM EDT
        var forecast = new WeatherForecast("GSP", 116, 59, evening, [new ForecastPeriod(1, "Tonight", evening, evening.AddHours(12), false, 71, 10, null, null, "Clear", null)]);

        var day = Assert.Single(WeatherSnapshotBuilder.SummarizeDays(forecast));

        Assert.Null(day.HighF);
        Assert.Equal(71, day.LowF);
    }

    [Fact]
    public void MaxPrecipitationChance_CoversOnlyTheRequestedHours()
    {
        var hourly = WeatherTestData.Hourly(T0);

        Assert.Equal(15, WeatherSnapshotBuilder.MaxPrecipitationChance(hourly, T0, T0.AddHours(4)));
        Assert.Null(WeatherSnapshotBuilder.MaxPrecipitationChance(hourly, T0.AddDays(2), T0.AddDays(3)));
    }

    [Fact]
    public void EndpointPolicy_AppliesFloorTargetExpiryCeilingAndBackoff()
    {
        var forecast = new WeatherEndpointPolicy(TimeSpan.FromMinutes(30), null, TimeSpan.FromHours(3));
        var alerts = new WeatherEndpointPolicy(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        Assert.Equal(DateTimeOffset.MinValue, forecast.DueAt(new WeatherEndpointState<WeatherForecast>()));
        Assert.Equal(T0.AddMinutes(30), forecast.DueAt(new WeatherEndpointState<WeatherForecast> { CheckedAt = T0, ExpiresAt = T0.AddMinutes(1) }));
        Assert.Equal(T0.AddHours(1), forecast.DueAt(new WeatherEndpointState<WeatherForecast> { CheckedAt = T0, ExpiresAt = T0.AddHours(1) }));
        Assert.Equal(T0.AddHours(3), forecast.DueAt(new WeatherEndpointState<WeatherForecast> { CheckedAt = T0, ExpiresAt = T0.AddDays(1) }));
        Assert.Equal(T0.AddHours(3), forecast.DueAt(new WeatherEndpointState<WeatherForecast> { CheckedAt = T0 }));
        Assert.Equal(T0.AddMinutes(5), alerts.DueAt(new WeatherEndpointState<WeatherForecast> { CheckedAt = T0, ExpiresAt = T0.AddSeconds(5) }));
        Assert.Equal(T0.AddMinutes(20), alerts.DueAt(new WeatherEndpointState<WeatherForecast> { CheckedAt = T0, NextAttemptNotBefore = T0.AddMinutes(20) }));

        Assert.Equal(TimeSpan.FromSeconds(30), Policy.BackoffAfter(1, null));
        Assert.Equal(TimeSpan.FromMinutes(2), Policy.BackoffAfter(2, null));
        Assert.Equal(TimeSpan.FromMinutes(10), Policy.BackoffAfter(3, null));
        Assert.Equal(TimeSpan.FromMinutes(30), Policy.BackoffAfter(9, null));
        Assert.Equal(TimeSpan.FromMinutes(45), Policy.BackoffAfter(1, TimeSpan.FromMinutes(45)));
    }

    [Fact]
    public void DefaultPolicy_MatchesTheApprovedRequestPolicy()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), Policy.Alerts.Target);
        Assert.Equal(TimeSpan.FromSeconds(60), Policy.Alerts.Floor);
        Assert.Equal(TimeSpan.FromHours(24), Policy.Point.Floor);
        Assert.Equal(TimeSpan.FromMinutes(30), Policy.Forecast.Floor);
        Assert.Equal(2, Policy.ObservationStationCount);
    }

    [Fact]
    public void Retention_DropsEndedDataAndClearsValidatorsOfAnEmptiedForecast()
    {
        var alerts = new IReadOnlyList<WeatherAlert>[] { [WeatherTestData.Alert("old", T0.AddHours(-5), T0.AddHours(-1)), WeatherTestData.Alert("now", T0.AddHours(-1), T0.AddHours(1))] };
        var document = new WeatherCacheDocument
        {
            Forecast = new() { Value = WeatherTestData.Forecast(T0.AddDays(-2)), ETag = "e", LastModified = T0, CheckedAt = T0 },
            Hourly = new() { Value = WeatherTestData.Hourly(T0.AddHours(-6)), CheckedAt = T0 },
            Stations = new() { Value = WeatherTestData.Stations, CheckedAt = T0 },
            Observations = new Dictionary<string, WeatherEndpointState<WeatherObservation>>
            {
                ["KCLT"] = new() { Value = WeatherTestData.Observation("KCLT", T0) },
                ["KAKH"] = new() { Value = WeatherTestData.Observation("KAKH", T0) },
            },
            Alerts = new Dictionary<string, WeatherEndpointState<IReadOnlyList<WeatherAlert>>> { ["test-a"] = new() { Value = alerts[0] } },
        };

        var pruned = WeatherCacheRetention.Prune(document, T0, 2);

        Assert.Null(pruned.Forecast.Value);
        Assert.Null(pruned.Forecast.ETag);
        Assert.Null(pruned.Forecast.LastModified);
        Assert.Equal(6, pruned.Hourly.Value!.Periods.Count);
        Assert.Equal(["KCLT"], pruned.Observations.Keys);
        Assert.Equal(["now"], pruned.Alerts["test-a"].Value!.Select(alert => alert.Id));
    }

    [Theory]
    [InlineData("https://forecast.weather.gov/MapClick.php?lat=35.1050&lon=-80.9420", true)]
    [InlineData("https://www.weather.gov/gsp/", true)]
    [InlineData("https://alerts.weather.gov/", true)]
    [InlineData("https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.x", true)]
    [InlineData("http://forecast.weather.gov/", false)]
    [InlineData("https://weather.gov.example.com/", false)]
    [InlineData("https://www.carowinds.com/", false)]
    [InlineData("https://user@forecast.weather.gov/", false)]
    [InlineData("https://forecast.weather.gov:8443/", false)]
    [InlineData("file:///C:/Windows/system32/calc.exe", false)]
    public void WeatherSourceLinkPolicy_AcceptsOnlyNwsHttpsPages(string address, bool expected)
    {
        Assert.Equal(expected, WeatherSourceLinkPolicy.TryValidate(new Uri(address), out _));
        Assert.False(WeatherSourceLinkPolicy.TryValidate(null, out _));
    }

    [Fact]
    public void OfficialSourceLinkPolicy_StillRefusesNwsPages() =>
        Assert.False(OfficialSourceLinkPolicy.TryValidate(new Uri("https://forecast.weather.gov/"), out _));

    [Fact]
    public void Attribution_NamesTheProviderAndItsPublicForecastPage()
    {
        Assert.Equal("National Weather Service", WeatherAttribution.ProviderName);
        Assert.Equal("https://forecast.weather.gov/MapClick.php?lat=35.1050&lon=-80.9420", WeatherAttribution.ForecastPage(WeatherTestData.PointA).AbsoluteUri);
    }
}
