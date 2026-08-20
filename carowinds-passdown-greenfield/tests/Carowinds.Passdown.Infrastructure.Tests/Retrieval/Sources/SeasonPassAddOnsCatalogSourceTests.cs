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
/// Contract tests for <see cref="SeasonPassAddOnsCatalogSource"/> against sanitized, committed
/// fixtures under <c>fixtures/season-pass-add-ons/</c> and the shared
/// <c>fixtures/catalog-source/</c> defensive fixtures (spec §14.2) — no network access (CLAUDE.md
/// "Testing"). Fixture provenance is documented in docs/retrieval/catalog-source-inventory.md; the
/// published-page fixture was fetched fresh as this phase's narrow live revalidation immediately
/// before this adapter was written (Phase 3F3).
/// </summary>
public class SeasonPassAddOnsCatalogSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private static RetrievalHttpResponse HtmlResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "text/html", FixtureFile.ReadText(fixtureRelativePath));

    private static SeasonPassAddOnsCatalogSource BuildSource(RetrievalHttpResponse response, out FakeRetrievalHttpClient httpClient)
    {
        httpClient = new FakeRetrievalHttpClient(response);
        return new SeasonPassAddOnsCatalogSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request() => new("season-pass-add-ons.official", SourceCategory.AddOns);

    [Fact]
    public async Task RetrieveAsync_PublishedPage_ReturnsVerifiedWithAllThreeAddOns()
    {
        var source = BuildSource(HtmlResponse("season-pass-add-ons/published-page.html"), out var httpClient);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(RetrievalStrategy.Html, result.Evidence.Strategy);
        Assert.Equal(new Uri("https://www.sixflags.com/carowinds/season-pass-add-ons"), httpClient.LastRequestedUri);
        Assert.NotNull(result.Payload);
        Assert.Equal(3, result.Payload!.Products.Count);
    }

    [Fact]
    public async Task RetrieveAsync_PreservesOfficialIdsAndDisplayNames()
    {
        var source = BuildSource(HtmlResponse("season-pass-add-ons/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var fastLane = result.Payload!.Products.Single(p => p.DisplayName == "All Season Fast Lane");
        Assert.Equal("AddOns:id:9jhv1xtnfkl57pqg96gnpy", fastLane.Key.ToString());

        var dining = result.Payload.Products.Single(p => p.DisplayName == "All Season Dining Access");
        Assert.Equal("AddOns:id:6kpqup6mhy7dydtmda7wcc", dining.Key.ToString());
    }

    [Fact]
    public async Task RetrieveAsync_PreservesExplicitComparisonPriceWhenPresent()
    {
        var source = BuildSource(HtmlResponse("season-pass-add-ons/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var fastLane = result.Payload!.Products.Single(p => p.DisplayName == "All Season Fast Lane");
        Assert.Equal(499.00m, fastLane.CurrentPriceAmount);
        Assert.Equal(899.00m, fastLane.ComparisonPriceAmount);
        Assert.Equal("Only", fastLane.PriceQualifier);
    }

    [Fact]
    public async Task RetrieveAsync_LeavesComparisonPriceNullWhenAbsent()
    {
        // "All Season FunPix" is a real card with no strikethrough element at all.
        var source = BuildSource(HtmlResponse("season-pass-add-ons/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var funPix = result.Payload!.Products.Single(p => p.DisplayName == "All Season FunPix");
        Assert.Equal(49.00m, funPix.CurrentPriceAmount);
        Assert.Null(funPix.ComparisonPriceAmount);
        Assert.Equal("Only", funPix.PriceQualifier); // qualifier present even without a comparison price
    }

    [Fact]
    public async Task RetrieveAsync_NeverFabricatesDateOrUnavailabilityFields()
    {
        var source = BuildSource(HtmlResponse("season-pass-add-ons/published-page.html"), out _);

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

        Assert.Single(result.Payload!.Products);
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
        var source = BuildSource(HtmlResponse("season-pass-add-ons/published-page.html", HttpStatusCode.ServiceUnavailable), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_TransportFailure_ReturnsErrorWithTechnicalDetail()
    {
        var httpClient = new FakeRetrievalHttpClient(new HttpRequestException("Connection reset"));
        var source = new SeasonPassAddOnsCatalogSource(httpClient, new FakeClock(SampleUtcNow));

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
        var source = new SeasonPassAddOnsCatalogSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(HtmlResponse("season-pass-add-ons/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }
}
