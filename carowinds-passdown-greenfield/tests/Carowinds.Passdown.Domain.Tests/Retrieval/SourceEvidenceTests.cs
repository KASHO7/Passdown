using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Domain.Tests.Retrieval;

public class SourceEvidenceTests
{
    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/park-hours");
    private static readonly DateTimeOffset SampleTime = new(2026, 8, 20, 6, 0, 0, TimeSpan.FromHours(-4));

    private static SourceEvidence Build(
        RetrievalStatus status,
        DateOnly? requestedDate = null,
        DateOnly? confirmedDate = null) =>
        new(
            sourceName: "Park hours",
            sourceUrl: SampleUrl,
            timeChecked: SampleTime,
            status: status,
            detail: "Checked successfully.",
            strategy: RetrievalStrategy.Html,
            requestedDate: requestedDate,
            confirmedDate: confirmedDate);

    [Fact]
    public void Constructor_RejectsStaleAsARawStatus()
    {
        var exception = Assert.Throws<ArgumentException>(() => Build(RetrievalStatus.Stale));
        Assert.Contains("derived status", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsVerifiedResultThatConfirmsADifferentDate()
    {
        var requested = new DateOnly(2026, 10, 31);
        var fellBackToToday = new DateOnly(2026, 8, 20);

        var exception = Assert.Throws<ArgumentException>(
            () => Build(RetrievalStatus.Verified, requested, fellBackToToday));

        Assert.Contains("exact date", exception.Message);
    }

    [Fact]
    public void Constructor_AcceptsVerifiedResultThatConfirmsTheRequestedDate()
    {
        var date = new DateOnly(2026, 10, 31);

        var evidence = Build(RetrievalStatus.Verified, date, date);

        Assert.Equal(RetrievalStatus.Verified, evidence.Status);
        Assert.Equal(date, evidence.ConfirmedDate);
    }

    [Fact]
    public void Constructor_AllowsNonVerifiedStatusesWithoutAConfirmedDate()
    {
        var requested = new DateOnly(2026, 10, 31);

        var evidence = Build(RetrievalStatus.NotPublished, requested, confirmedDate: null);

        Assert.Equal(RetrievalStatus.NotPublished, evidence.Status);
        Assert.Null(evidence.ConfirmedDate);
    }

    [Fact]
    public void Constructor_RejectsBlankSourceName()
    {
        Assert.Throws<ArgumentException>(() => new SourceEvidence(
            sourceName: "   ",
            sourceUrl: SampleUrl,
            timeChecked: SampleTime,
            status: RetrievalStatus.Error,
            detail: "Timed out.",
            strategy: RetrievalStrategy.Html));
    }
}
