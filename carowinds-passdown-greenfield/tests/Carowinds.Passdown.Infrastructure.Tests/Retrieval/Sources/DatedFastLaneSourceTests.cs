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
/// Contract tests for <see cref="DatedFastLaneSource"/> against sanitized, committed fixtures
/// under <c>fixtures/dated-fast-lane/</c> (spec §14.2) — no network access (CLAUDE.md "Testing").
/// Fixture provenance and the endpoint/shape evidence behind these cases is documented in
/// docs/retrieval/dated-fast-lane-source.md. "Today" for every test is 2026-08-20.
///
/// Unlike <see cref="DatedAdmissionSourceTests"/>, this adapter makes a primary single-day request
/// first and only a second, wider "horizon" request when the primary one comes back empty (see
/// docs). <see cref="StubHttpClient"/> supports queuing one response per expected call so tests can
/// exercise both calls.
/// </summary>
public class DatedFastLaneSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 20);

    private static string Fixture(string relativePath) => FixtureFile.ReadText($"dated-fast-lane/{relativePath}");

    private static RetrievalHttpResponse JsonResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "application/json", Fixture(fixtureRelativePath));

    private static DatedFastLaneSource BuildSource(RetrievalHttpResponse primaryResponse, out StubHttpClient httpClient)
    {
        httpClient = new StubHttpClient([primaryResponse]);
        return new DatedFastLaneSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static DatedFastLaneSource BuildSource(
        RetrievalHttpResponse primaryResponse,
        RetrievalHttpResponse horizonResponse,
        out StubHttpClient httpClient)
    {
        httpClient = new StubHttpClient([primaryResponse, horizonResponse]);
        return new DatedFastLaneSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request(DateOnly date) =>
        new("dated-fast-lane.official", SourceCategory.DatedFastLane, date);

    [Fact]
    public async Task RetrieveAsync_PublishedExactDate_ReturnsVerifiedWithMultipleProducts()
    {
        var source = BuildSource(JsonResponse("published-tomorrow.json"), out var httpClient);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 21)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(new DateOnly(2026, 8, 21), result.Evidence.ConfirmedDate);
        Assert.Equal(RetrievalStrategy.DirectApi, result.Evidence.Strategy);
        Assert.NotNull(result.Payload);
        Assert.Equal(2, result.Payload!.Products.Count);
        Assert.Single(httpClient.Requests); // only the primary call was needed

        var reserve = result.Payload.Products.Single(p => p.DisplayName == "FAST LANE RESERVE");
        Assert.Equal("10094823", reserve.PackageId);
        Assert.Equal(59.00m, reserve.ExactDatePrice);
        Assert.Null(reserve.ComparisonPrice);
        Assert.Null(reserve.PriceQualifier);

        var ultimate = result.Payload.Products.Single(p => p.DisplayName == "FAST LANE ULTIMATE");
        Assert.Equal("10093917", ultimate.PackageId);
        Assert.Equal(99.00m, ultimate.ExactDatePrice);
    }

    [Fact]
    public async Task RetrieveAsync_TwoDifferentRequestedDates_RemainIsolated()
    {
        var sourceForToday = BuildSource(JsonResponse("published-today.json"), out _);
        var resultToday = await sourceForToday.RetrieveAsync(Request(Today), CancellationToken.None);

        var tomorrow = new DateOnly(2026, 8, 21);
        var sourceForTomorrow = BuildSource(JsonResponse("published-tomorrow.json"), out _);
        var resultTomorrow = await sourceForTomorrow.RetrieveAsync(Request(tomorrow), CancellationToken.None);

        var todayReserve = resultToday.Payload!.Products.Single(p => p.DisplayName == "FAST LANE RESERVE");
        Assert.Equal(49.00m, todayReserve.ExactDatePrice);
        Assert.Equal(Today, todayReserve.ApplicableDate);

        var tomorrowReserve = resultTomorrow.Payload!.Products.Single(p => p.DisplayName == "FAST LANE RESERVE");
        Assert.Equal(59.00m, tomorrowReserve.ExactDatePrice);
        Assert.Equal(tomorrow, tomorrowReserve.ApplicableDate);
    }

    [Fact]
    public async Task RetrieveAsync_ResponseConfirmsADifferentDateThanRequested_ReturnsErrorRatherThanSubstituting()
    {
        // published-today.json's single entry confirms 2026-08-20. Requesting 08-21 against it
        // must be rejected as a mismatch, not silently accepted as 08-21's data (spec §5.4).
        var requestedDate = new DateOnly(2026, 8, 21);
        var source = BuildSource(JsonResponse("published-today.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Evidence.ConfirmedDate);
        Assert.Null(result.Payload);
        Assert.Contains("confirmed", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task RetrieveAsync_RequestedDateBeyondPublishedHorizon_ReturnsNotPublished()
    {
        var requestedDate = new DateOnly(2027, 6, 15);
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("horizon-window.json"), out var httpClient);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.NotPublished, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.Equal(2, httpClient.Requests.Count); // primary miss, then the horizon probe
    }

    [Fact]
    public async Task RetrieveAsync_RequestedDateWithinHorizonButMissing_ReturnsUnavailable()
    {
        // horizon-window.json has 08-20, 08-21, then jumps to 08-29 — 08-25 falls in that gap.
        var requestedDate = new DateOnly(2026, 8, 25);
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("horizon-window.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_MultipleDynamicProductVariants_ReturnsBothWithoutHardcodingNames()
    {
        var source = BuildSource(JsonResponse("published-today.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        var names = result.Payload!.Products.Select(p => p.DisplayName).OrderBy(n => n).ToList();
        Assert.Equal(["FAST LANE RESERVE", "FAST LANE ULTIMATE"], names);
    }

    [Fact]
    public async Task RetrieveAsync_NoLiveEvidenceOfADuplicateNamedPackage_StillNeverFabricatesAComparisonPriceOrQualifier()
    {
        // Synthetic fixture: two packages sharing one name on one date has never been observed
        // live for FastLaneOnly (unlike dated admission, where it's real). Proves the adapter
        // returns each package as its own product rather than inventing admission's
        // lower/higher-comparison-price grouping for a shape it has no evidence for.
        var source = BuildSource(JsonResponse("duplicate-name-package.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(2, result.Payload!.Products.Count);
        Assert.All(result.Payload.Products, p =>
        {
            Assert.Equal("FAST LANE RESERVE", p.DisplayName);
            Assert.Null(p.ComparisonPrice);
            Assert.Null(p.PriceQualifier);
        });
        Assert.Equal([49.00m, 69.00m], result.Payload.Products.Select(p => p.ExactDatePrice).OrderBy(x => x));
    }

    [Fact]
    public async Task RetrieveAsync_MarketingLowestPriceField_IsNeverUsedAsTheExactDatePrice()
    {
        var source = BuildSource(JsonResponse("published-today.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        // published-today.json's date-level "lowest_price" is "0.00" — real per-package
        // retail_amount values ("49.00"/"85.00") must be used instead.
        Assert.Contains(result.Payload!.Products, p => p.ExactDatePrice == 49.00m);
        Assert.Contains(result.Payload.Products, p => p.ExactDatePrice == 85.00m);
    }

    [Fact]
    public async Task RetrieveAsync_PastDate_ReturnsUnavailableWithoutMakingARequest()
    {
        var yesterday = Today.AddDays(-1);
        var httpClient = new StubHttpClient(new HttpRequestException("should never be called"));
        var source = new DatedFastLaneSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(yesterday), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.Empty(httpClient.Requests);
    }

    [Fact]
    public async Task RetrieveAsync_PrimaryCallReturnsMoreThanOneEntry_ReturnsErrorDefensively()
    {
        var source = BuildSource(JsonResponse("unexpected-multiple-entries.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_PrimaryCallReturnsNoEntries_ReturnsErrorDefensively()
    {
        var source = BuildSource(JsonResponse("empty-entries.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_HorizonWindowNoDatesFound_ReturnsNotPublishedNotError()
    {
        // Phase 19B: nothing published anywhere from today onward (e.g. off-season) is a successful
        // "not currently published" check, never a retrieval failure.
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("no-dates-found.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2027, 6, 15)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.NotPublished, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_HorizonWindowFailedWithAnyOtherMessage_ReturnsErrorRatherThanGuessingAStatus()
    {
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("unexpected-failed-message.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2027, 6, 15)), CancellationToken.None);

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
        var httpClient = new StubHttpClient(new HttpRequestException("Connection reset"));
        var source = new DatedFastLaneSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.NotNull(result.Evidence.TechnicalDetail);
        Assert.Contains("Connection reset", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task RetrieveAsync_CallerCancellation_PropagatesRatherThanBecomingAnErrorResult()
    {
        var httpClient = new StubHttpClient(new OperationCanceledException());
        var source = new DatedFastLaneSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(Today), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_WithoutARequestedDate_Throws()
    {
        var source = BuildSource(JsonResponse("published-today.json"), out _);
        var request = new RetrievalRequest("dated-fast-lane.official", SourceCategory.DatedFastLane);

        await Assert.ThrowsAsync<ArgumentException>(() => source.RetrieveAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_BuildsThePrimaryRequestForTheExactRequestedDateOnly()
    {
        var source = BuildSource(JsonResponse("published-tomorrow.json"), out var httpClient);

        await source.RetrieveAsync(Request(new DateOnly(2026, 8, 21)), CancellationToken.None);

        Assert.Equal(AccessoKeywordCalendarRequestEndpointForTests, httpClient.Requests[0].Uri);
        Assert.Contains("\"keyword\":\"FastLaneOnly\"", httpClient.Requests[0].Body);
        Assert.Contains("\"display_zero_capacity\":\"0\"", httpClient.Requests[0].Body);
        Assert.Contains("\"start_date\":\"2026-08-21\"", httpClient.Requests[0].Body);
        Assert.Contains("\"end_date\":\"2026-08-21\"", httpClient.Requests[0].Body);
        Assert.Equal("https://cf-ca.buy.sixflags.com/", httpClient.Requests[0].Headers!["referer"]);
    }

    [Fact]
    public async Task RetrieveAsync_BuildsTheHorizonRequestFromTodayWhenThePrimaryCallMisses()
    {
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("horizon-window.json"), out var httpClient);

        await source.RetrieveAsync(Request(new DateOnly(2026, 8, 25)), CancellationToken.None);

        Assert.Contains("\"start_date\":\"2026-08-20\"", httpClient.Requests[1].Body);
        Assert.Contains($"\"end_date\":\"{Today.AddDays(120):yyyy-MM-dd}\"", httpClient.Requests[1].Body);
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(JsonResponse("published-today.json"), out _);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }

    private static readonly Uri AccessoKeywordCalendarRequestEndpointForTests =
        new("https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar");
}
