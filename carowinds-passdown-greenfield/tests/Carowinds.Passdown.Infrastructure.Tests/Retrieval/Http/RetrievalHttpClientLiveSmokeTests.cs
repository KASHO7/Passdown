using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Http;

/// <summary>
/// Opt-in live check that the shared HTTP transport can reach a real official source (spec
/// §14.3). Excluded from a normal <c>dotnet test</c> run — see the <c>build-and-test</c> skill
/// for the <c>Category=LiveSmoke</c> filter. Only issues a plain GET against the park-hours page
/// documented in docs/phase1-discovery.md; no parsing, no cart/payment/sign-in interaction.
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class RetrievalHttpClientLiveSmokeTests
{
    [Fact]
    public async Task GetAsync_ReachesTheOfficialParkHoursPage()
    {
        using var httpClient = new HttpClient();
        var client = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(15) });

        var response = await client.GetAsync(new Uri("https://www.sixflags.com/carowinds/park-hours"), CancellationToken.None);

        Assert.True(response.IsSuccessStatusCode, $"Expected a successful response, got {response.StatusCode}.");
        Assert.False(string.IsNullOrWhiteSpace(response.Body));
    }
}
