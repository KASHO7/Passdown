using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Retrieval;

public class SourceResultTests
{
    private static readonly Uri SampleUrl = new("https://www.sixflags.com/carowinds/park-hours");
    private static readonly DateTimeOffset SampleTime = new(2026, 8, 20, 6, 0, 0, TimeSpan.FromHours(-4));

    private static SourceEvidence Build(RetrievalStatus status) => new(
        sourceName: "Park hours",
        sourceUrl: SampleUrl,
        timeChecked: SampleTime,
        status: status,
        detail: "Checked successfully.",
        strategy: RetrievalStrategy.Html);

    [Fact]
    public void Success_WithVerifiedEvidence_CarriesThePayload()
    {
        var evidence = Build(RetrievalStatus.Verified);

        var result = SourceResult<string>.Success("park-hours.official", evidence, "9:00 AM - 10:00 PM");

        Assert.Equal("park-hours.official", result.SourceId);
        Assert.Same(evidence, result.Evidence);
        Assert.Equal("9:00 AM - 10:00 PM", result.Payload);
    }

    [Theory]
    [InlineData(RetrievalStatus.Closed)]
    [InlineData(RetrievalStatus.NotPublished)]
    [InlineData(RetrievalStatus.Unavailable)]
    [InlineData(RetrievalStatus.Error)]
    public void Success_WithNonVerifiedEvidence_Throws(RetrievalStatus status)
    {
        var evidence = Build(status);

        Assert.Throws<ArgumentException>(() => SourceResult<string>.Success("park-hours.official", evidence, "payload"));
    }

    [Fact]
    public void WithoutPayload_WithVerifiedEvidence_Throws()
    {
        var evidence = Build(RetrievalStatus.Verified);

        Assert.Throws<ArgumentException>(() => SourceResult<string>.WithoutPayload("park-hours.official", evidence));
    }

    [Theory]
    [InlineData(RetrievalStatus.Closed)]
    [InlineData(RetrievalStatus.NotPublished)]
    [InlineData(RetrievalStatus.Unavailable)]
    [InlineData(RetrievalStatus.Error)]
    public void WithoutPayload_WithNonVerifiedEvidence_LeavesPayloadUnset(RetrievalStatus status)
    {
        var evidence = Build(status);

        var result = SourceResult<string>.WithoutPayload("park-hours.official", evidence);

        Assert.Null(result.Payload);
        Assert.Same(evidence, result.Evidence);
    }

    [Fact]
    public void Success_RejectsNullPayload()
    {
        var evidence = Build(RetrievalStatus.Verified);

        Assert.Throws<ArgumentNullException>(() => SourceResult<string>.Success("park-hours.official", evidence, null!));
    }
}
