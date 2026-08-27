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
/// Contract tests for <see cref="DatedBringAFriendSource"/> against sanitized, committed fixtures
/// under <c>fixtures/dated-bring-a-friend/</c> (spec §14.2) — no network access (CLAUDE.md
/// "Testing"). The "published"/"horizon" fixtures here are real, unmodified captures of the live
/// <c>getkeywordcalendar</c> response for the <c>BAFWizardDailyTickets</c> keyword, fetched
/// 2026-08-27 during Phase 15A's live re-investigation (docs/phase13-hardening.md Phase 15A
/// section) — not hand-authored. "Today" for every test is 2026-08-27.
/// </summary>
public class DatedBringAFriendSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 27);

    private static string Fixture(string relativePath) => FixtureFile.ReadText($"dated-bring-a-friend/{relativePath}");

    private static RetrievalHttpResponse JsonResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "application/json", Fixture(fixtureRelativePath));

    private static DatedBringAFriendSource BuildSource(RetrievalHttpResponse primaryResponse, out StubHttpClient httpClient)
    {
        httpClient = new StubHttpClient([primaryResponse]);
        return new DatedBringAFriendSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static DatedBringAFriendSource BuildSource(
        RetrievalHttpResponse primaryResponse,
        RetrievalHttpResponse horizonResponse,
        out StubHttpClient httpClient)
    {
        httpClient = new StubHttpClient([primaryResponse, horizonResponse]);
        return new DatedBringAFriendSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request(DateOnly date) =>
        new("dated-bring-a-friend.official", SourceCategory.DatedBringAFriend, date);

    [Fact]
    public async Task RetrieveAsync_PublishedAug29_ReturnsVerifiedWithRealExactPrice()
    {
        var requestedDate = new DateOnly(2026, 8, 29);
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out var httpClient);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(requestedDate, result.Evidence.ConfirmedDate);
        Assert.Equal(RetrievalStrategy.DirectApi, result.Evidence.Strategy);
        Assert.Single(httpClient.Requests); // only the primary call was needed

        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal("10094895", product.PackageId);
        Assert.Equal("Bring-A-Friend Ticket", product.DisplayName);
        Assert.Equal(39.00m, product.Price);
        Assert.Equal(requestedDate, product.ApplicableDate);
    }

    [Fact]
    public async Task RetrieveAsync_PublishedAug30_ReturnsRealExactPrice()
    {
        var requestedDate = new DateOnly(2026, 8, 30);
        var source = BuildSource(JsonResponse("published-2026-08-30.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal(35.00m, product.Price);
        Assert.Equal(requestedDate, product.ApplicableDate);
    }

    [Fact]
    public async Task RetrieveAsync_IgnoresTheCoReturnedBringAFriendDuoPackage()
    {
        // Both fixtures carry a "Celebrate America! Bring-A-Friend Duo Offer" package alongside the
        // target one — this must never leak into this source's own payload (that offer is exclusively
        // owned by DatedBringAFriendDuoSource/SourceCategory.BringAFriendDuo).
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.DoesNotContain(result.Payload!.Products, p => p.DisplayName.Contains("Duo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetrieveAsync_TwoDifferentRequestedDates_RemainIsolated()
    {
        var sourceFor29th = BuildSource(JsonResponse("published-2026-08-29.json"), out _);
        var resultFor29th = await sourceFor29th.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        var sourceFor30th = BuildSource(JsonResponse("published-2026-08-30.json"), out _);
        var resultFor30th = await sourceFor30th.RetrieveAsync(Request(new DateOnly(2026, 8, 30)), CancellationToken.None);

        Assert.Equal(39.00m, resultFor29th.Payload!.Products.Single().Price);
        Assert.Equal(35.00m, resultFor30th.Payload!.Products.Single().Price);
    }

    [Fact]
    public async Task RetrieveAsync_ResponseConfirmsADifferentDateThanRequested_ReturnsErrorRatherThanSubstituting()
    {
        var requestedDate = new DateOnly(2026, 8, 30);
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out _);

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
        Assert.Equal(2, httpClient.Requests.Count);
    }

    [Fact]
    public async Task RetrieveAsync_RequestedDateWithinHorizonButNotReturned_ReturnsUnavailable()
    {
        // horizon-window.json's real dates are 08-29, 08-30, 09-05..09-07 — 08-28 is inside that
        // span but genuinely absent, the same "closed/gap day" shape the other dated sources handle.
        var requestedDate = new DateOnly(2026, 8, 28);
        var source = BuildSource(JsonResponse("no-dates-found.json"), JsonResponse("horizon-window.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_PastDate_ReturnsUnavailableWithoutAnyRequest()
    {
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out var httpClient);

        var result = await source.RetrieveAsync(Request(Today.AddDays(-1)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Empty(httpClient.Requests);
    }

    [Fact]
    public async Task RetrieveAsync_TargetPackageMissingFromResponse_ReturnsErrorDefensively()
    {
        var source = BuildSource(JsonResponse("published-missing-target-package.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NeverReadsTheLowestPriceAggregateField()
    {
        // published-2026-08-29.json's "lowest_price" is "0.00", same decoy convention as every other
        // dated fixture — this test documents that the real price (39.00) is read from
        // PS.P.CT.retail_amount, never from the date-level aggregate (spec §5.4).
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(39.00m, result.Payload!.Products.Single().Price);
    }

    [Fact]
    public async Task RetrieveAsync_MalformedJson_ReturnsError()
    {
        var source = BuildSource(JsonResponse("malformed.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_MissingServiceObject_ReturnsError()
    {
        var source = BuildSource(JsonResponse("missing-service.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NonSuccessStatusCode_ReturnsError()
    {
        var source = BuildSource(JsonResponse("published-2026-08-29.json", HttpStatusCode.ServiceUnavailable), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_TransportFailure_ReturnsErrorWithTechnicalDetail()
    {
        var httpClient = new StubHttpClient(new HttpRequestException("Connection reset"));
        var source = new DatedBringAFriendSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.NotNull(result.Evidence.TechnicalDetail);
        Assert.Contains("Connection reset", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task RetrieveAsync_CallerCancellation_PropagatesRatherThanBecomingAnErrorResult()
    {
        var httpClient = new StubHttpClient(new OperationCanceledException());
        var source = new DatedBringAFriendSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_MissingRequestedDate_Throws()
    {
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out _);

        await Assert.ThrowsAsync<ArgumentException>(
            () => source.RetrieveAsync(new RetrievalRequest("dated-bring-a-friend.official", SourceCategory.DatedBringAFriend), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }
}
