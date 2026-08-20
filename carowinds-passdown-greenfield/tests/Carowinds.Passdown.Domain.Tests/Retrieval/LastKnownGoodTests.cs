using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Domain.Tests.Retrieval;

public class LastKnownGoodTests
{
    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/park-hours");

    private static SourceEvidence Evidence(RetrievalStatus status, DateTimeOffset timeChecked) => new(
        sourceName: "Park hours",
        sourceUrl: SampleUrl,
        timeChecked: timeChecked,
        status: status,
        detail: status == RetrievalStatus.Error ? "Timed out." : "Checked successfully.",
        strategy: RetrievalStrategy.Html);

    private static readonly DateTimeOffset Day1 = new(2026, 8, 18, 6, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateTimeOffset Day2 = new(2026, 8, 19, 6, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateTimeOffset Day3 = new(2026, 8, 20, 6, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Empty_HasNoSuccessAndNullEffectiveStatus()
    {
        var lkg = LastKnownGood<string>.Empty;

        Assert.False(lkg.HasSuccess);
        Assert.Null(lkg.EffectiveStatus);
        Assert.Null(lkg.Age(Day1));
    }

    [Fact]
    public void RecordAttempt_SuccessfulCompletionBecomesTheKnownGoodValue()
    {
        var evidence = Evidence(RetrievalStatus.Verified, Day1);

        var lkg = LastKnownGood<string>.Empty.RecordAttempt(evidence, "9 AM - 9 PM");

        Assert.True(lkg.HasSuccess);
        Assert.Equal("9 AM - 9 PM", lkg.SuccessValue);
        Assert.Equal(RetrievalStatus.Verified, lkg.EffectiveStatus);
        Assert.False(lkg.IsStale);
    }

    [Fact]
    public void RecordAttempt_FailedAttemptDoesNotOverwritePreviouslyVerifiedValue()
    {
        var success = LastKnownGood<string>.Empty.RecordAttempt(Evidence(RetrievalStatus.Verified, Day1), "9 AM - 9 PM");

        var afterFailure = success.RecordAttempt(Evidence(RetrievalStatus.Error, Day2), null);

        Assert.Equal("9 AM - 9 PM", afterFailure.SuccessValue);
        Assert.Equal(Day1, afterFailure.SuccessEvidence!.TimeChecked);
        Assert.Equal(Day2, afterFailure.LastAttemptEvidence!.TimeChecked);
    }

    [Fact]
    public void RecordAttempt_FailedAttemptAfterSuccessMarksResultStale()
    {
        var success = LastKnownGood<string>.Empty.RecordAttempt(Evidence(RetrievalStatus.Verified, Day1), "9 AM - 9 PM");

        var afterFailure = success.RecordAttempt(Evidence(RetrievalStatus.Error, Day2), null);

        Assert.True(afterFailure.IsStale);
        Assert.Equal(RetrievalStatus.Stale, afterFailure.EffectiveStatus);
    }

    [Fact]
    public void RecordAttempt_SubsequentSuccessClearsStaleness()
    {
        var success = LastKnownGood<string>.Empty.RecordAttempt(Evidence(RetrievalStatus.Verified, Day1), "9 AM - 9 PM");
        var afterFailure = success.RecordAttempt(Evidence(RetrievalStatus.Error, Day2), null);

        var recovered = afterFailure.RecordAttempt(Evidence(RetrievalStatus.Verified, Day3), "10 AM - 10 PM");

        Assert.False(recovered.IsStale);
        Assert.Equal("10 AM - 10 PM", recovered.SuccessValue);
        Assert.Equal(RetrievalStatus.Verified, recovered.EffectiveStatus);
    }

    [Fact]
    public void RecordAttempt_ClosedIsASuccessfulCompletionNotAFailure()
    {
        var lkg = LastKnownGood<string>.Empty.RecordAttempt(Evidence(RetrievalStatus.Closed, Day1), null);

        Assert.True(lkg.HasSuccess);
        Assert.Equal(RetrievalStatus.Closed, lkg.EffectiveStatus);
    }

    [Fact]
    public void Age_MeasuredFromTheSuccessfulEvidenceNotTheLatestAttempt()
    {
        var success = LastKnownGood<string>.Empty.RecordAttempt(Evidence(RetrievalStatus.Verified, Day1), "9 AM - 9 PM");
        var stale = success.RecordAttempt(Evidence(RetrievalStatus.Error, Day3), null);

        Assert.Equal(Day3 - Day1, stale.Age(Day3));
    }
}
