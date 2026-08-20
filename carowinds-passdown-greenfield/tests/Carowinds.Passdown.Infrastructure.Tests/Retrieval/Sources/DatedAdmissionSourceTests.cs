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
/// Contract tests for <see cref="DatedAdmissionSource"/> against sanitized, committed fixtures
/// under <c>fixtures/dated-admission/</c> (spec §14.2) — no network access (CLAUDE.md "Testing").
/// Fixture provenance and the endpoint/shape evidence behind these cases is documented in
/// docs/retrieval/dated-admission-source.md. "Today" for every test is 2026-08-20, matching the
/// fixtures' published window.
/// </summary>
public class DatedAdmissionSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 20);

    private static RetrievalHttpResponse JsonResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "application/json", FixtureFile.ReadText($"dated-admission/{fixtureRelativePath}"));

    private static DatedAdmissionSource BuildSource(RetrievalHttpResponse response, out FakeRetrievalHttpClient httpClient)
    {
        httpClient = new FakeRetrievalHttpClient(response);
        return new DatedAdmissionSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request(DateOnly date) =>
        new("dated-admission.official", SourceCategory.DatedAdmissionTickets, date);

    [Fact]
    public async Task RetrieveAsync_PublishedDateWithSinglePackage_ReturnsVerifiedWithExactDatePrice()
    {
        var source = BuildSource(JsonResponse("single-date-object.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(Today, result.Evidence.RequestedDate);
        Assert.Equal(Today, result.Evidence.ConfirmedDate);
        Assert.Equal(RetrievalStrategy.DirectApi, result.Evidence.Strategy);
        Assert.NotNull(result.Payload);
        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal("10094884", product.PackageId);
        Assert.Equal("Park Ticket", product.DisplayName);
        Assert.Equal(49.00m, product.ExactDatePrice);
        Assert.Null(product.ComparisonPrice);
        Assert.Null(product.PriceQualifier);
        Assert.Equal(Today, product.ApplicableDate);
    }

    [Fact]
    public async Task RetrieveAsync_TwoDifferentDatesAgainstSameWindow_ReturnsIsolatedResults()
    {
        var sourceForToday = BuildSource(JsonResponse("published-window.json"), out _);
        var resultToday = await sourceForToday.RetrieveAsync(Request(Today), CancellationToken.None);

        var tomorrow = new DateOnly(2026, 8, 21);
        var sourceForTomorrow = BuildSource(JsonResponse("published-window.json"), out _);
        var resultTomorrow = await sourceForTomorrow.RetrieveAsync(Request(tomorrow), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, resultToday.Evidence.Status);
        var todayProduct = Assert.Single(resultToday.Payload!.Products);
        Assert.Equal(49.00m, todayProduct.ExactDatePrice);
        Assert.Null(todayProduct.ComparisonPrice);

        Assert.Equal(RetrievalStatus.Verified, resultTomorrow.Evidence.Status);
        Assert.Equal(3, resultTomorrow.Payload!.Products.Count);
        Assert.All(resultTomorrow.Payload.Products, p => Assert.Equal(tomorrow, p.ApplicableDate));
    }

    [Fact]
    public async Task RetrieveAsync_DateWithMultipleProductsAndComparisonPricing_ReturnsComparisonPriceWithoutFabricatedQualifier()
    {
        var requestedDate = new DateOnly(2026, 8, 21);
        var source = BuildSource(JsonResponse("published-window.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(3, result.Payload!.Products.Count);

        // The live endpoint never carries an explicit qualifier field for a second price, so
        // PriceQualifier must stay null rather than a guessed label like "Same-day pricing" (spec
        // §5.2's "never fabricate or silently substitute"; see
        // docs/retrieval/dated-admission-source.md's "PriceQualifier is not populated" section).
        var parkTicket = result.Payload.Products.Single(p => p.DisplayName == "Park Ticket");
        Assert.Equal("10094885", parkTicket.PackageId);
        Assert.Equal(49.00m, parkTicket.ExactDatePrice);
        Assert.Equal(55.00m, parkTicket.ComparisonPrice);
        Assert.Null(parkTicket.PriceQualifier);

        var mealBundle = result.Payload.Products.Single(p => p.DisplayName == "Ticket + Meal, Snack & Drink Bundle");
        Assert.Equal(69.00m, mealBundle.ExactDatePrice);
        Assert.Equal(75.00m, mealBundle.ComparisonPrice);
        Assert.Null(mealBundle.PriceQualifier);

        var fastLaneBundle = result.Payload.Products.Single(p => p.DisplayName == "Ticket + Fast Lane Reserve Bundle");
        Assert.Equal(89.00m, fastLaneBundle.ExactDatePrice);
        Assert.Equal(95.00m, fastLaneBundle.ComparisonPrice);
        Assert.Null(fastLaneBundle.PriceQualifier);
    }

    [Fact]
    public async Task RetrieveAsync_RequestedDateWithinPublishedRangeButMissing_ReturnsUnavailable()
    {
        // published-window.json has 08-20, 08-21, then jumps to 08-29 (a real closed-date gap
        // pattern — docs/retrieval/dated-admission-source.md). 08-25 falls inside that gap.
        var requestedDate = new DateOnly(2026, 8, 25);
        var source = BuildSource(JsonResponse("published-window.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(result.Evidence.ConfirmedDate);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_RequestedDateBeyondPublishedHorizon_ReturnsNotPublished()
    {
        var requestedDate = new DateOnly(2027, 6, 15);
        var source = BuildSource(JsonResponse("published-window.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.NotPublished, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_ResponseNeverConfirmsRequestedDate_RejectsRatherThanSubstitutingAnotherDatesData()
    {
        // single-date-object.json only ever confirms 2026-08-20. Requesting 08-21 must never
        // return 08-20's product/price data as if it were confirmed for 08-21 (spec §5.4).
        var requestedDate = new DateOnly(2026, 8, 21);
        var source = BuildSource(JsonResponse("single-date-object.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.NotEqual(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Null(result.Evidence.ConfirmedDate);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_PastDate_ReturnsUnavailableWithoutMakingARequest()
    {
        var yesterday = Today.AddDays(-1);
        var httpClient = new FakeRetrievalHttpClient(new HttpRequestException("should never be called"));
        var source = new DatedAdmissionSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(yesterday), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.Null(httpClient.LastRequestedUri);
    }

    [Fact]
    public async Task RetrieveAsync_MarketingLowestPriceField_IsNeverUsedAsTheExactDatePrice()
    {
        var source = BuildSource(JsonResponse("decoy-lowest-price.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        var product = Assert.Single(result.Payload!.Products);
        // Fixture's date-level "lowest_price" is a decoy of "19.99" — the real per-package
        // retail_amount ("49.00") must be used instead.
        Assert.Equal(49.00m, product.ExactDatePrice);
    }

    [Fact]
    public async Task RetrieveAsync_EntireWindowFailed_ReturnsErrorRatherThanGuessingAStatus()
    {
        var source = BuildSource(JsonResponse("no-dates-found.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_MalformedJsonBody_ReturnsError()
    {
        var source = BuildSource(JsonResponse("malformed.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_EmptyEntries_ReturnsError()
    {
        var source = BuildSource(JsonResponse("empty-entries.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_MissingServiceObject_ReturnsError()
    {
        var source = BuildSource(JsonResponse("missing-service.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NonSuccessStatusCode_ReturnsError()
    {
        var source = BuildSource(JsonResponse("no-dates-found.json", HttpStatusCode.BadRequest), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_TransportFailure_ReturnsErrorWithTechnicalDetail()
    {
        var httpClient = new FakeRetrievalHttpClient(new HttpRequestException("Connection reset"));
        var source = new DatedAdmissionSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.NotNull(result.Evidence.TechnicalDetail);
        Assert.Contains("Connection reset", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task RetrieveAsync_CallerCancellation_PropagatesRatherThanBecomingAnErrorResult()
    {
        var httpClient = new FakeRetrievalHttpClient(new OperationCanceledException());
        var source = new DatedAdmissionSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(Today), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_WithoutARequestedDate_Throws()
    {
        var source = BuildSource(JsonResponse("single-date-object.json"), out _);
        var request = new RetrievalRequest("dated-admission.official", SourceCategory.DatedAdmissionTickets);

        await Assert.ThrowsAsync<ArgumentException>(() => source.RetrieveAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_BuildsRequestFromTodayThroughTheHorizonPadding()
    {
        var source = BuildSource(JsonResponse("published-window.json"), out var httpClient);

        await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(new Uri("https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar"), httpClient.LastRequestedUri);
        Assert.NotNull(httpClient.LastPostedBody);
        Assert.Contains("\"keyword\":\"SnapWizardDailyTickets\"", httpClient.LastPostedBody);
        Assert.Contains("\"start_date\":\"2026-08-20\"", httpClient.LastPostedBody);
        Assert.Contains($"\"end_date\":\"{Today.AddDays(120):yyyy-MM-dd}\"", httpClient.LastPostedBody);
        Assert.NotNull(httpClient.LastPostedHeaders);
        Assert.Equal("https://cf-ca.buy.sixflags.com/", httpClient.LastPostedHeaders!["referer"]);
    }

    [Fact]
    public async Task RetrieveAsync_RequestedDateBeyondHorizonPadding_ExtendsWindowToCoverIt()
    {
        var requestedDate = new DateOnly(2027, 6, 15);
        var source = BuildSource(JsonResponse("no-dates-found.json"), out var httpClient);

        await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Contains("\"start_date\":\"2026-08-20\"", httpClient.LastPostedBody);
        Assert.Contains("\"end_date\":\"2027-06-15\"", httpClient.LastPostedBody);
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(JsonResponse("single-date-object.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }
}
