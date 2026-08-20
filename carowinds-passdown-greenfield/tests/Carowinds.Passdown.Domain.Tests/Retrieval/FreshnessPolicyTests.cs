using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Domain.Tests.Retrieval;

public class FreshnessPolicyTests
{
    private static readonly TimeSpan Eastern = TimeSpan.FromHours(-4);

    private static FreshnessPolicy DailyAt6Am() => new(
        SourceCategory.ParkHours,
        new FreshnessCadence.DailyAt(new TimeOnly(6, 0)));

    private static FreshnessPolicy Every7Days() => new(
        SourceCategory.SeasonPasses,
        new FreshnessCadence.RollingInterval(TimeSpan.FromDays(7)));

    [Fact]
    public void IsDue_NeverSucceededIsAlwaysDue()
    {
        var policy = DailyAt6Am();
        var state = SourceState.Initial(SourceCategory.ParkHours);
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, Eastern);

        Assert.True(policy.IsDue(state.LastSuccessAt, now));
    }

    [Fact]
    public void IsDue_DailyAt_NotDueBeforeTodaysScheduledTime_WhenLastSuccessWasYesterday()
    {
        var policy = DailyAt6Am();
        var lastSuccess = new DateTimeOffset(2026, 8, 19, 6, 30, 0, Eastern);
        var state = SourceState.Initial(SourceCategory.ParkHours).RecordSuccess(lastSuccess, nextDueAt: null);
        var now = new DateTimeOffset(2026, 8, 20, 5, 59, 0, Eastern); // before today's 6 AM

        Assert.False(policy.IsDue(state.LastSuccessAt, now));
    }

    [Fact]
    public void IsDue_DailyAt_DueOnceTodaysScheduledTimeHasPassed()
    {
        var policy = DailyAt6Am();
        var lastSuccess = new DateTimeOffset(2026, 8, 19, 6, 30, 0, Eastern);
        var state = SourceState.Initial(SourceCategory.ParkHours).RecordSuccess(lastSuccess, nextDueAt: null);
        var now = new DateTimeOffset(2026, 8, 20, 6, 0, 1, Eastern); // just after today's 6 AM

        Assert.True(policy.IsDue(state.LastSuccessAt, now));
    }

    [Fact]
    public void IsDue_DailyAt_NotDueImmediatelyAfterTodaysRefreshSucceeded()
    {
        var policy = DailyAt6Am();
        var lastSuccess = new DateTimeOffset(2026, 8, 20, 6, 1, 0, Eastern);
        var state = SourceState.Initial(SourceCategory.ParkHours).RecordSuccess(lastSuccess, nextDueAt: null);
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, Eastern); // later the same day

        Assert.False(policy.IsDue(state.LastSuccessAt, now));
    }

    [Fact]
    public void IsDue_RollingInterval_NotDueBeforeIntervalElapses()
    {
        var policy = Every7Days();
        var lastSuccess = new DateTimeOffset(2026, 8, 14, 6, 0, 0, Eastern);
        var state = SourceState.Initial(SourceCategory.SeasonPasses).RecordSuccess(lastSuccess, nextDueAt: null);
        var now = lastSuccess.AddDays(6);

        Assert.False(policy.IsDue(state.LastSuccessAt, now));
    }

    [Fact]
    public void IsDue_RollingInterval_DueOnceIntervalElapses()
    {
        var policy = Every7Days();
        var lastSuccess = new DateTimeOffset(2026, 8, 14, 6, 0, 0, Eastern);
        var state = SourceState.Initial(SourceCategory.SeasonPasses).RecordSuccess(lastSuccess, nextDueAt: null);
        var now = lastSuccess.AddDays(7);

        Assert.True(policy.IsDue(state.LastSuccessAt, now));
    }

    [Fact]
    public void ComputeNextDueAt_DailyAt_RollsToTomorrowWhenSuccessWasAfterTodaysTime()
    {
        var policy = DailyAt6Am();
        var lastSuccess = new DateTimeOffset(2026, 8, 20, 9, 0, 0, Eastern);

        var nextDue = policy.ComputeNextDueAt(lastSuccess);

        Assert.Equal(new DateTimeOffset(2026, 8, 21, 6, 0, 0, Eastern), nextDue);
    }

    [Fact]
    public void ComputeNextDueAt_RollingInterval_AddsTheInterval()
    {
        var policy = Every7Days();
        var lastSuccess = new DateTimeOffset(2026, 8, 14, 6, 0, 0, Eastern);

        var nextDue = policy.ComputeNextDueAt(lastSuccess);

        Assert.Equal(lastSuccess.AddDays(7), nextDue);
    }
}
