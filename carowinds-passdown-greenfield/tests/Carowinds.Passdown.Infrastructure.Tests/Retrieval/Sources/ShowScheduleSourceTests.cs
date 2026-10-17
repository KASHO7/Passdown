using System.Net;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Phase 21C contract tests for <see cref="ShowScheduleSource"/> against committed fixtures under
/// <c>fixtures/show-schedules/</c> — trimmed live responses captured 2026-10-16 plus hand-authored edge
/// cases (docs/phase21c-official-show-schedules.md). No network access.
/// </summary>
public class ShowScheduleSourceTests
{
    /// <summary>2026-10-16 3:00 PM Eastern.</summary>
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 10, 16, 19, 0, 0, TimeSpan.Zero);

    private static RetrievalHttpResponse Fixture(string name, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode, "application/json", FixtureFile.ReadText($"show-schedules/{name}"));

    private static ShowScheduleSource Source(RetrievalHttpResponse response, out FakeRetrievalHttpClient http, DateTimeOffset? now = null)
    {
        http = new FakeRetrievalHttpClient(response);
        return new ShowScheduleSource(http, new FakeClock(now ?? SampleUtcNow));
    }

    private static Task<SourceResult<ShowSchedulePayload>> Retrieve(ShowScheduleSource source, DateOnly date) =>
        source.RetrieveAsync(new RetrievalRequest("show-schedule.official", SourceCategory.ShowSchedule, date), CancellationToken.None);

    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    [Fact]
    public async Task ValidSchedule_ReturnsVerifiedShowsWithAllTimesVenuesAndOfficialIds()
    {
        var source = Source(Fixture("scarowinds-2026-10-17.json"), out var http);

        var result = await Retrieve(source, new DateOnly(2026, 10, 17));

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Equal(new DateOnly(2026, 10, 17), result.Evidence.ConfirmedDate);
        Assert.Equal(RetrievalStrategy.DirectApi, result.Evidence.Strategy);
        Assert.Equal("https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=20261017", http.LastRequestedUri!.ToString());

        var payload = Assert.IsType<ShowSchedulePayload>(result.Payload);
        Assert.Equal(11, payload.Shows.Count);
        Assert.Equal(0, payload.ListedWithoutTimesCount);

        var wake = Assert.Single(payload.Shows, s => s.ShowId == "SHOW-030-00122");
        Assert.Equal("Wake the Dead", wake.Name);
        Assert.Equal("Carowinds Theatre", wake.Venue);
        Assert.Equal([T(19), T(20), T(21, 30), T(22, 30)], wake.Times);

        // Trailing spaces in official venue text are cleaned for display.
        Assert.Equal("CarnEVIL: Thrills Zone", payload.Shows.Single(s => s.ShowId == "SHOW-030-00187").Venue);
        Assert.Equal(16, payload.Shows.Single(s => s.ShowId == "SHOW-030-00101").Times.Count);
    }

    [Fact]
    public async Task ShowsListedWithoutTimes_AreNotPerformances_AndAreCountedOnly()
    {
        // Live 2026-10-16: an evening-only SCarowinds night lists the daytime shows with blank times.
        var source = Source(Fixture("evening-only-2026-10-16.json"), out _);

        var result = await Retrieve(source, new DateOnly(2026, 10, 16));

        var payload = Assert.IsType<ShowSchedulePayload>(result.Payload);
        Assert.Equal(6, payload.Shows.Count);
        Assert.Equal(5, payload.ListedWithoutTimesCount);
        Assert.DoesNotContain(payload.Shows, s => s.Name == "Costume Contest");
        Assert.All(payload.Shows, s => Assert.NotEmpty(s.Times));
        Assert.Equal("The Resurgence", payload.Shows[0].Name); // 6:30 PM — earliest first
    }

    [Fact]
    public async Task AllListedShowsWithoutTimes_IsNotPublished_NeverAnEmptySchedule()
    {
        var source = Source(Fixture("listed-without-times-2026-12-05.json"), out _);

        var result = await Retrieve(source, new DateOnly(2026, 12, 5));

        Assert.Equal(RetrievalStatus.NotPublished, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task FarFutureDate_WithoutHoursOrTimes_IsNotPublished()
    {
        var source = Source(Fixture("far-future-2027-10-15.json"), out _);

        var result = await Retrieve(source, new DateOnly(2027, 10, 15));

        Assert.Equal(RetrievalStatus.NotPublished, result.Evidence.Status);
    }

    [Fact]
    public async Task ExplicitlyEmptySchedule_OnPublishedOpenDay_IsVerifiedNoShows()
    {
        var source = Source(Fixture("hand-authored-explicitly-empty-2026-10-24.json"), out _);

        var result = await Retrieve(source, new DateOnly(2026, 10, 24));

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.Empty(Assert.IsType<ShowSchedulePayload>(result.Payload).Shows);
    }

    [Fact]
    public async Task ClosedDate_IsClosed()
    {
        var source = Source(Fixture("closed-2026-10-19.json"), out _);

        var result = await Retrieve(source, new DateOnly(2026, 10, 19));

        Assert.Equal(RetrievalStatus.Closed, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task DuplicateIds_MergeIntoOneShow_ButSameNameAtAnotherVenueStaysDistinct()
    {
        var source = Source(Fixture("hand-authored-duplicate-ids-2026-10-17.json"), out _);

        var payload = (await Retrieve(source, new DateOnly(2026, 10, 17))).Payload!;

        Assert.Equal(2, payload.Shows.Count);
        var merged = payload.Shows.Single(s => s.ShowId == "SHOW-030-00122");
        Assert.Equal([T(19), T(20), T(21, 30)], merged.Times);
        Assert.Equal("Carowinds Theatre", merged.Venue);
        Assert.Equal("Plaza Stage", payload.Shows.Single(s => s.ShowId == "SHOW-030-00900").Venue);
    }

    [Fact]
    public async Task MissingVenueAndMissingIds_UseNullVenueAndNormalizedFallbackIdentity_AndLateNightOrdersLast()
    {
        var source = Source(Fixture("hand-authored-missing-venue-and-late-night-2026-10-17.json"), out _);

        var payload = (await Retrieve(source, new DateOnly(2026, 10, 17))).Payload!;

        var slide = payload.Shows.Single(s => s.ShowId == "SHOW-030-00187");
        Assert.Null(slide.Venue);
        Assert.Equal([T(19, 30), T(23, 30), T(0, 15)], slide.Times);

        var parade = payload.Shows.Single(s => s.Name == "Pop-Up Parade");
        Assert.Equal("name:pop-up parade|main street", parade.ShowId);
    }

    [Fact]
    public async Task UnparseableTime_IsError_NeverAPartialSchedule()
    {
        var source = Source(Fixture("hand-authored-unparseable-time-2026-10-17.json"), out _);

        var result = await Retrieve(source, new DateOnly(2026, 10, 17));

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task DateMismatch_IsError_NeverAnotherDatesSchedule()
    {
        var source = Source(Fixture("hand-authored-date-mismatch-2026-10-18.json"), out _);

        var result = await Retrieve(source, new DateOnly(2026, 10, 17));

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task InvalidJson_IsError()
    {
        var source = Source(Fixture("hand-authored-malformed.json"), out _);

        var result = await Retrieve(source, new DateOnly(2026, 10, 17));

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
    }

    [Fact]
    public async Task HttpFailure_AndTimeout_AreErrors()
    {
        var notFound = Source(new RetrievalHttpResponse(HttpStatusCode.NotFound, "application/json", "{\"error\":\"Not Found\"}"), out _);
        Assert.Equal(RetrievalStatus.Error, (await Retrieve(notFound, new DateOnly(2026, 10, 17))).Evidence.Status);

        var timeout = new ShowScheduleSource(new FakeRetrievalHttpClient(new TimeoutException("timeout")), new FakeClock(SampleUtcNow));
        Assert.Equal(RetrievalStatus.Error, (await Retrieve(timeout, new DateOnly(2026, 10, 17))).Evidence.Status);

        var offline = new ShowScheduleSource(new FakeRetrievalHttpClient(new HttpRequestException("offline")), new FakeClock(SampleUtcNow));
        Assert.Equal(RetrievalStatus.Error, (await Retrieve(offline, new DateOnly(2026, 10, 17))).Evidence.Status);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        var source = new ShowScheduleSource(new FakeRetrievalHttpClient(new OperationCanceledException()), new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Retrieve(source, new DateOnly(2026, 10, 17)));
    }

    [Fact]
    public async Task HistoricalDate_IsNeverRequested()
    {
        var source = Source(Fixture("summer-2026-07-04.json"), out var http);

        var result = await Retrieve(source, new DateOnly(2026, 7, 4));

        Assert.Equal(RetrievalStatus.Unavailable, result.Evidence.Status);
        Assert.Null(http.LastRequestedUri);
    }

    [Fact]
    public async Task SeasonalSummerSchedule_ParsesWhenRequestedAsCurrent()
    {
        var source = Source(Fixture("summer-2026-07-04.json"), out _, now: new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero));

        var payload = (await Retrieve(source, new DateOnly(2026, 7, 4))).Payload!;

        Assert.Contains(payload.Shows, s => s.Name == "Fireworks 4th of July" && s.Times.SequenceEqual([T(21, 30)]) && s.Venue is null);
        Assert.DoesNotContain(payload.Shows, s => s.Name == "Crossroads Meet and Greet"); // listed without times
    }

    [Fact]
    public async Task ChangedAndRemovedPerformances_AreReportedExactlyAsTheFeedNowSays()
    {
        var source = Source(Fixture("hand-authored-changed-and-removed-2026-10-17.json"), out _);

        var payload = (await Retrieve(source, new DateOnly(2026, 10, 17))).Payload!;

        var wake = Assert.Single(payload.Shows);
        Assert.Equal([T(19, 30), T(21, 30), T(22, 30)], wake.Times);
    }
}
