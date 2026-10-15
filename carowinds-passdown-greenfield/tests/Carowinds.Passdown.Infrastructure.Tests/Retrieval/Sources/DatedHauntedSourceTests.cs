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
/// Contract tests for <see cref="DatedHauntedSource"/> against sanitized, committed fixtures under
/// <c>fixtures/dated-haunted/</c> (spec §14.2) — no network access (CLAUDE.md "Testing"). Fixture
/// provenance and the endpoint/shape evidence behind these cases is documented in
/// docs/retrieval/dated-haunted-source.md; the two published-date fixtures and the horizon-window
/// fixture are real observed 2026 SCarowinds responses captured during that investigation, not
/// invented. "Today" for every test is 2026-08-20 — genuinely off-season relative to the fixtures'
/// real 2026-09-11…2026-11-01 SCarowinds window, matching the actual investigation timing.
///
/// Structurally mirrors <see cref="DatedFastLaneSourceTests"/> since <see cref="DatedHauntedSource"/>
/// reuses the same primary-then-horizon design, independently justified for this keyword in the
/// docs above rather than assumed from Fast Lane.
/// </summary>
public class DatedHauntedSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 20);

    private static RetrievalHttpResponse JsonResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "application/json", FixtureFile.ReadText($"dated-haunted/{fixtureRelativePath}"));

    private static DatedHauntedSource BuildSource(RetrievalHttpResponse primaryResponse, out StubHttpClient httpClient)
    {
        httpClient = new StubHttpClient([primaryResponse, NoConjuring()]);
        return new DatedHauntedSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static DatedHauntedSource BuildSource(
        RetrievalHttpResponse primaryResponse,
        RetrievalHttpResponse horizonResponse,
        out StubHttpClient httpClient)
    {
        httpClient = new StubHttpClient([primaryResponse, NoConjuring(), horizonResponse]);
        return new DatedHauntedSource(httpClient, new FakeClock(SampleUtcNow));
    }

    /// <summary>Phase 18D: every exact-date check also asks the official `THECONJURING` keyword; this is its real "No dates found" shape.</summary>
    private static RetrievalHttpResponse NoConjuring() => JsonResponse("no-dates-found.json");

    private static RetrievalRequest Request(DateOnly date) =>
        new("dated-haunted.official", SourceCategory.HauntedAttractions, date);

    [Fact]
    public async Task RetrieveAsync_PublishedExactDate_ReturnsVerifiedWithMultipleProducts()
    {
        var requestedDate = new DateOnly(2026, 9, 11);
        var source = BuildSource(JsonResponse("published-2026-09-11.json"), out var httpClient);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(requestedDate, result.Evidence.ConfirmedDate);
        Assert.Equal(RetrievalStrategy.DirectApi, result.Evidence.Strategy);
        Assert.NotNull(result.Payload);
        Assert.Equal(2, result.Payload!.Products.Count);
        Assert.Equal(2, httpClient.Requests.Count); // HAPWizard + THECONJURING; no horizon probe needed

        var pass = result.Payload.Products.Single(p => p.DisplayName == "Haunted Attractions Pass");
        Assert.Equal("10098520", pass.PackageId);
        Assert.Equal(10.00m, pass.ExactDatePrice);
        Assert.Null(pass.ComparisonPrice);
        Assert.Null(pass.PriceQualifier);

        var express = result.Payload.Products.Single(p => p.DisplayName == "Haunted Attractions Express Pass");
        Assert.Equal("10098526", express.PackageId);
        Assert.Equal(49.00m, express.ExactDatePrice);
    }

    [Fact]
    public async Task RetrieveAsync_TwoDifferentRequestedDates_RemainIsolated()
    {
        var friday = new DateOnly(2026, 9, 11);
        var sourceForFriday = BuildSource(JsonResponse("published-2026-09-11.json"), out _);
        var resultFriday = await sourceForFriday.RetrieveAsync(Request(friday), CancellationToken.None);

        var saturday = new DateOnly(2026, 9, 12);
        var sourceForSaturday = BuildSource(JsonResponse("published-2026-09-12.json"), out _);
        var resultSaturday = await sourceForSaturday.RetrieveAsync(Request(saturday), CancellationToken.None);

        var fridayPass = resultFriday.Payload!.Products.Single(p => p.DisplayName == "Haunted Attractions Pass");
        Assert.Equal(10.00m, fridayPass.ExactDatePrice);
        Assert.Equal(friday, fridayPass.ApplicableDate);

        var saturdayPass = resultSaturday.Payload!.Products.Single(p => p.DisplayName == "Haunted Attractions Pass");
        Assert.Equal(15.00m, saturdayPass.ExactDatePrice);
        Assert.Equal(saturday, saturdayPass.ApplicableDate);
    }

    [Fact]
    public async Task RetrieveAsync_ResponseConfirmsADifferentDateThanRequested_ReturnsErrorRatherThanSubstituting()
    {
        // published-2026-09-11.json's single entry confirms 2026-09-11. Requesting 09-12 against it
        // must be rejected as a mismatch, not silently accepted as 09-12's data (spec §5.4).
        var requestedDate = new DateOnly(2026, 9, 12);
        var source = BuildSource(JsonResponse("published-2026-09-11.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Evidence.ConfirmedDate);
        Assert.Null(result.Payload);
        Assert.Contains("confirmed", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task RetrieveAsync_TodayBeforeTheSeasonStarts_ReturnsUnavailableNotError()
    {
        // Real, independently observed off-season shape: requesting "today" (2026-08-20, before the
        // real 2026-09-11 season start) against a horizon window whose max date is 09-19 classifies
        // as Unavailable — a season not having started yet is a recognized-but-not-offered date, the
        // same as any other gap, not a distinct status (docs/retrieval/dated-haunted-source.md).
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("horizon-window.json"), out var httpClient);

        var result = await source.RetrieveAsync(Request(Today), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.Equal(3, httpClient.Requests.Count);
    }

    [Fact]
    public async Task RetrieveAsync_NonOperatingNightWithinTheSeason_ReturnsUnavailable()
    {
        // 2026-09-13 (Sunday) is a real, independently confirmed dark night between two real
        // published nights (09-11 Fri, 09-19 Sat) in horizon-window.json.
        var requestedDate = new DateOnly(2026, 9, 13);
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("horizon-window.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_RequestedDateBeyondPublishedHorizon_ReturnsNotPublished()
    {
        var requestedDate = new DateOnly(2027, 6, 15);
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("horizon-window.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.NotPublished, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_MultipleDynamicProductVariants_ReturnsBothWithoutHardcodingNames()
    {
        var source = BuildSource(JsonResponse("published-2026-09-11.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None);

        var names = result.Payload!.Products.Select(p => p.DisplayName).OrderBy(n => n).ToList();
        Assert.Equal(["Haunted Attractions Express Pass", "Haunted Attractions Pass"], names);
    }

    [Fact]
    public async Task RetrieveAsync_NoLiveEvidenceOfADuplicateNamedPackage_StillNeverFabricatesAComparisonPriceOrQualifier()
    {
        // Synthetic fixture: two packages sharing one name on one date has never been observed live
        // for HAPWizard (unlike dated admission, where it's real). Proves the adapter returns each
        // package as its own product rather than inventing admission's comparison-price grouping.
        var source = BuildSource(JsonResponse("duplicate-name-package.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(2, result.Payload!.Products.Count);
        Assert.All(result.Payload.Products, p =>
        {
            Assert.Equal("Haunted Attractions Pass", p.DisplayName);
            Assert.Null(p.ComparisonPrice);
            Assert.Null(p.PriceQualifier);
        });
        Assert.Equal([10.00m, 18.00m], result.Payload.Products.Select(p => p.ExactDatePrice).OrderBy(x => x));
    }

    [Fact]
    public async Task RetrieveAsync_MarketingLowestPriceField_IsNeverUsedAsTheExactDatePrice()
    {
        var source = BuildSource(JsonResponse("published-2026-09-11.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None);

        // published-2026-09-11.json's date-level "lowest_price" is "0.00" — real per-package
        // retail_amount values ("10.00"/"49.00") must be used instead.
        Assert.Contains(result.Payload!.Products, p => p.ExactDatePrice == 10.00m);
        Assert.Contains(result.Payload.Products, p => p.ExactDatePrice == 49.00m);
    }

    [Fact]
    public async Task RetrieveAsync_PastDate_ReturnsUnavailableWithoutMakingARequest()
    {
        var yesterday = Today.AddDays(-1);
        var httpClient = new StubHttpClient(new HttpRequestException("should never be called"));
        var source = new DatedHauntedSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(yesterday), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.Empty(httpClient.Requests);
    }

    [Fact]
    public async Task RetrieveAsync_PrimaryCallReturnsMoreThanOneEntry_ReturnsErrorDefensively()
    {
        var source = BuildSource(JsonResponse("unexpected-multiple-entries.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_PrimaryCallReturnsNoEntries_ReturnsErrorDefensively()
    {
        var source = BuildSource(JsonResponse("empty-entries.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_HorizonWindowEntirelyFailed_ReturnsErrorRatherThanGuessingAStatus()
    {
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("no-dates-found.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2027, 6, 15)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_MalformedJsonBody_ReturnsError()
    {
        var source = BuildSource(JsonResponse("malformed.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_MissingServiceObject_ReturnsError()
    {
        var source = BuildSource(JsonResponse("missing-service.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NonSuccessStatusCode_ReturnsError()
    {
        var source = BuildSource(JsonResponse("no-dates-found.json", HttpStatusCode.BadRequest), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_TransportFailure_ReturnsErrorWithTechnicalDetail()
    {
        var httpClient = new StubHttpClient(new HttpRequestException("Connection reset"));
        var source = new DatedHauntedSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.NotNull(result.Evidence.TechnicalDetail);
        Assert.Contains("Connection reset", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task RetrieveAsync_CallerCancellation_PropagatesRatherThanBecomingAnErrorResult()
    {
        var httpClient = new StubHttpClient(new OperationCanceledException());
        var source = new DatedHauntedSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_WithoutARequestedDate_Throws()
    {
        var source = BuildSource(JsonResponse("published-2026-09-11.json"), out _);
        var request = new RetrievalRequest("dated-haunted.official", SourceCategory.HauntedAttractions);

        await Assert.ThrowsAsync<ArgumentException>(() => source.RetrieveAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_BuildsThePrimaryRequestForTheExactRequestedDateOnly()
    {
        var requestedDate = new DateOnly(2026, 9, 11);
        var source = BuildSource(JsonResponse("published-2026-09-11.json"), out var httpClient);

        await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(new Uri("https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar"), httpClient.Requests[0].Uri);
        Assert.Contains("\"keyword\":\"HAPWizard\"", httpClient.Requests[0].Body);
        Assert.Contains("\"display_zero_capacity\":\"0\"", httpClient.Requests[0].Body);
        Assert.Contains("\"start_date\":\"2026-09-11\"", httpClient.Requests[0].Body);
        Assert.Contains("\"end_date\":\"2026-09-11\"", httpClient.Requests[0].Body);
        Assert.Equal("https://cf-ca.buy.sixflags.com/", httpClient.Requests[0].Headers!["referer"]);
    }

    [Fact]
    public async Task RetrieveAsync_BuildsTheHorizonRequestFromTodayWhenThePrimaryCallMisses()
    {
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("horizon-window.json"), out var httpClient);

        await source.RetrieveAsync(Request(new DateOnly(2026, 9, 13)), CancellationToken.None);

        Assert.Contains("\"keyword\":\"HAPWizard\"", httpClient.Requests[2].Body);
        Assert.Contains("\"start_date\":\"2026-08-20\"", httpClient.Requests[2].Body);
        Assert.Contains($"\"end_date\":\"{Today.AddDays(120):yyyy-MM-dd}\"", httpClient.Requests[2].Body);
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(JsonResponse("published-2026-09-11.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 9, 11)), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }

    // ---- Phase 18D: "The Conjuring: Beyond Fear" (official keyword THECONJURING) ----

    [Fact]
    public async Task RetrieveAsync_ConjuringPublishedForTheNight_IsMergedWithItsOfficialIdentityAndExactPrice()
    {
        // Real 2026-10-17 responses for both official keywords (fixtures/dated-haunted/*2026-10-17*).
        // Before Phase 18D the adapter never asked THECONJURING, so this product could not appear.
        var requestedDate = new DateOnly(2026, 10, 17);
        var httpClient = new StubHttpClient([JsonResponse("published-2026-10-17.json"), JsonResponse("conjuring-published-2026-10-17.json")]);
        var source = new DatedHauntedSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(requestedDate, result.Evidence.ConfirmedDate);
        Assert.Equal(3, result.Payload!.Products.Count);
        var conjuring = result.Payload.Products.Single(p => p.DisplayName == "The Conjuring: Beyond Fear");
        Assert.Equal("10098308", conjuring.PackageId);
        Assert.Equal(30.00m, conjuring.ExactDatePrice);
        Assert.Null(conjuring.ComparisonPrice);
        Assert.Null(conjuring.PriceQualifier);
        Assert.Equal(requestedDate, conjuring.ApplicableDate);
        Assert.Contains(result.Payload.Products, p => p.DisplayName == "Haunted Attractions Pass" && p.ExactDatePrice == 25.00m);

        Assert.Contains("\"keyword\":\"THECONJURING\"", httpClient.Requests[1].Body);
        Assert.Contains("\"start_date\":\"2026-10-17\"", httpClient.Requests[1].Body);
        Assert.Contains("\"end_date\":\"2026-10-17\"", httpClient.Requests[1].Body);
    }

    [Fact]
    public async Task RetrieveAsync_ConjuringCallFails_FailsTheWholeCategoryInsteadOfReturningAPartialVerifiedResult()
    {
        var httpClient = new StubHttpClient([JsonResponse("published-2026-10-17.json"), JsonResponse("malformed.json")]);
        var source = new DatedHauntedSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 10, 17)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_ConjuringConfirmsADifferentDate_ReturnsErrorRatherThanSubstituting()
    {
        var httpClient = new StubHttpClient([JsonResponse("no-dates-found.json"), JsonResponse("conjuring-published-2026-10-17.json")]);
        var source = new DatedHauntedSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 10, 16)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_OnlyConjuringPublishedForTheNight_IsStillVerifiedWithoutAHorizonProbe()
    {
        var httpClient = new StubHttpClient([JsonResponse("no-dates-found.json"), JsonResponse("conjuring-published-2026-10-17.json")]);
        var source = new DatedHauntedSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 10, 17)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal("The Conjuring: Beyond Fear", Assert.Single(result.Payload!.Products).DisplayName);
        Assert.Equal(2, httpClient.Requests.Count);
    }
}
