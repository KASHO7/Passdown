using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Xunit.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Opt-in live check against the real official Memberships &amp; Season Passes page (spec §14.3;
/// Phase 25). Excluded from a normal <c>dotnet test</c> run — see the <c>build-and-test</c> skill for
/// the opt-in <c>Category=LiveSmoke</c> filter. One plain, anonymous GET; no sign-in, no cart, no
/// browser. Deliberately asserts no specific price (prices legitimately change) — only that the source
/// completes, that at least one Season Pass new-purchase price is verified, and that no offer carries a
/// fabricated or zero amount or a monthly unit on a one-time price. Writes every parsed offer to the
/// test output so a live verification can be compared with the visible page.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class SeasonPassCatalogSourceLiveSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RetrieveAsync_AgainstTheLiveMembershipsAndSeasonPassesPage_VerifiesSeparateOffers()
    {
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(20) });
        var source = new SeasonPassCatalogSource(retrievalHttpClient, new SystemClock());

        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);

        output.WriteLine($"Checked {result.Evidence.TimeChecked:yyyy-MM-dd HH:mm:ss zzz} (Eastern) {result.Evidence.SourceUrl} → {result.Evidence.Status}");
        output.WriteLine($"Detail: {result.Evidence.Detail}");
        output.WriteLine($"Warnings: {result.Evidence.TechnicalDetail ?? "(none)"}");
        foreach (var product in result.Payload?.Products ?? [])
        {
            var offer = PassOffer.FromKey(product.Key.ToString());
            output.WriteLine(
                $"{product.Key} | {product.DisplayName} | amount={product.CurrentPriceAmount?.ToString() ?? "none"} ({product.CurrentDisplayPrice}) " +
                $"| unit={offer?.Unit} | reference={product.ComparisonPriceAmount?.ToString() ?? "none"} | qualifier={product.PriceQualifier ?? "none"}");
        }

        foreach (var key in result.Payload?.UnconfirmedProductKeys ?? [])
        {
            output.WriteLine($"UNCONFIRMED {key}");
        }

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(RetrievalStrategy.Html, result.Evidence.Strategy);
        Assert.NotNull(result.Payload);
        Assert.Contains(result.Payload!.Products, p =>
            PassOffer.FromKey(p.Key.ToString()) is { Product: PassProductType.SeasonPass, PriceType: PassPriceType.NewPurchase } && p.CurrentPriceAmount > 0);

        foreach (var product in result.Payload.Products)
        {
            var offer = PassOffer.FromKey(product.Key.ToString());
            Assert.NotNull(offer);
            Assert.Equal(SourceCategory.SeasonPasses, product.Key.Category);
            if (product.CurrentPriceAmount is { } amount)
            {
                Assert.True(amount > 0);
            }

            if (offer!.Unit == PassPriceUnit.OneTime)
            {
                Assert.DoesNotContain("/mo", product.CurrentDisplayPrice ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
