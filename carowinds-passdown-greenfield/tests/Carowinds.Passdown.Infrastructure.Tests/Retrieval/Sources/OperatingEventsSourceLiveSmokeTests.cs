using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>Phase 31: opt-in live check of the official events listing/pages (never part of the normal build).</summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class OperatingEventsSourceLiveSmokeTests
{
    [Fact]
    public async Task RetrieveAsync_AgainstTheLiveEventPages_ReturnsOfficialDatesOnly()
    {
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(20) });
        var source = new OperatingEventsSource(retrievalHttpClient, new SystemClock());

        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.NotEmpty(result.Payload!.Events);
        Assert.All(result.Payload.Events, e => Assert.True(e.EndDate >= e.StartDate));
    }
}
