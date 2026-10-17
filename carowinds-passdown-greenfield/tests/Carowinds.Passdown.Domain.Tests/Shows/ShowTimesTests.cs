using Carowinds.Passdown.Domain.Shows;

namespace Carowinds.Passdown.Domain.Tests.Shows;

public class ShowTimesTests
{
    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    [Fact]
    public void Normalize_RemovesDuplicates_AndOrdersLateNightAfterEvening()
    {
        Assert.Equal([T(19, 30), T(23, 30), T(0, 15)], ShowTimes.Normalize([T(0, 15), T(23, 30), T(19, 30), T(23, 30)]));
    }

    [Fact]
    public void StartsAt_LateNightPerformance_IsOnTheNextCalendarDate()
    {
        var date = new DateOnly(2026, 10, 17);

        Assert.Equal(new DateTime(2026, 10, 17, 19, 0, 0), ShowTimes.StartsAt(date, T(19)));
        Assert.Equal(new DateTime(2026, 10, 18, 0, 15, 0), ShowTimes.StartsAt(date, T(0, 15)));
    }

    [Fact]
    public void StartsAt_OnDaylightSavingChangeDate_IsPlainWallClockTime()
    {
        // 2026-11-01 is the fall-back date; official times are wall-clock and are never shifted.
        Assert.Equal(new DateTime(2026, 11, 1, 13, 0, 0), ShowTimes.StartsAt(new DateOnly(2026, 11, 1), T(13)));
    }

    [Fact]
    public void Format_IsStandardUsTime_WithoutSeconds()
    {
        Assert.Equal("1:00 PM", ShowTimes.Format(T(13)));
        Assert.Equal("1:00 PM, 3:30 PM, 7:00 PM", ShowTimes.FormatList([T(19), T(13), T(15, 30)]));
    }

    [Fact]
    public void FormatCompactList_WritesThePeriodOncePerRun_AndKeepsEveryMinute()
    {
        Assert.Equal("1:00, 3:00 PM", ShowTimes.FormatCompactList([T(13), T(15)]));
        Assert.Equal("11:30 AM, 1:00 PM", ShowTimes.FormatCompactList([T(11, 30), T(13)]));
        Assert.Equal("11:15, 11:45 AM, 12:15, 5:45 PM", ShowTimes.FormatCompactList([T(11, 15), T(11, 45), T(12, 15), T(17, 45)]));
    }
}

public class ShowIdentityTests
{
    [Fact]
    public void Resolve_PrefersTheOfficialLocationId()
    {
        Assert.Equal("SHOW-030-00122", ShowIdentity.Resolve(" show-030-00122 ", 2261, "Wake the Dead", "Theatre"));
    }

    [Fact]
    public void Resolve_FallsBackToTheNumericShowId()
    {
        Assert.Equal("fims-show:2261", ShowIdentity.Resolve(null, 2261, "Wake the Dead", "Theatre"));
    }

    [Fact]
    public void Resolve_NameVenueFallback_IgnoresCaseAndSpacing_ButKeepsVenuesDistinct()
    {
        var a = ShowIdentity.Resolve(null, null, "Wake  the Dead ", "Carowinds Theatre");
        var b = ShowIdentity.Resolve("", 0, "wake the dead", "carowinds   theatre");
        var otherVenue = ShowIdentity.Resolve(null, null, "Wake the Dead", "Plaza Stage");

        Assert.Equal(a, b);
        Assert.NotEqual(a, otherVenue);
    }

    [Fact]
    public void CleanDisplayText_TrimsAndCollapsesWhitespace()
    {
        Assert.Equal("CarnEVIL: Thrills Zone", ShowIdentity.CleanDisplayText("CarnEVIL:  Thrills Zone "));
        Assert.Null(ShowIdentity.CleanDisplayText("   "));
    }
}
