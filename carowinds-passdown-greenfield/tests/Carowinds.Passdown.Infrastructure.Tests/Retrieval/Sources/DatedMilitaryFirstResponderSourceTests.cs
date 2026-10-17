using System.Net;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Phase 21A contract tests for <see cref="DatedMilitaryFirstResponderSource"/> against committed
/// fixtures under <c>fixtures/dated-military-first-responder/</c> — no network access. The
/// <c>published-*</c>, <c>closed-*</c>, <c>past-*</c> and <c>horizon-window</c> fixtures are live
/// captures of the anonymous <c>MilitaryWizardDailyTickets</c> keyword taken 2026-10-16 (client IP
/// and session id replaced); <c>hand-authored-*</c> fixtures are derived from them to cover shapes
/// not observed live. "Today" for every test is 2026-10-16.
/// </summary>
public class DatedMilitaryFirstResponderSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 16);

    private static RetrievalHttpResponse Json(string fixture, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "application/json", FixtureFile.ReadText($"dated-military-first-responder/{fixture}"));

    private static DatedMilitaryFirstResponderSource Source(out StubHttpClient httpClient, params RetrievalHttpResponse[] responses)
    {
        httpClient = new StubHttpClient(responses);
        return new DatedMilitaryFirstResponderSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request(DateOnly date) =>
        new("dated-military-first-responder.official", SourceCategory.DatedMilitaryFirstResponder, date);

    [Fact]
    public void Identity_IsItsOwnDatedCategory_NeverTheCatalogCardOrDailyAdmission()
    {
        var source = Source(out _);

        Assert.Equal("dated-military-first-responder.official", source.SourceId);
        Assert.Equal(SourceCategory.DatedMilitaryFirstResponder, source.Category);
        Assert.NotEqual(SourceCategory.MilitaryFirstResponder, source.Category);
        Assert.NotEqual(SourceCategory.DatedAdmissionTickets, source.Category);
    }

    [Fact]
    public async Task RetrieveAsync_Today_ReturnsVerifiedExactPricePerEligibilityWithEvidence()
    {
        var source = Source(out var httpClient, Json("published-2026-10-16.json"));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(Today, result.Evidence.RequestedDate);
        Assert.Equal(Today, result.Evidence.ConfirmedDate);
        Assert.Equal(RetrievalStrategy.DirectApi, result.Evidence.Strategy);
        Assert.Equal("Carowinds Dated Military & First Responder Tickets", result.Evidence.SourceName);
        Assert.Equal(new Uri("https://www.sixflags.com/carowinds/military-discounts"), result.Evidence.SourceUrl);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(-4)), result.Evidence.TimeChecked);
        Assert.Single(httpClient.Requests);

        Assert.Collection(
            result.Payload!.Products,
            military =>
            {
                Assert.Equal(MilitaryFirstResponderEligibility.Military, military.Eligibility);
                Assert.Equal("Military Ticket", military.DisplayName);
                Assert.Equal("10098283", military.PackageId);
                Assert.Equal(55.00m, military.Price);
                Assert.Equal(Today, military.ApplicableDate);
            },
            firstResponder =>
            {
                Assert.Equal(MilitaryFirstResponderEligibility.FirstResponder, firstResponder.Eligibility);
                Assert.Equal("First Responder Ticket", firstResponder.DisplayName);
                Assert.Equal(55.00m, firstResponder.Price);
            });
    }

    [Fact]
    public async Task RetrieveAsync_DifferentFutureDate_ReturnsThatDatesOwnPrice()
    {
        var date = new DateOnly(2026, 10, 17);
        var source = Source(out _, Json("published-2026-10-17.json"));

        var result = await source.RetrieveAsync(Request(date), CancellationToken.None);

        Assert.All(result.Payload!.Products, p =>
        {
            Assert.Equal(60.00m, p.Price);
            Assert.Equal(date, p.ApplicableDate);
        });
    }

    [Fact]
    public async Task RetrieveAsync_SeasonalAndPlainPackagesAgree_OneProductPerEligibilityPreferringThePlainPackage()
    {
        var date = new DateOnly(2026, 11, 8);
        var source = Source(out _, Json("published-2026-11-08-seasonal-and-plain.json"));

        var result = await source.RetrieveAsync(Request(date), CancellationToken.None);

        Assert.Equal(2, result.Payload!.Products.Count);
        var military = result.Payload.Products.Single(p => p.Eligibility == MilitaryFirstResponderEligibility.Military);
        Assert.Equal("10099674", military.PackageId); // the unprefixed "Military Ticket" package
        Assert.Equal(40.00m, military.Price);
    }

    [Fact]
    public async Task RetrieveAsync_EligibilitiesPricedDifferently_KeepsEachOwnExactPrice()
    {
        var source = Source(out _, Json("hand-authored-eligibility-prices-differ.json"));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(55.00m, result.Payload!.Products.Single(p => p.Eligibility == MilitaryFirstResponderEligibility.Military).Price);
        Assert.Equal(50.00m, result.Payload.Products.Single(p => p.Eligibility == MilitaryFirstResponderEligibility.FirstResponder).Price);
    }

    [Fact]
    public async Task RetrieveAsync_OneEligibilityListsTwoPrices_NeverPicksAMinimumForIt()
    {
        var source = Source(out _, Json("hand-authored-military-ambiguous.json"));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal(MilitaryFirstResponderEligibility.FirstResponder, product.Eligibility);
        Assert.Contains("Military lists more than one price", result.Evidence.Detail);
    }

    [Fact]
    public async Task RetrieveAsync_EveryEligibilityAmbiguous_ReturnsUnavailableNotErrorAndNoPrice()
    {
        var source = Source(out _, Json("hand-authored-both-ambiguous.json"));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_ClosedDateInsidePublishedHorizon_ReturnsUnavailable()
    {
        var date = new DateOnly(2026, 10, 19);
        var source = Source(out var httpClient, Json("closed-2026-10-19.json"), Json("horizon-window.json"));

        var result = await source.RetrieveAsync(Request(date), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.Equal(2, httpClient.Requests.Count);
    }

    [Fact]
    public async Task RetrieveAsync_DateBeyondPublishedHorizon_ReturnsNotPublished()
    {
        var date = new DateOnly(2027, 2, 13);
        var source = Source(out _, Json("closed-2026-10-19.json"), Json("horizon-window.json"));

        var result = await source.RetrieveAsync(Request(date), CancellationToken.None);

        Assert.Equal(RetrievalStatus.NotPublished, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NoDatesPublishedFromToday_ReturnsNotPublishedNotError()
    {
        var source = Source(out _, Json("closed-2026-10-19.json"), Json("closed-2026-10-19.json"));

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 10, 20)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.NotPublished, result.Evidence.Status);
    }

    [Fact]
    public async Task RetrieveAsync_PastDate_ReturnsUnavailableWithoutAnyRequest()
    {
        var source = Source(out var httpClient);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 10, 10)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Empty(httpClient.Requests);
    }

    [Fact]
    public async Task RetrieveAsync_NoMilitaryOrFirstResponderPackage_ReturnsErrorNeverAFabricatedPrice()
    {
        var source = Source(out _, Json("hand-authored-no-target-package.json"));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_UnparseableAmount_ReturnsError()
    {
        var source = Source(out _, Json("hand-authored-unparseable-amount.json"));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_ResponseConfirmsAnotherDate_ReturnsErrorRatherThanSubstituting()
    {
        var source = Source(out _, Json("hand-authored-confirms-other-date.json"));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_ServerError_ReturnsError()
    {
        var source = Source(out _, new RetrievalHttpResponse(HttpStatusCode.InternalServerError, "text/plain", "oops"));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
    }

    [Fact]
    public async Task RetrieveAsync_TransportFailure_ReturnsError()
    {
        var httpClient = new StubHttpClient(new HttpRequestException("offline"));
        var source = new DatedMilitaryFirstResponderSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
    }

    [Fact]
    public async Task RetrieveAsync_MalformedJson_ReturnsError()
    {
        var source = Source(out _, new RetrievalHttpResponse(HttpStatusCode.OK, "application/json", "{not json"));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
    }

    /// <summary>The request is the same anonymous storefront call every dated source makes — no ID.me, login, cookie, cart or session.</summary>
    [Fact]
    public async Task RetrieveAsync_SendsOnlyAnonymousStorefrontRequest_NoAuthentication()
    {
        var source = Source(out var httpClient, Json("published-2026-10-16.json"));

        await source.RetrieveAsync(Request(Today), CancellationToken.None);

        var request = Assert.Single(httpClient.Requests);
        Assert.Equal(new Uri("https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar"), request.Uri);
        Assert.Contains("\"keyword\":\"MilitaryWizardDailyTickets\"", request.Body);
        Assert.DoesNotContain("cart_", request.Body);
        Assert.DoesNotContain("session_id", request.Body);
        Assert.DoesNotContain(request.Headers!.Keys, key =>
            key.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetrieveAsync_NeverReadsTheLowestPriceAggregate()
    {
        // The fixture's date-level lowest_price is 55.00, yet First Responder's own package (50.00) is
        // reported — prices come only from each package's retail_amount.
        var source = Source(out _, Json("hand-authored-eligibility-prices-differ.json"));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Contains(result.Payload!.Products, p => p.Price == 50.00m);
    }
}
