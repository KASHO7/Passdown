using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Opt-in live check against the real official page documented in
/// docs/retrieval/catalog-source-inventory.md (spec §14.3). Excluded from a normal
/// <c>dotnet test</c> run — see the <c>build-and-test</c> skill for the opt-in
/// <c>Category=LiveSmoke</c> filter. A plain, anonymous GET; no sign-in, no cart, no browser.
/// Deliberately asserts no specific product count, price, or name (all can legitimately change) —
/// only that the source completes successfully and, if it returns products, that they carry sane,
/// non-fabricated fields.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class PromotionsCatalogSourceLiveSmokeTests
{
    [Fact]
    public async Task RetrieveAsync_AgainstTheLivePromotionsPage_SucceedsWithoutFabricatingProductCounts()
    {
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(20) });
        var source = new PromotionsCatalogSource(retrievalHttpClient, new SystemClock());

        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(RetrievalStrategy.Html, result.Evidence.Strategy);
        Assert.NotNull(result.Payload);

        foreach (var product in result.Payload!.Products)
        {
            Assert.False(string.IsNullOrWhiteSpace(product.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(product.Key.ToString()));
            Assert.False(product.DateSpecific);
            Assert.Null(product.ApplicableDate);
            if (product.CurrentPriceAmount is { } amount)
            {
                Assert.True(amount > 0);
            }
        }
    }
}
