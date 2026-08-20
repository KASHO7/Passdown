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
/// Contract tests for <see cref="ParkHoursSource"/> against sanitized, committed fixtures under
/// <c>fixtures/park-hours/</c> (spec §14.2) — no network access (CLAUDE.md "Testing"). Fixture
/// provenance and the endpoint/shape evidence behind these cases is documented in
/// docs/retrieval/park-hours-source.md.
/// </summary>
public class ParkHoursSourceTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private static RetrievalHttpResponse JsonResponse(string fixtureRelativePath, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "application/json", FixtureFile.ReadText($"park-hours/{fixtureRelativePath}"));

    private static ParkHoursSource BuildSource(RetrievalHttpResponse response, out FakeRetrievalHttpClient httpClient)
    {
        httpClient = new FakeRetrievalHttpClient(response);
        return new ParkHoursSource(httpClient, new FakeClock(SampleUtcNow));
    }

    private static RetrievalRequest Request(DateOnly date) =>
        new("park-hours.official", SourceCategory.ParkHours, date);

    [Fact]
    public async Task RetrieveAsync_PublishedOpenDate_ReturnsVerifiedWithOpeningAndClosingTimes()
    {
        var requestedDate = new DateOnly(2026, 8, 20);
        var source = BuildSource(JsonResponse("open-date.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(requestedDate, result.Evidence.RequestedDate);
        Assert.Equal(requestedDate, result.Evidence.ConfirmedDate);
        Assert.Equal(RetrievalStrategy.DirectApi, result.Evidence.Strategy);
        Assert.NotNull(result.Payload);
        Assert.Equal(new TimeOnly(10, 0), result.Payload!.OpeningTime);
        Assert.Equal(new TimeOnly(19, 0), result.Payload!.ClosingTime);
    }

    [Fact]
    public async Task RetrieveAsync_PublishedClosedDate_ReturnsClosedWithoutPayload()
    {
        var requestedDate = new DateOnly(2026, 8, 24);
        var source = BuildSource(JsonResponse("closed-date.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Closed, result.Evidence.Status);
        Assert.Equal(requestedDate, result.Evidence.ConfirmedDate);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_FutureUnpublishedDate_ReturnsNotPublished()
    {
        var requestedDate = new DateOnly(2027, 6, 15);
        var source = BuildSource(JsonResponse("not-published-date.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.NotPublished, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_ParkOperatingEntryWithBlankTimes_ReturnsNotPublished()
    {
        var requestedDate = new DateOnly(2026, 8, 20);
        var source = BuildSource(JsonResponse("blank-park-item.json"), out _);

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.NotPublished, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_ResponseConfirmsADifferentDateThanRequested_ReturnsErrorRatherThanSubstituting()
    {
        var requestedDate = new DateOnly(2026, 8, 21);
        var source = BuildSource(JsonResponse("open-date.json"), out _); // fixture confirms 08/20/2026

        var result = await source.RetrieveAsync(Request(requestedDate), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Equal(requestedDate, result.Evidence.RequestedDate);
        Assert.Null(result.Evidence.ConfirmedDate);
        Assert.Null(result.Payload);
        Assert.Contains("confirmed", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task RetrieveAsync_MalformedJsonBody_ReturnsError()
    {
        var source = BuildSource(JsonResponse("malformed.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 20)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_EmptyDatesArray_ReturnsError()
    {
        var source = BuildSource(JsonResponse("empty-dates.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 20)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_ErrorBodyWithSuccessStatusCode_ReturnsErrorDefensively()
    {
        // Mirrors the official page's own client code, which treats an `{"error": ...}` body as a
        // failed lookup even when the transport status happens to be 200 (docs/retrieval/park-hours-source.md).
        var source = BuildSource(JsonResponse("error-not-found.json", HttpStatusCode.OK), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 20)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NotFoundStatusCode_ReturnsError()
    {
        var source = BuildSource(JsonResponse("error-not-found.json", HttpStatusCode.NotFound), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2020, 1, 1)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_NonJsonServiceUnavailableResponse_ReturnsError()
    {
        var response = new RetrievalHttpResponse(
            HttpStatusCode.ServiceUnavailable,
            "text/html",
            FixtureFile.ReadText("park-hours/cloudfront-lambda-error.html"));
        var source = BuildSource(response, out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 20)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task RetrieveAsync_TransportFailure_ReturnsErrorWithTechnicalDetail()
    {
        var httpClient = new FakeRetrievalHttpClient(new HttpRequestException("Connection reset"));
        var source = new ParkHoursSource(httpClient, new FakeClock(SampleUtcNow));

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 20)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
        Assert.NotNull(result.Evidence.TechnicalDetail);
        Assert.Contains("Connection reset", result.Evidence.TechnicalDetail);
    }

    [Fact]
    public async Task RetrieveAsync_CallerCancellation_PropagatesRatherThanBecomingAnErrorResult()
    {
        var httpClient = new FakeRetrievalHttpClient(new OperationCanceledException());
        var source = new ParkHoursSource(httpClient, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.RetrieveAsync(Request(new DateOnly(2026, 8, 20)), CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_WithoutARequestedDate_Throws()
    {
        var source = BuildSource(JsonResponse("open-date.json"), out _);
        var request = new RetrievalRequest("park-hours.official", SourceCategory.ParkHours);

        await Assert.ThrowsAsync<ArgumentException>(() => source.RetrieveAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task RetrieveAsync_BuildsTheDirectlyObservedEndpointWithParkIdAndDailyDateFormat()
    {
        var source = BuildSource(JsonResponse("open-date.json"), out var httpClient);

        await source.RetrieveAsync(Request(new DateOnly(2026, 8, 20)), CancellationToken.None);

        Assert.NotNull(httpClient.LastRequestedUri);
        Assert.Equal(
            "https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=20260820",
            httpClient.LastRequestedUri!.ToString());
    }

    [Fact]
    public async Task RetrieveAsync_RecordsTimeCheckedInEasternTime()
    {
        var source = BuildSource(JsonResponse("open-date.json"), out _);

        var result = await source.RetrieveAsync(Request(new DateOnly(2026, 8, 20)), CancellationToken.None);

        Assert.Equal(EasternTimeZone.ConvertFromUtc(SampleUtcNow), result.Evidence.TimeChecked);
    }
}
