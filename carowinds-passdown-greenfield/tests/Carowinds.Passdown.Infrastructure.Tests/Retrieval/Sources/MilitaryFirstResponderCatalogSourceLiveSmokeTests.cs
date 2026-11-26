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
/// Phase 31B opt-in live check against the real official Military &amp; First Responders page.
/// Excluded from a normal <c>dotnet test</c> run (<c>Category=LiveSmoke</c>). One plain, anonymous GET —
/// no sign-in, no ID.me, no cart. Asserts no specific price; only that the page is read (never a
/// verified empty catalog while it shows offers), that every offer keeps a keyword identity, and prints
/// the offers and their important details for comparison with the visible page.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class MilitaryFirstResponderCatalogSourceLiveSmokeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RetrieveAsync_AgainstTheLiveMilitaryPage_ReadsTheOffersUnderKeywordIdentities()
    {
        using var httpClient = new HttpClient();
        var retrievalHttpClient = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(20) });
        var source = new MilitaryFirstResponderCatalogSource(retrievalHttpClient, new SystemClock());

        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), CancellationToken.None);

        output.WriteLine($"Checked {result.Evidence.TimeChecked:yyyy-MM-dd HH:mm:ss zzz} (Eastern) {result.Evidence.SourceUrl} → {result.Evidence.Status}: {result.Evidence.Detail}");
        foreach (var product in result.Payload?.Products ?? [])
        {
            output.WriteLine($"{product.Key} | {product.DisplayName} | {product.CurrentDisplayPrice} | qualifier={product.PriceQualifier ?? "none"}");
        }

        foreach (var details in result.Payload?.ProductDetails?.Products ?? [])
        {
            output.WriteLine($"DETAILS {details.Title} ({details.Keyword})");
            foreach (var statement in details.Statements)
            {
                output.WriteLine($"  [{statement.Kind}] {statement.Text}");
            }
        }

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.NotEmpty(result.Payload!.Products);
        Assert.All(result.Payload.Products, p => Assert.StartsWith("id:", p.Key.Value, StringComparison.Ordinal));
        Assert.All(result.Payload.Products, p => Assert.True(p.CurrentPriceAmount is > 0m));
    }
}
