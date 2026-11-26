using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>Phase 32: opt-in live check of the official Perks &amp; Play pages (never part of the normal build).</summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class PassPerksSourceLiveSmokeTests
{
    [Fact]
    public async Task RetrieveAsync_AgainstTheLivePerksPages_ReturnsAMonthListWithOfficialDatesOnly()
    {
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(20) });
        var source = new PassPerksSource(retrievalHttpClient, new SystemClock());

        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.NotEmpty(result.Payload!.Perks);
        Assert.All(result.Payload.Perks, p => Assert.All(p.Dates, d => Assert.True(d.Start is null || d.End >= d.Start)));
    }
}
