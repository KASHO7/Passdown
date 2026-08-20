using System.Net;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Contract tests for <see cref="SeasonPassCatalogSource"/> against sanitized, committed fixtures
/// under <c>fixtures/season-passes/</c> and the shared <c>fixtures/catalog-source/</c> defensive
/// fixtures (spec §14.2) — no network access (CLAUDE.md "Testing"). Fixture provenance and the
/// parser evidence behind this adapter is documented in
/// docs/retrieval/catalog-source-inventory.md; the published-page fixture was narrowly
/// revalidated live immediately before this adapter was written (Phase 3F2).
/// </summary>
public class SeasonPassCatalogSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private static RetrievalHttpResponse HtmlResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "text/html", FixtureFile.ReadText(fixtureRelativePath));

    private static SeasonPassCatalogSource BuildSource(RetrievalHttpResponse response, out FakeRetrievalHttpClient httpClient)
    {
        httpClient = new FakeRetrievalHttpClient(response);
        return new SeasonPassCatalogSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request() => new("season-passes.official", SourceCategory.SeasonPasses);

    [Fact]
    public async Task RetrieveAsync_PublishedPage_ReturnsVerifiedWithBothPasses()
    {
        var source = BuildSource(HtmlResponse("season-passes/published-page.html"), out var httpClient);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(RetrievalStrategy.Html, result.Evidence.Strategy);
        Assert.Equal(new Uri("https://www.sixflags.com/carowinds/season-passes"), httpClient.LastRequestedUri);
        Assert.NotNull(result.Payload);
        Assert.Equal(2, result.Payload!.Products.Count);
    }

    [Fact]
    public async Task RetrieveAsync_PreservesOfficialIdsAndDisplayNames()
    {
        var source = BuildSource(HtmlResponse("season-passes/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var gold = result.Payload!.Products.Single(p => p.DisplayName == "2027 Gold Pass");
        // Sanity doc id preferred over the accesso keyword (docs: more durable across pages).
        Assert.Equal("SeasonPasses:id:550e3db6-5bad-45f8-8404-5d608c367617", gold.Key.ToString());

        var prestige = result.Payload.Products.Single(p => p.DisplayName == "2027 Prestige Pass");
        Assert.Equal("SeasonPasses:id:5de73366-26f8-4c9d-8983-407dc10132ea", prestige.Key.ToString());
    }

    [Fact]
    public async Task RetrieveAsync_PreservesCurrentAndExplicitComparisonPricesAndQualifier()
    {
        var source = BuildSource(HtmlResponse("season-passes/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var gold = result.Payload!.Products.Single(p => p.DisplayName == "2027 Gold Pass");
        Assert.Equal(89.00m, gold.CurrentPriceAmount);
        Assert.Equal("$89", gold.CurrentDisplayPrice);
        Assert.Equal(115.00m, gold.ComparisonPriceAmount);
        Assert.Equal("Sale Price", gold.PriceQualifier);

        var prestige = result.Payload.Products.Single(p => p.DisplayName == "2027 Prestige Pass");
        Assert.Equal(229.00m, prestige.CurrentPriceAmount);
        Assert.Equal(275.00m, prestige.ComparisonPriceAmount);
    }

    [Fact]
    public async Task RetrieveAsync_NeverFabricatesDateOrUnavailabilityFields()
    {
        var source = BuildSource(HtmlResponse("season-passes/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.All(result.Payload!.Products, p =>
        {
            Assert.False(p.DateSpecific);
            Assert.Null(p.ApplicableDate);
            Assert.False(p.IsExplicitlyUnavailable);
            Assert.Null(p.UnavailableReason);
        });
    }

    [Fact]
    public async Task RetrieveAsync_DuplicateCardForTheSameProduct_KeepsExactlyOneDeterministically()
    {
        var source = BuildSource(HtmlResponse("catalog-source/duplicate-card-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal("2027 Gold Pass", product.DisplayName);
        Assert.Equal(89.00m, product.CurrentPriceAmount);
    }

    [Fact]
    public async Task RetrieveAsync_PageWithNoRecognizedCards_ReturnsVerifiedWithEmptyCatalogRatherThanError()
    {
        var source = BuildSource(HtmlResponse("catalog-source/no-products-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.NotNull(result.Payload);
        Assert.Empty(result.Payload!.Products);
    }

    [Fact]
    public async Task RetrieveAsync_MalformedCard_ReturnsError()
    {
        var source = BuildSource(HtmlResponse("catalog-source/malformed-card.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NonSuccessStatusCode_ReturnsError()
    {
        var source = BuildSource(HtmlResponse("season-passes/published-page.html", HttpStatusCode.ServiceUnavailable), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_TransportFailure_ReturnsErrorWithTechnicalDetail()
    {
        var httpClient = new FakeRetrievalHttpClient(new HttpRequestException("Connection reset"));
        var source = new SeasonPassCatalogSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.NotNull(result.Evidence.TechnicalDetail);
        Assert.Contains("Connection reset", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task RetrieveAsync_CallerCancellation_PropagatesRatherThanBecomingAnErrorResult()
    {
        var httpClient = new FakeRetrievalHttpClient(new OperationCanceledException());
        var source = new SeasonPassCatalogSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(HtmlResponse("season-passes/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }
}
