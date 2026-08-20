using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Freshness;

public class DatedResultProjectionExtensionsTests
{
    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/park-hours");
    private static readonly DateOnly Halloween = new(2026, 10, 31);
    private static readonly DatedResultKey Key = new(SourceCategory.ParkHours, Halloween);

    private static SourceEvidence Evidence(RetrievalStatus status, DateTimeOffset timeChecked) => new(
        sourceName: "Park hours",
        sourceUrl: SampleUrl,
        timeChecked: timeChecked,
        status: status,
        detail: status == RetrievalStatus.Error ? "Timed out." : "Checked successfully.",
        strategy: RetrievalStrategy.Html,
        requestedDate: Halloween,
        confirmedDate: status == RetrievalStatus.Verified ? Halloween : null);

    private static readonly DateTimeOffset Day1 = new(2026, 10, 20, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2026, 10, 21, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToLastKnownGood_SuccessOnly_HasSuccessAndMatchingStatus()
    {
        var successEvidence = Evidence(RetrievalStatus.Verified, Day1);
        var record = new DatedResultRecord(Key, successEvidence, "9 AM - 9 PM", successEvidence);

        var projection = record.ToLastKnownGood();

        Assert.True(projection.HasSuccess);
        Assert.Equal("9 AM - 9 PM", projection.SuccessValue);
        Assert.Equal(RetrievalStatus.Verified, projection.EffectiveStatus);
        Assert.False(projection.IsStale);
    }

    [Fact]
    public void ToLastKnownGood_LatestErrorAfterSuccess_PreservesThePreviousSuccessValue()
    {
        var successEvidence = Evidence(RetrievalStatus.Verified, Day1);
        var errorEvidence = Evidence(RetrievalStatus.Error, Day2);
        var record = new DatedResultRecord(Key, successEvidence, "9 AM - 9 PM", errorEvidence);

        var projection = record.ToLastKnownGood();

        Assert.True(projection.HasSuccess);
        Assert.Equal("9 AM - 9 PM", projection.SuccessValue);
        Assert.Equal(successEvidence, projection.SuccessEvidence);
    }

    [Fact]
    public void ToLastKnownGood_LatestErrorAfterSuccess_IsMarkedStale()
    {
        var successEvidence = Evidence(RetrievalStatus.Verified, Day1);
        var errorEvidence = Evidence(RetrievalStatus.Error, Day2);
        var record = new DatedResultRecord(Key, successEvidence, "9 AM - 9 PM", errorEvidence);

        var projection = record.ToLastKnownGood();

        Assert.True(projection.IsStale);
        Assert.Equal(RetrievalStatus.Stale, projection.EffectiveStatus);
    }

    [Fact]
    public void ToLastKnownGood_StaleAge_IsMeasuredFromTheSuccessfulEvidenceNotTheLatestAttempt()
    {
        var successEvidence = Evidence(RetrievalStatus.Verified, Day1);
        var errorEvidence = Evidence(RetrievalStatus.Error, Day2);
        var record = new DatedResultRecord(Key, successEvidence, "9 AM - 9 PM", errorEvidence);
        var asOf = Day2.AddDays(1);

        var projection = record.ToLastKnownGood();

        Assert.Equal(asOf - Day1, projection.Age(asOf));
    }

    [Fact]
    public void ToLastKnownGood_NoPriorSuccess_ErrorOnly_ProducesNoFakeSuccessValue()
    {
        var errorEvidence = Evidence(RetrievalStatus.Error, Day1);
        var record = new DatedResultRecord(Key, SuccessEvidence: null, SuccessPayloadJson: null, errorEvidence);

        var projection = record.ToLastKnownGood();

        Assert.False(projection.HasSuccess);
        Assert.Null(projection.SuccessValue);
        Assert.Null(projection.Age(Day2));
        // Never Stale: staleness only applies when there is a previous success to be stale relative to.
        Assert.Equal(RetrievalStatus.Error, projection.EffectiveStatus);
        Assert.False(projection.IsStale);
    }

    [Fact]
    public void ToLastKnownGood_RecoverySuccessAfterAnErrorAttempt_ClearsStaleness()
    {
        var successEvidence = Evidence(RetrievalStatus.Verified, Day1);
        var errorEvidence = Evidence(RetrievalStatus.Error, Day2);
        var staleRecord = new DatedResultRecord(Key, successEvidence, "9 AM - 9 PM", errorEvidence);
        Assert.True(staleRecord.ToLastKnownGood().IsStale);

        var recoveredEvidence = Evidence(RetrievalStatus.Verified, Day2.AddDays(1));
        var recoveredRecord = new DatedResultRecord(Key, recoveredEvidence, "10 AM - 10 PM", recoveredEvidence);

        var projection = recoveredRecord.ToLastKnownGood();

        Assert.False(projection.IsStale);
        Assert.Equal("10 AM - 10 PM", projection.SuccessValue);
    }
}
