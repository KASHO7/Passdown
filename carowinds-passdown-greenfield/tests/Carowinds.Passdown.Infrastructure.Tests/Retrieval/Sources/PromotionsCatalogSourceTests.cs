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
/// Contract tests for <see cref="PromotionsCatalogSource"/> against sanitized, committed fixtures
/// under <c>fixtures/promotions/</c> and the shared <c>fixtures/catalog-source/</c> defensive
/// fixtures (spec §14.2) — no network access (CLAUDE.md "Testing"). Fixture provenance and the
/// parser evidence behind this adapter is documented in
/// docs/retrieval/catalog-source-inventory.md; the published-page fixture was narrowly revalidated
/// live immediately before this adapter was written (Phase 3F4).
/// </summary>
public class PromotionsCatalogSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private static RetrievalHttpResponse HtmlResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "text/html", FixtureFile.ReadText(fixtureRelativePath));

    private static PromotionsCatalogSource BuildSource(RetrievalHttpResponse response, out FakeRetrievalHttpClient httpClient)
    {
        httpClient = new FakeRetrievalHttpClient(response);
        return new PromotionsCatalogSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request() => new("promotions.official", SourceCategory.Promotions);

    [Fact]
    public async Task RetrieveAsync_PublishedPage_ReturnsVerifiedWithBothPromotions()
    {
        var source = BuildSource(HtmlResponse("promotions/published-page.html"), out var httpClient);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(RetrievalStrategy.Html, result.Evidence.Strategy);
        Assert.Equal(new Uri("https://www.sixflags.com/carowinds/special-offers"), httpClient.LastRequestedUri);
        Assert.NotNull(result.Payload);
        Assert.Equal(2, result.Payload!.Products.Count);
    }

    [Fact]
    public async Task RetrieveAsync_PreservesOfficialIdsAndDisplayNames()
    {
        var source = BuildSource(HtmlResponse("promotions/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var bringAFriend = result.Payload!.Products.Single(p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal("Promotions:id:449e6fe8-a6ad-4af5-b162-b7835798b8ae", bringAFriend.Key.ToString());

        var goldPass = result.Payload.Products.Single(p => p.DisplayName == "2027 Gold Pass");
        Assert.Equal("Promotions:id:9d031bb4-5b2f-46ea-8da0-08d826e7bd11", goldPass.Key.ToString());
    }

    [Fact]
    public async Task RetrieveAsync_PreservesCurrentAndExplicitComparisonPricesAndQualifier()
    {
        var source = BuildSource(HtmlResponse("promotions/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var bringAFriend = result.Payload!.Products.Single(p => p.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal(35.00m, bringAFriend.CurrentPriceAmount);
        Assert.Equal("$35", bringAFriend.CurrentDisplayPrice);
        Assert.Equal(80.00m, bringAFriend.ComparisonPriceAmount);
        Assert.Equal("Starting From", bringAFriend.PriceQualifier);

        var goldPass = result.Payload.Products.Single(p => p.DisplayName == "2027 Gold Pass");
        Assert.Equal(89.00m, goldPass.CurrentPriceAmount);
        Assert.Equal(115.00m, goldPass.ComparisonPriceAmount);
        Assert.Equal("Sale Price", goldPass.PriceQualifier);
    }

    [Fact]
    public async Task RetrieveAsync_CardWithNoQualifierOrComparisonPrice_LeavesBothNullRatherThanFabricating()
    {
        var source = BuildSource(HtmlResponse("promotions/no-qualifier-card.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal("Weekday Value Ticket", product.DisplayName);
        Assert.Equal(29.00m, product.CurrentPriceAmount);
        Assert.Null(product.ComparisonPriceAmount);
        Assert.Null(product.PriceQualifier);
    }

    [Fact]
    public async Task RetrieveAsync_NeverAttachesPageLevelValidityOrUnavailabilityToAnyProduct()
    {
        var source = BuildSource(HtmlResponse("promotions/published-page.html"), out _);

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
        var source = BuildSource(HtmlResponse("promotions/published-page.html", HttpStatusCode.ServiceUnavailable), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_TransportFailure_ReturnsErrorWithTechnicalDetail()
    {
        var httpClient = new FakeRetrievalHttpClient(new HttpRequestException("Connection reset"));
        var source = new PromotionsCatalogSource(httpClient, new FakeClock(SampleUtcNow));

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
        var source = new PromotionsCatalogSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(HtmlResponse("promotions/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }
}
