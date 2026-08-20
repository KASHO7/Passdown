using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Tests.Retrieval;

public class RetrievalRequestTests
{
    [Fact]
    public void Constructor_SetsCatalogRequestWithNoDate()
    {
        var request = new RetrievalRequest("season-passes.official", SourceCategory.SeasonPasses);

        Assert.Equal("season-passes.official", request.SourceId);
        Assert.Equal(SourceCategory.SeasonPasses, request.Category);
        Assert.Null(request.RequestedDate);
    }

    [Fact]
    public void Constructor_SetsDatedRequestDate()
    {
        var date = new DateOnly(2026, 10, 31);

        var request = new RetrievalRequest("park-hours.official", SourceCategory.ParkHours, date);

        Assert.Equal(date, request.RequestedDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankSourceId(string? sourceId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RetrievalRequest(sourceId!, SourceCategory.ParkHours));
    }
}
