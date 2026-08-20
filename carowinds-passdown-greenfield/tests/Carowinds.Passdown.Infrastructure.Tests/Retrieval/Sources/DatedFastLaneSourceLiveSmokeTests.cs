using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Opt-in live check against the real official accesso endpoint documented in
/// docs/retrieval/dated-fast-lane-source.md (spec §14.3). Excluded from a normal
/// <c>dotnet test</c> run — see the <c>build-and-test</c> skill for the opt-in
/// <c>Category=LiveSmoke</c> filter. Only issues plain, anonymous POSTs against the public
/// calendar-pricing endpoint; never signs in, never adds anything to a cart, never submits
/// payment.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class DatedFastLaneSourceLiveSmokeTests
{
    [Fact]
    public async Task RetrieveAsync_ForACurrentlyPublishedNearTermDate_ConfirmsTheExactRequestedDate()
    {
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(20) });
        var source = new DatedFastLaneSource(retrievalHttpClient, new SystemClock());

        var requestedDate = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(DateTimeOffset.UtcNow).AddDays(2).DateTime);
        var request = new RetrievalRequest("dated-fast-lane.official", SourceCategory.DatedFastLane, requestedDate);

        var result = await source.RetrieveAsync(request, CancellationToken.None);

        Assert.NotEqual(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Equal(requestedDate, result.Evidence.RequestedDate);

        if (result.Evidence.Status == RetrievalStatus.Verified)
        {
            Assert.Equal(requestedDate, result.Evidence.ConfirmedDate);
            Assert.NotNull(result.Payload);
            Assert.NotEmpty(result.Payload!.Products);
            Assert.All(result.Payload.Products, p => Assert.Equal(requestedDate, p.ApplicableDate));
        }
    }
}
