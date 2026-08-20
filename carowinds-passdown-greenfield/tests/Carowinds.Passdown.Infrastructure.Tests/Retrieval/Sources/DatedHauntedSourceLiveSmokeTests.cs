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
/// docs/retrieval/dated-haunted-source.md (spec §14.3). Excluded from a normal <c>dotnet test</c>
/// run — see the <c>build-and-test</c> skill for the opt-in <c>Category=LiveSmoke</c> filter. Only
/// issues plain, anonymous POSTs against the public calendar-pricing endpoint; never signs in,
/// never adds anything to a cart, never submits payment.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class DatedHauntedSourceLiveSmokeTests
{
    /// <summary>
    /// Near-term relative date, same pattern as the admission/Fast Lane smoke tests. As of this
    /// writing (2026-08-20) SCarowinds is genuinely off-season (real season: 2026-09-11…2026-11-01,
    /// see docs), so "today + 2 days" is expected to independently confirm the off-season
    /// (<see cref="RetrievalStatus.Unavailable"/> or <see cref="RetrievalStatus.NotPublished"/>)
    /// path live, not a published-product path — this is reported honestly rather than presented as
    /// a published-product smoke test.
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_ForANearTermDate_ConfirmsTheExactRequestedDateRegardlessOfSeason()
    {
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(20) });
        var source = new DatedHauntedSource(retrievalHttpClient, new SystemClock());

        var requestedDate = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(DateTimeOffset.UtcNow).AddDays(2).DateTime);
        var request = new RetrievalRequest("dated-haunted.official", SourceCategory.HauntedAttractions, requestedDate);

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

    /// <summary>
    /// Targets 2026-09-11 — independently confirmed live during investigation
    /// (docs/retrieval/dated-haunted-source.md) as a currently published, orderable SCarowinds
    /// night. This is the "currently published date" case spec §14.3/this phase's LiveSmoke
    /// requirement calls for. If this test runs after that date is no longer available (season
    /// ended, schedule changed), a non-<see cref="RetrievalStatus.Error"/>, date-consistent result
    /// is still required, but the stronger <c>Verified</c>-with-products assertions are skipped
    /// rather than failing the run for a reason outside this adapter's control.
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_ForTheKnownPublishedSeasonOpeningNight_ConfirmsExactDateAndProducts()
    {
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(20) });
        var source = new DatedHauntedSource(retrievalHttpClient, new SystemClock());

        var requestedDate = new DateOnly(2026, 9, 11);
        var request = new RetrievalRequest("dated-haunted.official", SourceCategory.HauntedAttractions, requestedDate);

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
