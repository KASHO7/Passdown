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
/// Opt-in live check against the real official endpoint documented in
/// docs/retrieval/park-hours-source.md (spec §14.3). Excluded from a normal <c>dotnet test</c>
/// run — see the <c>build-and-test</c> skill for the opt-in <c>Category=LiveSmoke</c> filter.
/// Only issues a plain GET against the public CloudFront-hosted JSON feed; no sign-in, no cart.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class ParkHoursSourceLiveSmokeTests
{
    [Fact]
    public async Task RetrieveAsync_ForTomorrow_ConfirmsTheExactRequestedDate()
    {
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(15) });
        var source = new ParkHoursSource(retrievalHttpClient, new SystemClock());

        var requestedDate = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(DateTimeOffset.UtcNow).AddDays(1).DateTime);
        var request = new RetrievalRequest("park-hours.official", SourceCategory.ParkHours, requestedDate);

        var result = await source.RetrieveAsync(request, CancellationToken.None);

        Assert.NotEqual(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Equal(requestedDate, result.Evidence.RequestedDate);

        if (result.Evidence.Status == RetrievalStatus.Verified)
        {
            Assert.Equal(requestedDate, result.Evidence.ConfirmedDate);
            Assert.NotNull(result.Payload);
        }
    }
}
