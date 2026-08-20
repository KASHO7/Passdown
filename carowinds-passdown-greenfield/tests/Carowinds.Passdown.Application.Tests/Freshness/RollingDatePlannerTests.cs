using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Tests.TestSupport;

namespace Carowinds.Passdown.Application.Tests.Freshness;

public class RollingDatePlannerTests
{
    [Fact]
    public void GetRefreshWindow_DefaultLength_ReturnsTodayThroughTheFollowingSixDaysInOrder()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)); // 6 AM EDT
        var planner = new RollingDatePlanner(clock, new RollingWindowOptions());

        var window = planner.GetRefreshWindow();

        Assert.Equal(7, window.Count);
        Assert.Equal(
            [
                new DateOnly(2026, 8, 20), // today
                new DateOnly(2026, 8, 21), // tomorrow
                new DateOnly(2026, 8, 22),
                new DateOnly(2026, 8, 23),
                new DateOnly(2026, 8, 24),
                new DateOnly(2026, 8, 25),
                new DateOnly(2026, 8, 26),
            ],
            window);
    }

    [Fact]
    public void GetRefreshWindow_PrioritizesTodayThenTomorrowFirst()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var planner = new RollingDatePlanner(clock, new RollingWindowOptions());

        var window = planner.GetRefreshWindow();

        Assert.Equal(new DateOnly(2026, 8, 20), window[0]);
        Assert.Equal(new DateOnly(2026, 8, 21), window[1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(14)]
    public void GetRefreshWindow_ExplicitLength_OverridesTheConfiguredDefault(int windowLengthDays)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var planner = new RollingDatePlanner(clock, new RollingWindowOptions { WindowLengthDays = 7 });

        var window = planner.GetRefreshWindow(windowLengthDays);

        Assert.Equal(windowLengthDays, window.Count);
        Assert.Equal(new DateOnly(2026, 8, 20), window[0]);
        Assert.Equal(new DateOnly(2026, 8, 20).AddDays(windowLengthDays - 1), window[^1]);
    }

    [Fact]
    public void GetRefreshWindow_UsesTheConfiguredOptionsLengthWhenNoOverrideGiven()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var planner = new RollingDatePlanner(clock, new RollingWindowOptions { WindowLengthDays = 10 });

        var window = planner.GetRefreshWindow();

        Assert.Equal(10, window.Count);
    }

    [Fact]
    public void GetRefreshWindow_ZeroOrNegativeLength_Throws()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var planner = new RollingDatePlanner(clock, new RollingWindowOptions());

        Assert.Throws<ArgumentOutOfRangeException>(() => planner.GetRefreshWindow(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => planner.GetRefreshWindow(-1));
    }

    [Fact]
    public void GetRefreshWindow_NearMidnightUtc_AnchorsOnTheEasternCalendarDateNotTheUtcOne()
    {
        // 2026-08-20 02:00 UTC is 2026-08-19 22:00 EDT — a UTC-naive implementation
        // would anchor "today" one day late.
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.Zero));
        var planner = new RollingDatePlanner(clock, new RollingWindowOptions());

        var window = planner.GetRefreshWindow(1);

        Assert.Equal([new DateOnly(2026, 8, 19)], window);
    }

    [Fact]
    public void GetRefreshWindow_AcrossADstTransition_StillProducesConsecutiveCalendarDates()
    {
        // 2026-11-01 is the US fall-back date; "today" must still resolve correctly.
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero)); // 11 AM EDT/EST that day
        var planner = new RollingDatePlanner(clock, new RollingWindowOptions());

        var window = planner.GetRefreshWindow(3);

        Assert.Equal(
            [new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3)],
            window);
    }
}
