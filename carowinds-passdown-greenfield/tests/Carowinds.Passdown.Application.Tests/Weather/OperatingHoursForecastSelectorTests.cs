using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Application.Tests.Weather;

/// <summary>Phase 37F: selecting the hourly NWS periods that cover one operating date's park hours (synthetic data only).</summary>
public class OperatingHoursForecastSelectorTests
{
    private static readonly DateOnly Summer = new(2026, 7, 18); // EDT, UTC−4

    internal static EffectiveHours Hours(int openHour, int closeHour, int openMinute = 0, int closeMinute = 0) =>
        ParkHoursCalculator.Calculate(false, new TimeOnly(openHour, openMinute), new TimeOnly(closeHour, closeMinute), false, null, null, false, null, null);

    /// <summary>Hourly periods from <paramref name="firstStartUtc"/>, one hour each.</summary>
    internal static HourlyForecastPeriod[] HourlyFrom(DateTimeOffset firstStartUtc, int count, Func<int, int?>? temperature = null, Func<int, int?>? chance = null, Func<int, string?>? text = null) =>
        Enumerable.Range(0, count)
            .Select(i => new HourlyForecastPeriod(
                firstStartUtc.AddHours(i), firstStartUtc.AddHours(i + 1), IsDaytime: true,
                temperature is null ? 70 + (i % 5) : temperature(i),
                chance is null ? 10 : chance(i),
                null, null, null, null,
                text is null ? "Partly Sunny" : text(i)))
            .ToArray();

    internal static WeatherSnapshot Snapshot(IReadOnlyList<HourlyForecastPeriod> periods, DateTimeOffset checkedAt, DateTimeOffset? issuedAt = null)
    {
        var fresh = new WeatherPartStatus(WeatherAvailability.Fresh, checkedAt, checkedAt, WeatherFailureKind.None);
        var none = new WeatherPartStatus(WeatherAvailability.NeverRetrieved, null, null, WeatherFailureKind.None);
        return new WeatherSnapshot(checkedAt, true, none, null, fresh, new HourlyWeatherForecast("GSP", 1, 1, issuedAt ?? checkedAt.AddMinutes(-20), periods), none, null, none, []);
    }

    private static DateTimeOffset Utc(DateOnly date, int hour, int minute = 0, int offsetHours = -4) =>
        new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(offsetHours)).ToUniversalTime();

    [Fact]
    public void Selects_ExactlyTheOperatingHours_ForTheOperatingDate()
    {
        var now = Utc(Summer, 7);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 7), 48), now);

        var selection = OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(12, 20), now);

        Assert.Equal(OperatingHoursForecastOmission.None, selection.Omission);
        var forecast = Assert.IsType<OperatingHoursForecast>(selection.Forecast);
        Assert.Equal(Summer, forecast.OperatingDate);
        Assert.Equal(Utc(Summer, 12), forecast.OpensAt);
        Assert.Equal(Utc(Summer, 20), forecast.ClosesAt);
        Assert.Equal(8, forecast.Hours.Count);
        Assert.Equal(Utc(Summer, 12), forecast.Hours[0].Start);
        Assert.Equal(Utc(Summer, 20), forecast.Hours[^1].End);
        Assert.False(forecast.StartsAfterOpening);
    }

    [Fact]
    public void AnotherDate_SelectsThatDatesHours_NotToday()
    {
        var now = Utc(Summer, 7);
        var tomorrow = Summer.AddDays(1);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 7), 72), now);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, tomorrow, Hours(10, 18), now).Forecast!;

        Assert.Equal(tomorrow, forecast.OperatingDate);
        Assert.All(forecast.Hours, hour => Assert.True(hour.Start >= Utc(tomorrow, 10) && hour.End <= Utc(tomorrow, 18)));
    }

    [Theory]
    [InlineData(17, 18, 1)]   // short window
    [InlineData(9, 23, 14)]   // long day
    [InlineData(18, 23, 5)]   // evening event
    [InlineData(10, 22, 12)]
    public void Window_Length_MatchesTheHours(int open, int close, int expectedHours)
    {
        var now = Utc(Summer, 6);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 6), 48), now);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(open, close), now).Forecast!;

        Assert.Equal(expectedHours, forecast.Hours.Count);
    }

    [Fact]
    public void HalfHourHours_IncludeThePartlyCoveredHours()
    {
        var now = Utc(Summer, 6);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 6), 48), now);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(10, 21, openMinute: 30, closeMinute: 30), now).Forecast!;

        Assert.Equal(Utc(Summer, 10), forecast.Hours[0].Start);   // 10:00–11:00 overlaps 10:30
        Assert.Equal(Utc(Summer, 22), forecast.Hours[^1].End);    // 21:00–22:00 overlaps until 21:30
        Assert.Equal(Utc(Summer, 10, 30), forecast.OpensAt);
    }

    [Fact]
    public void HoursCrossingMidnight_CloseOnTheNextDay()
    {
        var now = Utc(Summer, 12);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 12), 48), now);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(18, 1), now).Forecast!;

        Assert.Equal(Utc(Summer.AddDays(1), 1), forecast.ClosesAt);
        Assert.Equal(7, forecast.Hours.Count);
    }

    [Fact]
    public void ClosingAtMidnight_EndsAtTheStartOfTheNextDay()
    {
        var now = Utc(Summer, 12);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 12), 48), now);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(16, 0), now).Forecast!;

        Assert.Equal(Utc(Summer.AddDays(1), 0), forecast.ClosesAt);
        Assert.Equal(8, forecast.Hours.Count);
    }

    [Fact]
    public void StandardTime_UsesUtcMinusFive()
    {
        var winter = new DateOnly(2026, 12, 5);
        var now = Utc(winter, 6, offsetHours: -5);
        var snapshot = Snapshot(HourlyFrom(now, 48), now);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, winter, Hours(17, 22), now).Forecast!;

        Assert.Equal(new DateTimeOffset(2026, 12, 5, 22, 0, 0, TimeSpan.Zero), forecast.OpensAt);
        Assert.Equal(5, forecast.Hours.Count);
    }

    [Fact]
    public void FallBackNight_HoursAcrossTheChange_CountTheRepeatedHour()
    {
        // 2026-11-01: 2:00 AM EDT becomes 1:00 AM EST. Open 8 PM Oct 31 (EDT) to 2 AM Nov 1 (EST) is 7 real hours.
        var date = new DateOnly(2026, 10, 31);
        var now = Utc(date, 12);
        var snapshot = Snapshot(HourlyFrom(now, 48), now);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, date, Hours(20, 2), now).Forecast!;

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), forecast.OpensAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero), forecast.ClosesAt);
        Assert.Equal(7, forecast.Hours.Count);
    }

    [Fact]
    public void SpringForwardDay_HoursAfterTheChange_UseDaylightTime()
    {
        var date = new DateOnly(2026, 3, 8); // 2:00 AM EST → 3:00 AM EDT
        var now = new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero);
        var snapshot = Snapshot(HourlyFrom(now, 48), now);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, date, Hours(10, 18), now).Forecast!;

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 14, 0, 0, TimeSpan.Zero), forecast.OpensAt);
        Assert.Equal(8, forecast.Hours.Count);
    }

    [Fact]
    public void ParkAlreadyOpen_CoversOnlyTheRestOfTheHours()
    {
        var now = Utc(Summer, 15, 20);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 15), 24), now);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(10, 20), now).Forecast!;

        Assert.True(forecast.StartsAfterOpening);
        Assert.Equal(now, forecast.CoverageStart);
        Assert.Equal(Utc(Summer, 15), forecast.Hours[0].Start);
        Assert.Equal(5, forecast.Hours.Count);
    }

    [Fact]
    public void HoursEnded_IsOmitted()
    {
        var now = Utc(Summer, 21);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 21), 24), now);

        Assert.Equal(OperatingHoursForecastOmission.HoursEnded, OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(10, 20), now).Omission);
    }

    [Fact]
    public void PastDate_IsOmitted()
    {
        var now = Utc(Summer, 9);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 9), 24), now);

        Assert.Equal(OperatingHoursForecastOmission.HoursEnded, OperatingHoursForecastSelector.Select(snapshot, Summer.AddDays(-1), Hours(10, 20), now).Omission);
    }

    [Fact]
    public void BeyondTheForecastHorizon_IsOmitted()
    {
        var now = Utc(Summer, 6);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 6), 156), now); // about 6.5 days

        var selection = OperatingHoursForecastSelector.Select(snapshot, Summer.AddDays(9), Hours(10, 20), now);

        Assert.Null(selection.Forecast);
        Assert.Equal(OperatingHoursForecastOmission.OutsideForecast, selection.Omission);
    }

    [Fact]
    public void HorizonEndingInsideTheHours_IsOmitted_NotSummarizedFromPartOfTheDay()
    {
        var now = Utc(Summer, 6);
        var snapshot = Snapshot(HourlyFrom(Utc(Summer, 6), 10), now); // ends 4 PM

        Assert.Equal(OperatingHoursForecastOmission.IncompleteCoverage, OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(10, 20), now).Omission);
    }

    [Fact]
    public void GapInTheHourlyForecast_IsOmitted()
    {
        var now = Utc(Summer, 6);
        var periods = HourlyFrom(Utc(Summer, 6), 24).Where(period => period.Start != Utc(Summer, 14)).ToArray();

        Assert.Equal(OperatingHoursForecastOmission.IncompleteCoverage, OperatingHoursForecastSelector.Select(Snapshot(periods, now), Summer, Hours(10, 20), now).Omission);
    }

    [Fact]
    public void ForecastOlderThanThreeHours_IsOmitted()
    {
        var retrieved = Utc(Summer, 6);
        var snapshot = Snapshot(HourlyFrom(retrieved, 48), retrieved);

        Assert.Equal(OperatingHoursForecastOmission.None, OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(12, 20), retrieved.AddHours(3)).Omission);
        Assert.Equal(OperatingHoursForecastOmission.ForecastTooOld, OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(12, 20), retrieved.AddHours(3).AddMinutes(1)).Omission);
        Assert.Equal(TimeSpan.FromHours(3), OperatingHoursForecastSelector.MaxForecastAge);
    }

    [Fact]
    public void NoSnapshot_OrNoHourlyForecast_IsOmitted()
    {
        var now = Utc(Summer, 7);
        Assert.Equal(OperatingHoursForecastOmission.NoForecast, OperatingHoursForecastSelector.Select(null, Summer, Hours(10, 20), now).Omission);

        var none = new WeatherPartStatus(WeatherAvailability.Unavailable, null, now, WeatherFailureKind.Network);
        var offline = new WeatherSnapshot(now, true, none, null, none, null, none, null, none, []);
        Assert.Equal(OperatingHoursForecastOmission.NoForecast, OperatingHoursForecastSelector.Select(offline, Summer, Hours(10, 20), now).Omission);
    }

    [Fact]
    public void MissingParkHours_AreNeverReplacedByAnotherPeriod()
    {
        var now = Utc(Summer, 7);
        var snapshot = Snapshot(HourlyFrom(now, 48), now);

        var notChecked = ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null);
        var closed = ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null);
        var sameTimes = Hours(12, 12);

        Assert.Equal(OperatingHoursForecastOmission.NoParkHours, OperatingHoursForecastSelector.Select(snapshot, Summer, notChecked, now).Omission);
        Assert.Equal(OperatingHoursForecastOmission.NoParkHours, OperatingHoursForecastSelector.Select(snapshot, Summer, closed, now).Omission);
        Assert.Equal(OperatingHoursForecastOmission.NoParkHours, OperatingHoursForecastSelector.Select(snapshot, Summer, sameTimes, now).Omission);
    }

    [Fact]
    public void ManualOverrideHours_AreTheHoursUsed()
    {
        var now = Utc(Summer, 6);
        var snapshot = Snapshot(HourlyFrom(now, 48), now);
        var manual = ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(20, 0), true, new TimeOnly(12, 0), new TimeOnly(16, 0), false, null, null);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, Summer, manual, now).Forecast!;

        Assert.Equal(Utc(Summer, 12), forecast.OpensAt);
        Assert.Equal(4, forecast.Hours.Count);
    }

    [Fact]
    public void Selection_CarriesTheNwsIssueTimeAndRetrievalTime()
    {
        var now = Utc(Summer, 7);
        var issued = Utc(Summer, 6, 5);
        var snapshot = Snapshot(HourlyFrom(now, 48), now, issued);

        var forecast = OperatingHoursForecastSelector.Select(snapshot, Summer, Hours(10, 20), now).Forecast!;

        Assert.Equal(issued, forecast.IssuedAt);
        Assert.Equal(now, forecast.RetrievedAt);
    }
}
