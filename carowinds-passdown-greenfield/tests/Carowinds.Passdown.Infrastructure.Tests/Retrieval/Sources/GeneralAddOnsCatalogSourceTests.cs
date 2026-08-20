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
/// Contract tests for <see cref="GeneralAddOnsCatalogSource"/> against sanitized, committed
/// fixtures under <c>fixtures/general-add-ons/</c> (spec §14.2) — no network access (CLAUDE.md
/// "Testing"). Fixture provenance and the parser evidence behind this adapter is documented in
/// docs/retrieval/catalog-source-inventory.md; the published-page fixture was narrowly revalidated
/// live immediately before this adapter was written (Phase 3F5).
/// </summary>
public class GeneralAddOnsCatalogSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private static RetrievalHttpResponse HtmlResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "text/html", FixtureFile.ReadText(fixtureRelativePath));

    private static GeneralAddOnsCatalogSource BuildSource(RetrievalHttpResponse response, out FakeRetrievalHttpClient httpClient)
    {
        httpClient = new FakeRetrievalHttpClient(response);
        return new GeneralAddOnsCatalogSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request() => new("general-add-ons.official", SourceCategory.AddOns);

    [Fact]
    public async Task RetrieveAsync_PublishedPage_ReturnsVerifiedWithBothAddOns()
    {
        var source = BuildSource(HtmlResponse("general-add-ons/published-page.html"), out var httpClient);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(RetrievalStrategy.Html, result.Evidence.Strategy);
        Assert.Equal(new Uri("https://www.sixflags.com/carowinds/add-ons"), httpClient.LastRequestedUri);
        Assert.NotNull(result.Payload);
        Assert.Equal(2, result.Payload!.Products.Count);
    }

    [Fact]
    public async Task RetrieveAsync_PreservesIdentityFromTheAccessoKeywordAndDisplayName()
    {
        var source = BuildSource(HtmlResponse("general-add-ons/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        // No Sanity doc id exists on this page (docs), so identity falls back to the accesso keyword.
        var single = result.Payload!.Products.Single(p => p.DisplayName == "Single Day FunPix");
        Assert.Equal("AddOns:id:2026singlephoto", single.Key.ToString());

        var allSeason = result.Payload.Products.Single(p => p.DisplayName == "All Season FunPix");
        Assert.Equal("AddOns:id:27addonsphoto", allSeason.Key.ToString());
    }

    [Fact]
    public async Task RetrieveAsync_PreservesCurrentPrice()
    {
        var source = BuildSource(HtmlResponse("general-add-ons/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var single = result.Payload!.Products.Single(p => p.DisplayName == "Single Day FunPix");
        Assert.Equal(19.99m, single.CurrentPriceAmount);
        Assert.Equal("$19.99", single.CurrentDisplayPrice);

        var allSeason = result.Payload.Products.Single(p => p.DisplayName == "All Season FunPix");
        Assert.Equal(49.00m, allSeason.CurrentPriceAmount);
        Assert.Equal("$49", allSeason.CurrentDisplayPrice);
    }

    [Fact]
    public async Task RetrieveAsync_NoComparisonPriceOrQualifierElementExistsOnThisPage_BothRemainNullRatherThanFabricated()
    {
        var source = BuildSource(HtmlResponse("general-add-ons/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.All(result.Payload!.Products, p =>
        {
            Assert.Null(p.ComparisonPriceAmount);
            Assert.Null(p.PriceQualifier);
        });
    }

    [Fact]
    public async Task RetrieveAsync_NeverFabricatesDateOrUnavailabilityFields()
    {
        var source = BuildSource(HtmlResponse("general-add-ons/published-page.html"), out _);

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
        var source = BuildSource(HtmlResponse("general-add-ons/duplicate-card-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal("Single Day FunPix", product.DisplayName);
        Assert.Equal(19.99m, product.CurrentPriceAmount);
    }

    [Fact]
    public async Task RetrieveAsync_PageWithNoRecognizedCards_ReturnsVerifiedWithEmptyCatalogRatherThanError()
    {
        var source = BuildSource(HtmlResponse("general-add-ons/no-products-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.NotNull(result.Payload);
        Assert.Empty(result.Payload!.Products);
    }

    [Fact]
    public async Task RetrieveAsync_MalformedCard_ReturnsError()
    {
        var source = BuildSource(HtmlResponse("general-add-ons/malformed-card.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NonSuccessStatusCode_ReturnsError()
    {
        var source = BuildSource(HtmlResponse("general-add-ons/published-page.html", HttpStatusCode.ServiceUnavailable), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_TransportFailure_ReturnsErrorWithTechnicalDetail()
    {
        var httpClient = new FakeRetrievalHttpClient(new HttpRequestException("Connection reset"));
        var source = new GeneralAddOnsCatalogSource(httpClient, new FakeClock(SampleUtcNow));

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
        var source = new GeneralAddOnsCatalogSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(HtmlResponse("general-add-ons/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }
}
