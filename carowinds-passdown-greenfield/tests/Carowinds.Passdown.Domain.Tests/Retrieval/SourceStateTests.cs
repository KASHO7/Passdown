using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Domain.Tests.Retrieval;

public class SourceStateTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 8, 18, 6, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateTimeOffset Day2 = new(2026, 8, 19, 6, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void RecordSuccess_ResetsFailureCountAndSetsLastSuccess()
    {
        var state = SourceState.Initial(SourceCategory.SeasonPasses)
            .RecordFailure(Day1, nextDueAt: null)
            .RecordFailure(Day1, nextDueAt: null);

        var afterSuccess = state.RecordSuccess(Day2, nextDueAt: Day2.AddDays(7), fingerprint: "abc123");

        Assert.Equal(0, afterSuccess.ConsecutiveFailureCount);
        Assert.Equal(Day2, afterSuccess.LastSuccessAt);
        Assert.Equal(Day2, afterSuccess.LastAttemptAt);
        Assert.Equal("abc123", afterSuccess.SourceFingerprint);
    }

    [Fact]
    public void RecordFailure_LeavesLastSuccessUntouched()
    {
        var state = SourceState.Initial(SourceCategory.SeasonPasses)
            .RecordSuccess(Day1, nextDueAt: Day1.AddDays(7));

        var afterFailure = state.RecordFailure(Day2, nextDueAt: null);

        Assert.Equal(Day1, afterFailure.LastSuccessAt);
        Assert.Equal(Day2, afterFailure.LastAttemptAt);
        Assert.Equal(1, afterFailure.ConsecutiveFailureCount);
    }

    [Fact]
    public void RecordFailure_AccumulatesAcrossRepeatedFailures()
    {
        var state = SourceState.Initial(SourceCategory.SeasonPasses)
            .RecordFailure(Day1, nextDueAt: null)
            .RecordFailure(Day1, nextDueAt: null)
            .RecordFailure(Day1, nextDueAt: null);

        Assert.Equal(3, state.ConsecutiveFailureCount);
    }
}
