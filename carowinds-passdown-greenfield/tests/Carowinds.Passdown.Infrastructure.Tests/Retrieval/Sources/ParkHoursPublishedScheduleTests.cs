using System.Net;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Phase 22E.1: <see cref="ParkHoursSource"/>'s seasonal main-gate entries and its month form, against
/// official responses recorded live on 2026-10-17 (venues/shows/events stripped) under
/// <c>fixtures/park-hours/</c> and <c>fixtures/park-hours/months/</c>. Files named <c>derived-*</c> are
/// those recordings with one documented edit each (removed, changed or malformed entries) for cases the
/// live feed did not show that day. See docs/phase22e1-published-park-schedule-coverage.md.
/// </summary>
public class ParkHoursPublishedScheduleTests
{
    /// <summary>10:00 AM EDT, 2026-10-17 — the recording day.</summary>
    private static readonly DateTimeOffset RecordedAtUtc = new(2026, 10, 17, 14, 0, 0, TimeSpan.Zero);

    private static ParkHoursSource Source(string fixture, out FakeRetrievalHttpClient http, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/json")
    {
        http = new FakeRetrievalHttpClient(new RetrievalHttpResponse(status, contentType, FixtureFile.ReadText($"park-hours/{fixture}")));
        return new ParkHoursSource(http, new FakeClock(RecordedAtUtc));
    }

    private static ParkHoursSource Throwing(Exception exception) => new(new FakeRetrievalHttpClient(exception), new FakeClock(RecordedAtUtc));

    private static SourceResult<ParkHoursPayload> On(PublishedParkHoursMonth month, int monthNumber, int day) =>
        Assert.Single(month.Dates, r => r.Evidence.RequestedDate == new DateOnly(month.Year, monthNumber, day));

    // ------------------------------------------------------------- seasonal main-gate labels (id 24)

    [Fact]
    public async Task SingleDate_WinterFestLabelledMainGate_IsVerifiedHours()
    {
        var date = new DateOnly(2026, 12, 5);
        var result = await Source("winterfest-date.json", out _).RetrieveAsync(new RetrievalRequest("park-hours.official", SourceCategory.ParkHours, date), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(new TimeOnly(12, 0), result.Payload!.OpeningTime);
        Assert.Equal(new TimeOnly(21, 0), result.Payload.ClosingTime);
        Assert.Null(result.Payload.WaterparkOpeningTime);
    }

    [Fact]
    public async Task SingleDate_NewYearsEveLabelledMainGate_ClosesAfterMidnight()
    {
        var date = new DateOnly(2026, 12, 31);
        var result = await Source("new-years-eve-date.json", out _).RetrieveAsync(new RetrievalRequest("park-hours.official", SourceCategory.ParkHours, date), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(new TimeOnly(15, 0), result.Payload!.OpeningTime);
        Assert.Equal(new TimeOnly(1, 0), result.Payload.ClosingTime);
    }

    // ------------------------------------------------------------------------------ month form

    [Fact]
    public async Task Month_RequestsTheMonthFormOnce()
    {
        await Source("months/2026-12.json", out var http).RetrieveMonthAsync(2026, 12, CancellationToken.None);

        Assert.Equal("https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=202612", http.LastRequestedUri!.ToString());
    }

    [Fact]
    public async Task Month_December_PublishedWinterFestHoursAndConfirmedClosures()
    {
        var month = await Source("months/2026-12.json", out _).RetrieveMonthAsync(2026, 12, CancellationToken.None);

        Assert.Null(month.Failure);
        Assert.Equal(31, month.Dates.Count);
        Assert.Equal(16, month.Dates.Count(r => r.Evidence.Status == RetrievalStatus.Verified));
        Assert.Equal(15, month.Dates.Count(r => r.Evidence.Status == RetrievalStatus.Closed));
        Assert.DoesNotContain(month.Dates, r => r.Evidence.Status == RetrievalStatus.NotPublished);

        var dec5 = On(month, 12, 5);
        Assert.Equal(new TimeOnly(12, 0), dec5.Payload!.OpeningTime);
        Assert.Equal(new TimeOnly(21, 0), dec5.Payload.ClosingTime);
        Assert.Equal(new DateOnly(2026, 12, 5), dec5.Evidence.ConfirmedDate);
        Assert.Equal(RetrievalStatus.Closed, On(month, 12, 1).Evidence.Status);
        Assert.Equal(RetrievalStatus.Closed, On(month, 12, 25).Evidence.Status);
        Assert.Equal(new TimeOnly(1, 0), On(month, 12, 31).Payload!.ClosingTime);
        Assert.Equal(month.Dates.OrderBy(r => r.Evidence.RequestedDate), month.Dates);
    }

    [Fact]
    public async Task Month_November_MixesParkAndWinterFestLabels()
    {
        var month = await Source("months/2026-11.json", out _).RetrieveMonthAsync(2026, 11, CancellationToken.None);

        Assert.Equal(30, month.Dates.Count);
        Assert.Equal(new TimeOnly(23, 0), On(month, 11, 1).Payload!.ClosingTime);   // "Park"
        Assert.Equal(new TimeOnly(20, 0), On(month, 11, 27).Payload!.ClosingTime);  // "Winterfest"
        Assert.Equal(new TimeOnly(0, 0), On(month, 11, 6).Payload!.ClosingTime);    // closes at midnight
    }

    [Fact]
    public async Task Month_CurrentMonth_ListsOnlyTheRemainingDays()
    {
        var month = await Source("months/2026-10.json", out _).RetrieveMonthAsync(2026, 10, CancellationToken.None);

        Assert.Null(month.Failure);
        Assert.Equal(14, month.Dates.Count);
        Assert.Equal(new DateOnly(2026, 10, 18), month.Dates[0].Evidence.RequestedDate);
        Assert.DoesNotContain(month.Dates, r => r.Evidence.RequestedDate < new DateOnly(2026, 10, 18));
    }

    [Fact]
    public async Task Month_January_OpenFlagWithoutHours_IsNotPublishedNeverClosed()
    {
        var month = await Source("months/2027-01.json", out _).RetrieveMonthAsync(2027, 1, CancellationToken.None);

        Assert.Equal(RetrievalStatus.NotPublished, On(month, 1, 1).Evidence.Status);
        Assert.Equal(RetrievalStatus.NotPublished, On(month, 1, 2).Evidence.Status);
        Assert.Equal(29, month.Dates.Count(r => r.Evidence.Status == RetrievalStatus.Closed));
    }

    [Fact]
    public async Task Month_FarFuture_EveryDateNotPublished()
    {
        var month = await Source("months/2027-02.json", out _).RetrieveMonthAsync(2027, 2, CancellationToken.None);

        Assert.Equal(28, month.Dates.Count);
        Assert.All(month.Dates, r => Assert.Equal(RetrievalStatus.NotPublished, r.Evidence.Status));
        Assert.All(month.Dates, r => Assert.Null(r.Payload));
    }

    [Fact]
    public async Task Month_HorizonMonth_EndsWhereTheFeedEnds()
    {
        var month = await Source("months/2027-10.json", out _).RetrieveMonthAsync(2027, 10, CancellationToken.None);

        Assert.Equal(17, month.Dates.Count);
        Assert.Equal(new DateOnly(2027, 10, 17), month.Dates[^1].Evidence.RequestedDate);
    }

    [Fact]
    public async Task Month_MissingDates_AreAbsentNotClosed()
    {
        var month = await Source("months/derived-2026-11-missing-dates.json", out _).RetrieveMonthAsync(2026, 11, CancellationToken.None);

        Assert.Null(month.Failure);
        Assert.Equal(27, month.Dates.Count);
        Assert.DoesNotContain(month.Dates, r => r.Evidence.RequestedDate is { Day: 14 or 15 or 16 });
    }

    [Fact]
    public async Task Month_BadEntries_UnreadableTimeIsErrorAndForeignOrInvalidDatesIgnored()
    {
        var month = await Source("months/derived-2026-12-bad-entries.json", out _).RetrieveMonthAsync(2026, 12, CancellationToken.None);

        Assert.Equal(31, month.Dates.Count);
        Assert.Equal(RetrievalStatus.Error, On(month, 12, 6).Evidence.Status);
        Assert.All(month.Dates, r => Assert.Equal(12, r.Evidence.RequestedDate!.Value.Month));
    }

    [Fact]
    public async Task Month_DuplicateDate_FirstEntryKept()
    {
        var body = """{"dates":[{"date":"12/05/2026","isParkClosed":false,"operatings":[{"operatingTypeId":24,"operatingTypeName":"Winterfest","items":[{"timeFrom":"12:00","timeTo":"21:00"}]}]},{"date":"12/05/2026","isParkClosed":true,"operatings":[]}]}""";
        var source = new ParkHoursSource(new FakeRetrievalHttpClient(new RetrievalHttpResponse(HttpStatusCode.OK, "application/json", body)), new FakeClock(RecordedAtUtc));

        var month = await source.RetrieveMonthAsync(2026, 12, CancellationToken.None);

        Assert.Equal(RetrievalStatus.Verified, Assert.Single(month.Dates).Evidence.Status);
    }

    [Theory]
    [InlineData("months/derived-empty-dates.json", HttpStatusCode.OK, "application/json")]
    [InlineData("error-not-found.json", HttpStatusCode.NotFound, "application/json")]
    [InlineData("cloudfront-lambda-error.html", HttpStatusCode.ServiceUnavailable, "text/html")]
    [InlineData("malformed.json", HttpStatusCode.OK, "application/json")]
    [InlineData("error-not-found.json", HttpStatusCode.TooManyRequests, "application/json")]
    public async Task Month_UnusableResponse_FailsTheWholeMonth(string fixture, HttpStatusCode status, string contentType)
    {
        var month = await Source(fixture, out _, status, contentType).RetrieveMonthAsync(2026, 12, CancellationToken.None);

        Assert.NotNull(month.Failure);
        Assert.Equal(RetrievalStatus.Error, month.Failure!.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), month.Failure.RequestedDate);
        Assert.Empty(month.Dates);
    }

    [Fact]
    public async Task Month_TimeoutOrNetworkFailure_FailsTheWholeMonth()
    {
        var timedOut = await Throwing(new TimeoutException("timed out")).RetrieveMonthAsync(2026, 12, CancellationToken.None);
        var offline = await Throwing(new HttpRequestException("offline")).RetrieveMonthAsync(2026, 12, CancellationToken.None);

        Assert.NotNull(timedOut.Failure);
        Assert.NotNull(offline.Failure);
        Assert.Empty(timedOut.Dates);
    }

    [Fact]
    public async Task Month_CallerCancellation_Propagates()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Throwing(new OperationCanceledException()).RetrieveMonthAsync(2026, 12, CancellationToken.None));
    }
}
