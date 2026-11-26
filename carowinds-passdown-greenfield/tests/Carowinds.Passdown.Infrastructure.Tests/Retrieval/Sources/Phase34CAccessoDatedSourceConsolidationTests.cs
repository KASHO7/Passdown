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
/// Phase 34C: the six dated accesso sources now share one transport/parse step
/// (<c>AccessoKeywordCalendarCall</c>) and one evidence builder (<c>AccessoDatedResults</c>). This matrix pins,
/// for every source, the behavior those shared pieces carry: each failure kind on each call becomes that
/// source's own Error (its id, name, page and wording, with the same technical detail as before), caller
/// cancellation still propagates, "no dates found" is still NotPublished, past dates never hit the network,
/// the official endpoint and headers are unchanged, and the Phase 34B-2 coalescing client still shares
/// identical calendar requests while keeping different dates apart.
///
/// Product parsing, published/closed/missing classification and date matching stay in each source and are
/// covered by the per-source fixture suites (<see cref="DatedAdmissionSourceTests"/> and siblings), which run
/// unchanged. The response bodies here are small synthetic shapes identical to the committed
/// <c>no-dates-found.json</c>/<c>malformed.json</c>/<c>missing-service.json</c> fixtures; the Military/First
/// Responder fixture folder has no such files, so one shared synthetic set keeps all six rows identical.
/// "Today" is 2026-08-20 (Eastern).
/// </summary>
public class Phase34CAccessoDatedSourceConsolidationTests
{
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 20);
    private static readonly DateOnly Requested = new(2026, 9, 12);
    private static readonly Uri Endpoint = new("https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar");

    private const string NoDatesFound = """{"SERVICE":{"status":"FAILED","error_msg":"No dates found that meet requirements"}}""";
    private const string Malformed = """{"SERVICE": {"status": "OK", "DATES": """;
    private const string MissingService = """{"SOMETHING_ELSE":{}}""";

    /// <summary>Every dated accesso source with its exact, pre-34C evidence identity and wording.</summary>
    private sealed record Row(
        Func<IRetrievalHttpClient, IClock, IOfficialDataSourceAdapter> Create,
        string SourceId,
        SourceCategory Category,
        string EvidenceName,
        string PageUrl,
        string DefaultErrorDetail,
        string NotPublishedDetail,
        string Keyword,
        int CallsWhenNothingIsPublished);

    /// <summary>Non-generic view over the six <see cref="IOfficialDataSource{TPayload}"/> implementations.</summary>
    private interface IOfficialDataSourceAdapter
    {
        Task<(SourceEvidence Evidence, string SourceId, bool HasPayload)> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken);
    }

    private sealed class Adapter<T>(IOfficialDataSource<T> source) : IOfficialDataSourceAdapter
    {
        public async Task<(SourceEvidence Evidence, string SourceId, bool HasPayload)> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
        {
            var result = await source.RetrieveAsync(request, cancellationToken);
            return (result.Evidence, result.SourceId, result.Payload is not null);
        }
    }

    private static Row Get(string name) => name switch
    {
        "admission" => new Row(
            (h, c) => new Adapter<DatedAdmissionPayload>(new DatedAdmissionSource(h, c)),
            "dated-admission.official", SourceCategory.DatedAdmissionTickets, "Carowinds Dated Admission Tickets",
            "https://www.sixflags.com/carowinds/daily-tickets", "We couldn't check dated admission tickets right now.",
            "Dated admission tickets for September 12, 2026 have not been published yet.", "SnapWizardDailyTickets", 1),
        "fast-lane" => new Row(
            (h, c) => new Adapter<DatedFastLanePayload>(new DatedFastLaneSource(h, c)),
            "dated-fast-lane.official", SourceCategory.DatedFastLane, "Carowinds Dated Fast Lane",
            "https://www.sixflags.com/carowinds/fast-lane", "We couldn't check Fast Lane availability right now.",
            "Fast Lane for September 12, 2026 has not been published yet.", "FastLaneOnly", 2),
        "haunted" => new Row(
            (h, c) => new Adapter<DatedHauntedPayload>(new DatedHauntedSource(h, c)),
            "dated-haunted.official", SourceCategory.HauntedAttractions, "Carowinds Dated Haunted Attractions",
            "https://www.sixflags.com/carowinds/events/scarowinds", "We couldn't check haunted-attraction availability right now.",
            "Haunted attractions for September 12, 2026 have not been published yet.", "", 3),
        "bring-a-friend" => new Row(
            (h, c) => new Adapter<DatedBringAFriendPayload>(new DatedBringAFriendSource(h, c)),
            "dated-bring-a-friend.official", SourceCategory.DatedBringAFriend, "Carowinds Dated Bring-A-Friend",
            "https://www.sixflags.com/carowinds/special-offers", "We couldn't check Bring-A-Friend availability right now.",
            "Bring-A-Friend for September 12, 2026 has not been published yet.", "BAFWizardDailyTickets", 2),
        "bring-a-friend-duo" => new Row(
            (h, c) => new Adapter<DatedBringAFriendDuoPayload>(new DatedBringAFriendDuoSource(h, c)),
            "dated-bring-a-friend-duo.official", SourceCategory.BringAFriendDuo, "Carowinds Dated Bring-A-Friend Duo",
            "https://www.sixflags.com/carowinds/daily-tickets", "We couldn't check Bring-A-Friend Duo availability right now.",
            "Bring-A-Friend Duo for September 12, 2026 has not been published yet.", "SUBAF50", 2),
        "military" => new Row(
            (h, c) => new Adapter<DatedMilitaryFirstResponderPayload>(new DatedMilitaryFirstResponderSource(h, c)),
            "dated-military-first-responder.official", SourceCategory.DatedMilitaryFirstResponder,
            "Carowinds Dated Military & First Responder Tickets", "https://www.sixflags.com/carowinds/military-discounts",
            "We couldn't check Military/First Responder pricing right now.",
            "Military/First Responder pricing for September 12, 2026 has not been published yet.", "MilitaryWizardDailyTickets", 2),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>Answers each POST from a per-call function; records every request.</summary>
    private sealed class ScriptedClient(Func<int, Task<RetrievalHttpResponse>> respond) : IRetrievalHttpClient
    {
        private int _posts;

        public List<RecordedRequest> Requests { get; } = [];

        public int PostCount => Volatile.Read(ref _posts);

        public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Dated accesso sources never GET.");

        public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(new RecordedRequest(requestUri, jsonBody, headers));
            }

            return respond(Interlocked.Increment(ref _posts));
        }
    }

    private static Task<RetrievalHttpResponse> Respond(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        Task.FromResult(new RetrievalHttpResponse(status, "application/json", body));

    private static RetrievalRequest Request(Row row, DateOnly date) => new(row.SourceId, row.Category, date);

    private static void AssertIdentity(Row row, string sourceId, SourceEvidence evidence, RetrievalStatus status, DateTimeOffset expectedTimeChecked)
    {
        Assert.Equal(row.SourceId, sourceId);
        Assert.Equal(status, evidence.Status);
        Assert.Equal(row.EvidenceName, evidence.SourceName);
        Assert.Equal(new Uri(row.PageUrl), evidence.SourceUrl);
        Assert.Equal(RetrievalStrategy.DirectApi, evidence.Strategy);
        Assert.Equal(Requested, evidence.RequestedDate);
        Assert.Null(evidence.ConfirmedDate);
        Assert.Equal(expectedTimeChecked, evidence.TimeChecked);
        Assert.Equal(TimeSpan.FromHours(-4), evidence.TimeChecked.Offset); // Eastern daylight time
    }

    private static DateTimeOffset EasternNow => EasternTimeZone.ConvertFromUtc(SampleUtcNow);

    public static TheoryData<string> Sources => new() { "admission", "fast-lane", "haunted", "bring-a-friend", "bring-a-friend-duo", "military" };

    // ----- Failures on the first call -----

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task TransportFailure_IsThatSourcesOwnError_WithTheExceptionAsTechnicalDetail(string name)
    {
        var row = Get(name);
        var http = new ScriptedClient(_ => Task.FromException<RetrievalHttpResponse>(new HttpRequestException("Connection reset by peer")));
        var source = row.Create(http, new FakeClock(SampleUtcNow));

        var (evidence, sourceId, hasPayload) = await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);

        AssertIdentity(row, sourceId, evidence, RetrievalStatus.Error, EasternNow);
        Assert.False(hasPayload);
        Assert.Equal(row.DefaultErrorDetail, evidence.Detail);
        Assert.Contains("HttpRequestException", evidence.TechnicalDetail);
        Assert.Contains("Connection reset by peer", evidence.TechnicalDetail);
        Assert.Equal(1, http.PostCount); // a failed first call stops the source: no follow-up probe
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task NonSuccessStatus_IsAnError_QuotingStatusAndATruncatedBody(string name)
    {
        var row = Get(name);
        var longBody = new string('x', 700);
        var http = new ScriptedClient(_ => Respond(longBody, HttpStatusCode.ServiceUnavailable));
        var source = row.Create(http, new FakeClock(SampleUtcNow));

        var (evidence, sourceId, hasPayload) = await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);

        AssertIdentity(row, sourceId, evidence, RetrievalStatus.Error, EasternNow);
        Assert.False(hasPayload);
        Assert.Equal(row.DefaultErrorDetail, evidence.Detail);
        Assert.Contains(
            $"POST {Endpoint} returned 503 ServiceUnavailable. Body: {new string('x', 500)}…",
            evidence.TechnicalDetail);
        Assert.DoesNotContain(new string('x', 501), evidence.TechnicalDetail);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task MalformedJson_IsAnError_WithTheJsonException(string name)
    {
        var row = Get(name);
        var http = new ScriptedClient(_ => Respond(Malformed));
        var source = row.Create(http, new FakeClock(SampleUtcNow));

        var (evidence, sourceId, hasPayload) = await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);

        AssertIdentity(row, sourceId, evidence, RetrievalStatus.Error, EasternNow);
        Assert.False(hasPayload);
        Assert.Equal(row.DefaultErrorDetail, evidence.Detail);
        Assert.Contains("System.Text.Json.JsonException", evidence.TechnicalDetail);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task MissingServiceObject_IsAnError_NamingTheShapeProblem(string name)
    {
        var row = Get(name);
        var http = new ScriptedClient(_ => Respond(MissingService));
        var source = row.Create(http, new FakeClock(SampleUtcNow));

        var (evidence, sourceId, hasPayload) = await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);

        AssertIdentity(row, sourceId, evidence, RetrievalStatus.Error, EasternNow);
        Assert.False(hasPayload);
        Assert.Equal(row.DefaultErrorDetail, evidence.Detail);
        Assert.Contains($"Unexpected response shape from {Endpoint}: no SERVICE object. Body: {MissingService}", evidence.TechnicalDetail);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task CallerCancellation_Propagates_InsteadOfBecomingAnError(string name)
    {
        var row = Get(name);
        var http = new ScriptedClient(_ => Task.FromException<RetrievalHttpResponse>(new OperationCanceledException()));
        var source = row.Create(http, new FakeClock(SampleUtcNow));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.RetrieveAsync(Request(row, Requested), CancellationToken.None));
    }

    // ----- Partial failures: a later call fails after an earlier one succeeded -----

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task LaterCallFailure_FailsTheWholeResult_SoNoPartialAnswerReplacesLastKnownGood(string name)
    {
        var row = Get(name);
        var lastCall = row.CallsWhenNothingIsPublished;
        var http = new ScriptedClient(n => n < lastCall
            ? Respond(NoDatesFound)
            : Task.FromException<RetrievalHttpResponse>(new HttpRequestException("second call dropped")));
        var source = row.Create(http, new FakeClock(SampleUtcNow));

        var (evidence, sourceId, hasPayload) = await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);

        AssertIdentity(row, sourceId, evidence, RetrievalStatus.Error, EasternNow);
        Assert.False(hasPayload);
        Assert.Equal(row.DefaultErrorDetail, evidence.Detail);
        Assert.Contains("second call dropped", evidence.TechnicalDetail);
        Assert.Equal(lastCall, http.PostCount);
    }

    // ----- Successful checks without a payload -----

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task NoDatesFoundFromToday_IsNotPublished_WithThatSourcesWording(string name)
    {
        var row = Get(name);
        var http = new ScriptedClient(_ => Respond(NoDatesFound));
        var source = row.Create(http, new FakeClock(SampleUtcNow));

        var (evidence, sourceId, hasPayload) = await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);

        AssertIdentity(row, sourceId, evidence, RetrievalStatus.NotPublished, EasternNow);
        Assert.False(hasPayload);
        Assert.Equal(row.NotPublishedDetail, evidence.Detail);
        Assert.Null(evidence.TechnicalDetail);
        Assert.Equal(row.CallsWhenNothingIsPublished, http.PostCount);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task PastDate_IsUnavailable_WithoutAnyRequest(string name)
    {
        var row = Get(name);
        var http = new ScriptedClient(_ => throw new InvalidOperationException("must not be called"));
        var source = row.Create(http, new FakeClock(SampleUtcNow));
        var yesterday = Today.AddDays(-1);

        var (evidence, sourceId, hasPayload) = await source.RetrieveAsync(Request(row, yesterday), CancellationToken.None);

        Assert.Equal(row.SourceId, sourceId);
        Assert.Equal(RetrievalStatus.Unavailable, evidence.Status);
        Assert.Equal(row.EvidenceName, evidence.SourceName);
        Assert.Equal(new Uri(row.PageUrl), evidence.SourceUrl);
        Assert.Equal(yesterday, evidence.RequestedDate);
        Assert.Equal("That date has already passed.", evidence.Detail);
        Assert.False(hasPayload);
        Assert.Equal(0, http.PostCount);
    }

    // ----- Request shape and Phase 34B-2 coalescing -----

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task EveryCall_UsesTheOfficialCalendarEndpoint_HeadersAndAFreshRequestToken(string name)
    {
        var row = Get(name);
        var http = new ScriptedClient(_ => Respond(NoDatesFound));
        var source = row.Create(http, new FakeClock(SampleUtcNow));

        await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);

        Assert.All(http.Requests, request =>
        {
            Assert.Equal(Endpoint, request.Uri);
            Assert.NotNull(request.Headers);
            Assert.Equal("cedarfair", request.Headers!["com-accessopassport-client"]);
            Assert.Equal("https://cf-ca.buy.sixflags.com/", request.Headers["referer"]);
            Assert.Equal("https://cf-ca.buy.sixflags.com", request.Headers["origin"]);
            Assert.Contains("\"request_token\":", request.Body);
            if (row.Keyword.Length > 0)
            {
                Assert.Contains($"\"keyword\":\"{row.Keyword}\"", request.Body);
            }
        });
        var tokens = http.Requests.Select(r => System.Text.Json.JsonDocument.Parse(r.Body!).RootElement.GetProperty("request_token").GetString()).ToList();
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task ThroughTheCoalescingClient_ARepeatedCheckIsShared_ButADifferentDateIsAskedAgain(string name)
    {
        var row = Get(name);
        var inner = new ScriptedClient(_ => Respond(NoDatesFound));
        var clock = new FakeClock(SampleUtcNow);
        var shared = new CoalescingRetrievalHttpClient(inner, clock, AccessoKeywordCalendarCoalescing.KeyBody);
        var source = row.Create(shared, clock);

        var first = await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);
        var afterFirst = inner.PostCount;
        var again = await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);

        Assert.Equal(row.CallsWhenNothingIsPublished, afterFirst);
        Assert.Equal(afterFirst, inner.PostCount); // same date again: every calendar POST was reused
        Assert.Equal(RetrievalStatus.NotPublished, first.Evidence.Status);
        Assert.Equal(RetrievalStatus.NotPublished, again.Evidence.Status);
        Assert.Equal(first.Evidence.Detail, again.Evidence.Detail);

        var otherDate = Requested.AddDays(1);
        var other = await source.RetrieveAsync(Request(row, otherDate), CancellationToken.None);

        Assert.Equal(otherDate, other.Evidence.RequestedDate);
        Assert.Equal(RetrievalStatus.NotPublished, other.Evidence.Status);
        Assert.Contains("September 13, 2026", other.Evidence.Detail);
        // A single-day request for the new date must reach the server; only a today-anchored horizon
        // window identical to the earlier one may be shared (admission's one call is that window).
        Assert.True(inner.PostCount > afterFirst || row.CallsWhenNothingIsPublished == 1);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task ThroughTheCoalescingClient_AFailureIsNeverReused(string name)
    {
        var row = Get(name);
        var inner = new ScriptedClient(n => n == 1 ? Respond("busy", HttpStatusCode.ServiceUnavailable) : Respond(NoDatesFound));
        var clock = new FakeClock(SampleUtcNow);
        var source = row.Create(new CoalescingRetrievalHttpClient(inner, clock, AccessoKeywordCalendarCoalescing.KeyBody), clock);

        var failed = await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);
        var retried = await source.RetrieveAsync(Request(row, Requested), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, failed.Evidence.Status);
        Assert.Equal(RetrievalStatus.NotPublished, retried.Evidence.Status);
        Assert.Equal(1 + row.CallsWhenNothingIsPublished, inner.PostCount);
    }
}
