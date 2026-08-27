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
/// Contract tests for <see cref="MilitaryFirstResponderCatalogSource"/> against sanitized, committed
/// fixtures under <c>fixtures/military-first-responder/</c> (spec §14.2) — no network access
/// (CLAUDE.md "Testing"). Fixture provenance and the live investigation behind this adapter are
/// documented in docs/retrieval/special-ticket-sources.md (Phase 13C, live-fetched 2026-08-27).
/// </summary>
public class MilitaryFirstResponderCatalogSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);

    private static RetrievalHttpResponse HtmlResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "text/html", FixtureFile.ReadText(fixtureRelativePath));

    private static MilitaryFirstResponderCatalogSource BuildSource(RetrievalHttpResponse response, out FakeRetrievalHttpClient httpClient)
    {
        httpClient = new FakeRetrievalHttpClient(response);
        return new MilitaryFirstResponderCatalogSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request() => new("military-first-responder.official", SourceCategory.MilitaryFirstResponder);

    [Fact]
    public async Task RetrieveAsync_PublishedPage_ReturnsVerifiedWithOneTicket_DedupingTheResponsiveDuplicateCard()
    {
        var source = BuildSource(HtmlResponse("military-first-responder/published-page.html"), out var httpClient);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(RetrievalStrategy.Html, result.Evidence.Strategy);
        Assert.Equal(new Uri("https://www.sixflags.com/carowinds/military-discounts"), httpClient.LastRequestedUri);
        Assert.NotNull(result.Payload);
        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal("Military & First Responder Tickets", product.DisplayName);
    }

    [Fact]
    public async Task RetrieveAsync_PreservesStartingFromQualifierAndGatePriceComparison()
    {
        var source = BuildSource(HtmlResponse("military-first-responder/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal(37.00m, product.CurrentPriceAmount);
        Assert.Equal("$37", product.CurrentDisplayPrice);
        Assert.Equal(80.00m, product.ComparisonPriceAmount);
        Assert.Equal("Starting From", product.PriceQualifier);
    }

    [Fact]
    public async Task RetrieveAsync_IdentityFallsBackToAccessoKeyword_NoSanityIdOnThisPage()
    {
        var source = BuildSource(HtmlResponse("military-first-responder/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal("MilitaryFirstResponder:id:militarywizard", product.Key.ToString());
    }

    [Fact]
    public async Task RetrieveAsync_NeverFabricatesDateOrUnavailabilityFields()
    {
        var source = BuildSource(HtmlResponse("military-first-responder/published-page.html"), out _);

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
        var source = BuildSource(HtmlResponse("military-first-responder/no-products-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.NotNull(result.Payload);
        Assert.Empty(result.Payload!.Products);
    }

    [Fact]
    public async Task RetrieveAsync_MalformedCard_ReturnsError()
    {
        var source = BuildSource(HtmlResponse("military-first-responder/malformed-card.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NonSuccessStatusCode_ReturnsError()
    {
        var source = BuildSource(HtmlResponse("military-first-responder/published-page.html", HttpStatusCode.ServiceUnavailable), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_TransportFailure_ReturnsErrorWithTechnicalDetail()
    {
        var httpClient = new FakeRetrievalHttpClient(new HttpRequestException("Connection reset"));
        var source = new MilitaryFirstResponderCatalogSource(httpClient, new FakeClock(SampleUtcNow));

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
        var source = new MilitaryFirstResponderCatalogSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(HtmlResponse("military-first-responder/published-page.html"), out _);

        var result = await source.RetrieveAsync(Request(), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }
}
