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
/// Contract tests for <see cref="DatedBringAFriendDuoSource"/> against sanitized, committed fixtures
/// under <c>fixtures/dated-bring-a-friend-duo/</c> (spec §14.2) — no network access (CLAUDE.md
/// "Testing"). Every "published" fixture here is a real, unmodified capture of the live
/// <c>getkeywordcalendar</c> response for the <c>SUBAF50</c> keyword, fetched 2026-08-27 during
/// Phase 13C's live reconfirmation (docs/retrieval/special-ticket-sources.md) — not hand-authored.
/// "Today" for every test is 2026-08-27.
/// </summary>
public class DatedBringAFriendDuoSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 27);

    private static string Fixture(string relativePath) => FixtureFile.ReadText($"dated-bring-a-friend-duo/{relativePath}");

    private static RetrievalHttpResponse JsonResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "application/json", Fixture(fixtureRelativePath));

    private static DatedBringAFriendDuoSource BuildSource(RetrievalHttpResponse primaryResponse, out StubHttpClient httpClient)
    {
        httpClient = new StubHttpClient([primaryResponse]);
        return new DatedBringAFriendDuoSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static DatedBringAFriendDuoSource BuildSource(
        RetrievalHttpResponse primaryResponse,
        RetrievalHttpResponse horizonResponse,
        out StubHttpClient httpClient)
    {
        httpClient = new StubHttpClient([primaryResponse, horizonResponse]);
        return new DatedBringAFriendDuoSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request(DateOnly date) =>
        new("dated-bring-a-friend-duo.official", SourceCategory.BringAFriendDuo, date);

    [Fact]
    public async Task RetrieveAsync_PublishedAug29_ReturnsVerifiedWithRealPerPersonPrice()
    {
        var requestedDate = new DateOnly(2026, 8, 29);
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out var httpClient);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(requestedDate, result.Evidence.ConfirmedDate);
        Assert.Equal(RetrievalStrategy.DirectApi, result.Evidence.Strategy);
        Assert.Single(httpClient.Requests); // only the primary call was needed

        var product = Assert.Single(result.Payload!.Products);
        Assert.Equal("10097207", product.PackageId);
        Assert.Equal("Celebrate America! Bring-A-Friend Duo Offer", product.DisplayName);
        Assert.Equal(25.00m, product.PerPersonPrice);
        Assert.Null(product.ComparisonPrice);
        Assert.Null(product.PriceQualifier);
        Assert.Equal(requestedDate, product.ApplicableDate);
    }

    [Fact]
    public async Task RetrieveAsync_TwoDifferentRequestedDates_RemainIsolated()
    {
        var sourceFor29th = BuildSource(JsonResponse("published-2026-08-29.json"), out _);
        var resultFor29th = await sourceFor29th.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        var sourceFor30th = BuildSource(JsonResponse("published-2026-08-30.json"), out _);
        var resultFor30th = await sourceFor30th.RetrieveAsync(Request(new DateOnly(2026, 8, 30)), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 8, 29), resultFor29th.Payload!.Products.Single().ApplicableDate);
        Assert.Equal(new DateOnly(2026, 8, 30), resultFor30th.Payload!.Products.Single().ApplicableDate);
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
    public async Task RetrieveAsync_PrimaryCallReturnsMultipleEntries_ReturnsErrorDefensively()
    {
        var source = BuildSource(JsonResponse("unexpected-multiple-entries.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NeverReadsTheLowestPriceAggregateField()
    {
        // published-2026-08-29.json's "lowest_price" is "0.00", same decoy convention as every other
        // dated fixture — this test documents that the real per-person price (25.00) is read from
        // PS.P.CT.retail_amount, never from the date-level aggregate (spec §5.4).
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(25.00m, result.Payload!.Products.Single().PerPersonPrice);
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
        var source = new DatedBringAFriendDuoSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.NotNull(result.Evidence.TechnicalDetail);
        Assert.Contains("Connection reset", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task RetrieveAsync_CallerCancellation_PropagatesRatherThanBecomingAnErrorResult()
    {
        var httpClient = new StubHttpClient(new OperationCanceledException());
        var source = new DatedBringAFriendDuoSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_MissingRequestedDate_Throws()
    {
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out _);

        await Assert.ThrowsAsync<ArgumentException>(
            () => source.RetrieveAsync(new RetrievalRequest("dated-bring-a-friend-duo.official", SourceCategory.BringAFriendDuo), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(JsonResponse("published-2026-08-29.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 29)), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }
}
