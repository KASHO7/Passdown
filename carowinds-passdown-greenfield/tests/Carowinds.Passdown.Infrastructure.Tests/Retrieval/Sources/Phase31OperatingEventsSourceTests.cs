using System.Net;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Domain.Seasons;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Phase 31: <see cref="OperatingEventsSource"/> against fixtures trimmed from the official events
/// listing and event pages as published on 2026-11-21 (fixtures/phase31). No network access.
/// </summary>
public class Phase31OperatingEventsSourceTests
{
    private static readonly DateTimeOffset Nov21 = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private static RetrievalHttpResponse Html(string fixture) => new(HttpStatusCode.OK, "text/html", FixtureFile.ReadText("phase31/" + fixture));

    private static RetrievalHttpResponse Status(HttpStatusCode code) => new(code, "text/html", "<html></html>");

    /// <summary>Listing, then the standing seasonal pages in order (scarowinds, oktoberfest, winterfest), then the listing's extra seasonal link (New Year's Eve).</summary>
    private static StubHttpClient Client(RetrievalHttpResponse? listing = null, RetrievalHttpResponse? scarowinds = null, RetrievalHttpResponse? newYearsEve = null) => new(
    [
        listing ?? Html("events-listing.html"),
        scarowinds ?? Html("event-scarowinds.html"),
        Html("event-oktoberfest.html"),
        Html("event-winterfest.html"),
        newYearsEve ?? Status(HttpStatusCode.NotFound),
    ]);

    private static async Task<SourceResult<Application.Seasons.OperatingEventsPayload>> RetrieveAsync(StubHttpClient client) =>
        await new OperatingEventsSource(client, new FakeClock(Nov21)).RetrieveAsync(new RetrievalRequest("operating-events.official", SourceCategory.OperatingEvents), CancellationToken.None);

    [Fact]
    public async Task OfficialPages_YieldTheVerified2026SeasonalSchedule()
    {
        var client = Client();

        var result = await RetrieveAsync(client);

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        var events = result.Payload!.Events;

        var oktoberfest = Assert.Single(events, e => e.Kind == OperatingEventKind.Oktoberfest);
        Assert.Equal((new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 22)), (oktoberfest.StartDate, oktoberfest.EndDate));
        Assert.True(oktoberfest.IsSelectDates);
        Assert.Equal("Select Days, November 7-22, 2026", oktoberfest.DateLabel);
        Assert.Equal(new Uri("https://www.sixflags.com/carowinds/events/oktoberfest"), oktoberfest.SourceUrl);

        var winterFest = Assert.Single(events, e => e.Kind == OperatingEventKind.WinterFest);
        Assert.Equal((new DateOnly(2026, 11, 27), new DateOnly(2027, 1, 3)), (winterFest.StartDate, winterFest.EndDate));
        Assert.True(winterFest.IsSelectDates); // the listing omits "Select"; the event page states it
        Assert.Equal("Select Dates, Nov 27 - Jan 3, 2027", winterFest.DateLabel);

        // SCarowinds is no longer on the listing; its own page still carries the official dates.
        var scarowinds = Assert.Single(events, e => e.Kind == OperatingEventKind.SCarowinds);
        Assert.Equal((new DateOnly(2026, 9, 11), new DateOnly(2026, 11, 7)), (scarowinds.StartDate, scarowinds.EndDate));
        Assert.True(scarowinds.IsSelectDates);

        Assert.Single(events, e => e.Kind == OperatingEventKind.NewYearsEve);
        Assert.Contains(events, e => e.Kind == OperatingEventKind.Other && e.Title == "WinterFest Theatre Arts Day");

        Assert.Equal(5, client.Requests.Count);
        Assert.Equal(OperatingEventsSource.ListingUrl, client.Requests[0].Uri);
        Assert.Contains(client.Requests, r => r.Uri.AbsoluteUri == "https://www.sixflags.com/carowinds/events/new-years-eve-at-winterfest");
        Assert.Contains("404", result.Evidence.TechnicalDetail, StringComparison.Ordinal); // the missing page is reported, not hidden
    }

    [Fact]
    public async Task EventPageFailure_IsOnlyAWarning()
    {
        var result = await RetrieveAsync(Client(scarowinds: Status(HttpStatusCode.ServiceUnavailable)));

        Assert.Equal(RetrievalStatus.Verified, result.Evidence.Status);
        Assert.DoesNotContain(result.Payload!.Events, e => e.Kind == OperatingEventKind.SCarowinds);
        Assert.Contains(result.Payload.Warnings, w => w.Contains("scarowinds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListingUnavailable_IsAnError()
    {
        var result = await RetrieveAsync(Client(listing: Status(HttpStatusCode.BadGateway)));

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task ListingWithoutScheduleData_IsAnError_NotAnEmptySchedule()
    {
        var result = await RetrieveAsync(Client(listing: new RetrievalHttpResponse(HttpStatusCode.OK, "text/html", "<html><body>Events</body></html>")));

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
    }

    [Fact]
    public async Task TransportFailure_IsAnError()
    {
        var result = await RetrieveAsync(new StubHttpClient(new HttpRequestException("offline")));

        Assert.Equal(RetrievalStatus.Error, result.Evidence.Status);
    }

    [Fact]
    public async Task MalformedEventEntries_AreSkipped_NeverGuessed()
    {
        var listing = FixtureFile.ReadText("phase31/events-listing.html")
            .Replace("\\\"endDate\\\":\\\"2026-11-22\\\"", "\\\"endDate\\\":\\\"not-a-date\\\"", StringComparison.Ordinal);

        var result = await RetrieveAsync(Client(listing: new RetrievalHttpResponse(HttpStatusCode.OK, "text/html", listing)));

        // The listing's Oktoberfest entry is unreadable; the event page's own dates still stand.
        var oktoberfest = Assert.Single(result.Payload!.Events, e => e.Kind == OperatingEventKind.Oktoberfest);
        Assert.Equal(new DateOnly(2026, 11, 22), oktoberfest.EndDate);
    }
}
