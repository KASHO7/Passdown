using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Domain.Tests.Retrieval;

/// <summary>Pre-RC.15 post-closing pricing retention: <see cref="LastKnownGood{TValue}.RecordNoOfferAttemptRetainingVerified"/>.</summary>
public class PostClosingRetentionLastKnownGoodTests
{
    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/daily-tickets");
    private static readonly DateTimeOffset VerifiedAt = new(2026, 11, 27, 15, 10, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset AfterClosing = new(2026, 11, 27, 22, 30, 0, TimeSpan.FromHours(-5));

    private static SourceEvidence Evidence(RetrievalStatus status, DateTimeOffset at) =>
        new("Dated admission", SampleUrl, at, status, "Checked.", RetrievalStrategy.DirectApi);

    [Theory]
    [InlineData(RetrievalStatus.Unavailable)]
    [InlineData(RetrievalStatus.NotPublished)]
    public void KeepsTheVerifiedValueAndEvidence_AndRecordsTheLatestAnswer(RetrievalStatus answer)
    {
        var verified = Evidence(RetrievalStatus.Verified, VerifiedAt);
        var lkg = LastKnownGood<string>.Empty.RecordAttempt(verified, "$64.99")
            .RecordNoOfferAttemptRetainingVerified(Evidence(answer, AfterClosing));

        Assert.Equal("$64.99", lkg.SuccessValue);
        Assert.Same(verified, lkg.SuccessEvidence);
        Assert.Equal(answer, lkg.LastAttemptEvidence!.Status);
        Assert.True(lkg.IsStale);
    }

    [Fact]
    public void WithoutAVerifiedValue_BehavesLikeRecordAttempt()
    {
        var lkg = LastKnownGood<string>.Empty.RecordNoOfferAttemptRetainingVerified(Evidence(RetrievalStatus.Unavailable, AfterClosing));

        Assert.Null(lkg.SuccessValue);
        Assert.Equal(RetrievalStatus.Unavailable, lkg.SuccessEvidence!.Status);
        Assert.False(lkg.IsStale);
    }

    [Theory]
    [InlineData(RetrievalStatus.Verified)]
    [InlineData(RetrievalStatus.Closed)]
    [InlineData(RetrievalStatus.Error)]
    public void RejectsAnyOtherStatus(RetrievalStatus status)
    {
        Assert.Throws<ArgumentException>(() => LastKnownGood<string>.Empty.RecordNoOfferAttemptRetainingVerified(Evidence(status, AfterClosing)));
    }

    [Fact]
    public void ExistingRecordAttempt_StillLetsASuccessfulNoOfferAnswerReplaceTheValue()
    {
        // Unchanged generic semantics: only the explicit retention operation keeps the value.
        var lkg = LastKnownGood<string>.Empty.RecordAttempt(Evidence(RetrievalStatus.Verified, VerifiedAt), "$64.99")
            .RecordAttempt(Evidence(RetrievalStatus.Unavailable, AfterClosing), null);

        Assert.Null(lkg.SuccessValue);
    }
}
