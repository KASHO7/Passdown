using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.Tests.Time;

public class EasternTimeZoneTests
{
    [Fact]
    public void ConvertFromUtc_InJanuary_UsesEasternStandardTimeOffset()
    {
        var utc = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

        var eastern = EasternTimeZone.ConvertFromUtc(utc);

        Assert.Equal(TimeSpan.FromHours(-5), eastern.Offset);
        Assert.Equal(new DateTime(2026, 1, 15, 7, 0, 0), eastern.DateTime);
    }

    [Fact]
    public void ConvertFromUtc_InJuly_UsesEasternDaylightTimeOffset()
    {
        var utc = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        var eastern = EasternTimeZone.ConvertFromUtc(utc);

        Assert.Equal(TimeSpan.FromHours(-4), eastern.Offset);
        Assert.Equal(new DateTime(2026, 7, 15, 8, 0, 0), eastern.DateTime);
    }

    [Fact]
    public void ConvertFromUtc_ImmediatelyAfterSpringForward_UsesDaylightOffset()
    {
        // 2026-03-08 is the US spring-forward date: 2:00 AM EST becomes 3:00 AM EDT.
        var utc = new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero); // 3:30 AM EDT

        var eastern = EasternTimeZone.ConvertFromUtc(utc);

        Assert.Equal(TimeSpan.FromHours(-4), eastern.Offset);
        Assert.Equal(new DateTime(2026, 3, 8, 3, 30, 0), eastern.DateTime);
    }

    [Fact]
    public void ConvertFromUtc_ImmediatelyAfterFallBack_UsesStandardOffset()
    {
        // 2026-11-01 is the US fall-back date: 2:00 AM EDT becomes 1:00 AM EST.
        var utc = new DateTimeOffset(2026, 11, 1, 7, 30, 0, TimeSpan.Zero); // 2:30 AM EST

        var eastern = EasternTimeZone.ConvertFromUtc(utc);

        Assert.Equal(TimeSpan.FromHours(-5), eastern.Offset);
        Assert.Equal(new DateTime(2026, 11, 1, 2, 30, 0), eastern.DateTime);
    }

    [Fact]
    public void ConvertFromUtc_NearMidnightUtc_CanFallOnThePreviousEasternCalendarDate()
    {
        var utc = new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.Zero); // 10:00 PM Eastern the day before

        var eastern = EasternTimeZone.ConvertFromUtc(utc);

        Assert.Equal(new DateOnly(2026, 8, 19), DateOnly.FromDateTime(eastern.DateTime));
    }
}
