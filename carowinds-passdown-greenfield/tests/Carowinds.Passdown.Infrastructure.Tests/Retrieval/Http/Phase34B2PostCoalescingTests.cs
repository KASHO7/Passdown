using System.Net;
using System.Text.Json;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Http;

/// <summary>
/// Phase 34B-2: identical official POSTs (same endpoint, headers and body, apart from accesso's per-request
/// nonce) are sent once; anything that could change the server's answer keeps them apart. Deterministic: no
/// network, fake clock, gated responses.
/// </summary>
public class Phase34B2PostCoalescingTests
{
    private static readonly Uri Accesso = new("https://ecomm.api.cf.accessoticketing.com/api/request/getkeywordcalendar");
    private static readonly Uri OtherEndpoint = new("https://example.invalid/api/post");
    private static readonly DateTimeOffset SampleUtcNow = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<string, string> Headers = new Dictionary<string, string>
    {
        ["com-accessopassport-client"] = "cedarfair",
        ["com-accessopassport-language"] = "en",
        ["referer"] = "https://cf-ca.buy.sixflags.com/",
    };

    private sealed class CountingPostClient(Func<int, string, Task<RetrievalHttpResponse>> respond) : IRetrievalHttpClient
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public List<string> Bodies { get; } = [];

        public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No GETs expected.");

        public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            lock (Bodies)
            {
                Bodies.Add(jsonBody);
            }

            return respond(Interlocked.Increment(ref _postCount), jsonBody);
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : Application.Time.IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private static RetrievalHttpResponse Ok(int n) => new(HttpStatusCode.OK, "application/json", $"body-{n}");

    private static string Body(string keyword, string start, string end, string? token = null) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["display_zero_capacity"] = "1",
            ["keyword"] = keyword,
            ["end_date"] = end,
            ["start_date"] = start,
            ["request_type"] = "GetKeywordCalendar",
            ["request_token"] = token ?? Guid.NewGuid().ToString("N").ToUpperInvariant(),
        });

    private static CoalescingRetrievalHttpClient Coalescing(IRetrievalHttpClient inner, Application.Time.IClock? clock = null) =>
        new(inner, clock ?? new FakeClock(SampleUtcNow), AccessoKeywordCalendarCoalescing.KeyBody);

    // ----- Coalescer rules -----

    [Fact]
    public async Task ConcurrentIdenticalPosts_ShareOneRequest_AndEveryCallerGetsTheResponse()
    {
        var gate = new TaskCompletionSource<RetrievalHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new CountingPostClient((_, _) => gate.Task);
        var client = Coalescing(inner);

        var calls = Enumerable.Range(0, 5)
            .Select(_ => client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None))
            .ToList();
        gate.SetResult(Ok(1));

        foreach (var call in calls)
        {
            Assert.Equal("body-1", (await call.WaitAsync(TimeSpan.FromSeconds(10))).Body);
        }

        Assert.Equal(1, inner.PostCount);
    }

    [Fact]
    public async Task SequentialIdenticalPosts_ReuseTheSuccessWithinTheWindow_AndRefetchAfterIt()
    {
        var inner = new CountingPostClient((n, _) => Task.FromResult(Ok(n)));
        var clock = new MutableClock(SampleUtcNow);
        var client = Coalescing(inner, clock);

        await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None);
        clock.UtcNow += TimeSpan.FromSeconds(90);
        Assert.Equal("body-1", (await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None)).Body);

        clock.UtcNow += CoalescingRetrievalHttpClient.ReuseWindow;
        Assert.Equal("body-2", (await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None)).Body);
        Assert.Equal(2, inner.PostCount);
    }

    [Theory]
    [InlineData("FastLaneOnly", "2026-08-20", "2026-12-18")] // different keyword
    [InlineData("SnapWizardDailyTickets", "2026-08-21", "2026-12-18")] // different start date
    [InlineData("SnapWizardDailyTickets", "2026-08-20", "2026-12-19")] // different end date
    public async Task DifferentBodies_AreNeverMerged(string keyword, string start, string end)
    {
        var inner = new CountingPostClient((n, _) => Task.FromResult(Ok(n)));
        var client = Coalescing(inner);

        var first = await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None);
        var second = await client.PostJsonAsync(Accesso, Body(keyword, start, end), Headers, CancellationToken.None);

        Assert.Equal("body-1", first.Body);
        Assert.Equal("body-2", second.Body);
        Assert.Equal(2, inner.PostCount);
    }

    [Fact]
    public async Task DifferentDisplayZeroCapacity_IsNotMerged()
    {
        var inner = new CountingPostClient((n, _) => Task.FromResult(Ok(n)));
        var client = Coalescing(inner);
        var body = Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18");

        await client.PostJsonAsync(Accesso, body, Headers, CancellationToken.None);
        await client.PostJsonAsync(Accesso, body.Replace("\"display_zero_capacity\":\"1\"", "\"display_zero_capacity\":\"0\"", StringComparison.Ordinal), Headers, CancellationToken.None);

        Assert.Equal(2, inner.PostCount);
    }

    [Fact]
    public async Task DifferentHeaderValues_OrExtraHeaders_AreNeverMerged()
    {
        var inner = new CountingPostClient((n, _) => Task.FromResult(Ok(n)));
        var client = Coalescing(inner);
        var otherLanguage = new Dictionary<string, string>(Headers) { ["com-accessopassport-language"] = "es" };
        var extraHeader = new Dictionary<string, string>(Headers) { ["authorization"] = "Bearer x" };

        await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None);
        await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), otherLanguage, CancellationToken.None);
        await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), extraHeader, CancellationToken.None);
        await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), headers: null, CancellationToken.None);

        Assert.Equal(4, inner.PostCount);
    }

    [Fact]
    public async Task HeaderOrderAndNameCase_DoNotPreventMerging()
    {
        var inner = new CountingPostClient((n, _) => Task.FromResult(Ok(n)));
        var client = Coalescing(inner);
        var reordered = Headers.Reverse().ToDictionary(h => h.Key.ToUpperInvariant(), h => h.Value);

        await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None);
        await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), reordered, CancellationToken.None);

        Assert.Equal(1, inner.PostCount);
    }

    [Fact]
    public async Task DifferentEndpoints_AreNeverMerged()
    {
        var inner = new CountingPostClient((n, _) => Task.FromResult(Ok(n)));
        var client = Coalescing(inner);
        const string body = "{\"a\":\"1\"}";

        await client.PostJsonAsync(OtherEndpoint, body, Headers, CancellationToken.None);
        await client.PostJsonAsync(Accesso, body, Headers, CancellationToken.None);

        Assert.Equal(2, inner.PostCount);
    }

    [Fact]
    public async Task OutsideTheAccessoEndpoint_BodiesMustMatchByteForByte_EvenForANonceField()
    {
        var inner = new CountingPostClient((n, _) => Task.FromResult(Ok(n)));
        var client = Coalescing(inner);

        await client.PostJsonAsync(OtherEndpoint, Body("k", "2026-08-20", "2026-08-20", "AAA"), Headers, CancellationToken.None);
        await client.PostJsonAsync(OtherEndpoint, Body("k", "2026-08-20", "2026-08-20", "BBB"), Headers, CancellationToken.None);
        await client.PostJsonAsync(OtherEndpoint, Body("k", "2026-08-20", "2026-08-20", "BBB"), Headers, CancellationToken.None);

        Assert.Equal(2, inner.PostCount);
    }

    [Fact]
    public async Task WithoutABodyKey_NonceDifferentBodiesAreNotMerged()
    {
        var inner = new CountingPostClient((n, _) => Task.FromResult(Ok(n)));
        var client = new CoalescingRetrievalHttpClient(inner, new FakeClock(SampleUtcNow));

        await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None);
        await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None);

        Assert.Equal(2, inner.PostCount);
    }

    [Fact]
    public async Task FailureStatus_IsNeverReused()
    {
        var inner = new CountingPostClient((n, _) => Task.FromResult(n == 1
            ? new RetrievalHttpResponse(HttpStatusCode.ServiceUnavailable, "text/html", "down")
            : Ok(n)));
        var client = Coalescing(inner);

        Assert.False((await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None)).IsSuccessStatusCode);
        Assert.Equal("body-2", (await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None)).Body);
        Assert.Equal(2, inner.PostCount);
    }

    [Fact]
    public async Task TransportException_IsNeverReused()
    {
        var inner = new CountingPostClient((n, _) => n == 1
            ? Task.FromException<RetrievalHttpResponse>(new HttpRequestException("reset"))
            : Task.FromResult(Ok(n)));
        var client = Coalescing(inner);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None));
        Assert.Equal("body-2", (await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None)).Body);
    }

    [Fact]
    public async Task ConcurrentCallers_AllSeeASharedFailure_ButTheNextCallRetries()
    {
        var gate = new TaskCompletionSource<RetrievalHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new CountingPostClient((n, _) => n == 1 ? gate.Task : Task.FromResult(Ok(n)));
        var client = Coalescing(inner);

        var first = client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None);
        var second = client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None);
        gate.SetResult(new RetrievalHttpResponse(HttpStatusCode.BadGateway, "text/plain", "bad gateway"));

        Assert.Equal(HttpStatusCode.BadGateway, (await first.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await second.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
        Assert.Equal("body-2", (await client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None)).Body);
        Assert.Equal(2, inner.PostCount);
    }

    [Fact]
    public async Task OneCallerCancelling_DoesNotCancelTheSharedPost()
    {
        var gate = new TaskCompletionSource<RetrievalHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new CountingPostClient((_, _) => gate.Task);
        var client = Coalescing(inner);
        using var cts = new CancellationTokenSource();

        var cancelled = client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, cts.Token);
        var other = client.PostJsonAsync(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18"), Headers, CancellationToken.None);
        await cts.CancelAsync();
        gate.SetResult(Ok(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal("body-1", (await other.WaitAsync(TimeSpan.FromSeconds(10))).Body);
        Assert.Equal(1, inner.PostCount);
    }

    [Fact]
    public async Task GetsAndPosts_ToTheSameUri_AreSeparate()
    {
        var getCount = 0;
        var inner = new GetAndPostClient(() => Interlocked.Increment(ref getCount));
        var client = Coalescing(inner);

        await client.GetAsync(Accesso, CancellationToken.None);
        await client.PostJsonAsync(Accesso, "{}", Headers, CancellationToken.None);

        Assert.Equal(1, getCount);
        Assert.Equal(1, inner.PostCount);
    }

    private sealed class GetAndPostClient(Action onGet) : IRetrievalHttpClient
    {
        public int PostCount;

        public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken)
        {
            onGet();
            return Task.FromResult(new RetrievalHttpResponse(HttpStatusCode.OK, null, "get"));
        }

        public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref PostCount);
            return Task.FromResult(new RetrievalHttpResponse(HttpStatusCode.OK, null, "post"));
        }
    }

    // ----- Accesso body key -----

    [Fact]
    public void AccessoKey_IgnoresOnlyTheRequestToken()
    {
        var a = AccessoKeywordCalendarCoalescing.KeyBody(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18", "AAA"));
        var b = AccessoKeywordCalendarCoalescing.KeyBody(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-18", "BBB"));
        var c = AccessoKeywordCalendarCoalescing.KeyBody(Accesso, Body("SnapWizardDailyTickets", "2026-08-20", "2026-12-17", "AAA"));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.DoesNotContain("request_token", a, StringComparison.Ordinal);
        Assert.Contains("SnapWizardDailyTickets", a, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"nested\":{\"a\":1}}")]
    [InlineData("{\"keyword\":\"x\"}")] // no nonce: compared exactly
    public void AccessoKey_LeavesAnythingUnexpectedExact(string body)
    {
        Assert.Equal(body, AccessoKeywordCalendarCoalescing.KeyBody(Accesso, body));
    }

    // ----- Real accesso sources over a rolling window (deterministic fixtures) -----

    private static readonly DateOnly[] RollingWindow = [.. Enumerable.Range(0, 7).Select(i => new DateOnly(2026, 8, 20).AddDays(i))];

    private static RetrievalHttpResponse Fixture(string relativePath) =>
        new(HttpStatusCode.OK, "application/json", FixtureFile.ReadText(relativePath));

    [Fact]
    public async Task AdmissionAcrossASevenDayWindow_SendsOnePost_AndEveryDateKeepsItsOwnInterpretation()
    {
        var response = Fixture("dated-admission/published-window.json");
        var rawInner = new CountingPostClient((_, _) => Task.FromResult(response));
        var coalescedInner = new CountingPostClient((_, _) => Task.FromResult(response));
        var raw = new DatedAdmissionSource(rawInner, new FakeClock(SampleUtcNow));
        var coalesced = new DatedAdmissionSource(Coalescing(coalescedInner), new FakeClock(SampleUtcNow));

        foreach (var date in RollingWindow)
        {
            var request = new RetrievalRequest("dated-admission.official", SourceCategory.DatedAdmissionTickets, date);
            var expected = await raw.RetrieveAsync(request, CancellationToken.None);
            var actual = await coalesced.RetrieveAsync(request, CancellationToken.None);
            AssertSameResult(expected, actual);
            Assert.Equal(date, actual.Evidence.RequestedDate);
        }

        Assert.Equal(7, rawInner.PostCount);
        Assert.Equal(1, coalescedInner.PostCount);
    }

    [Fact]
    public async Task FastLaneGapDates_ShareOneHorizonPost_ButKeepTheirOwnSingleDayPosts()
    {
        // horizon-window.json publishes 08-20, 08-21, then 08-29: 08-22..08-28 are gap dates, so each one makes
        // its own single-day request and then the identical horizon probe.
        var noDates = Fixture("dated-fast-lane/no-dates-found.json");
        var horizon = Fixture("dated-fast-lane/horizon-window.json");
        Task<RetrievalHttpResponse> Respond(string body) =>
            Task.FromResult(IsSingleDay(body) ? noDates : horizon);
        var rawInner = new CountingPostClient((_, body) => Respond(body));
        var coalescedInner = new CountingPostClient((_, body) => Respond(body));
        var raw = new DatedFastLaneSource(rawInner, new FakeClock(SampleUtcNow));
        var coalesced = new DatedFastLaneSource(Coalescing(coalescedInner), new FakeClock(SampleUtcNow));
        DateOnly[] gapDates = [.. Enumerable.Range(22, 7).Select(day => new DateOnly(2026, 8, day))];

        foreach (var date in gapDates)
        {
            var request = new RetrievalRequest("dated-fast-lane.official", SourceCategory.DatedFastLane, date);
            var expected = await raw.RetrieveAsync(request, CancellationToken.None);
            var actual = await coalesced.RetrieveAsync(request, CancellationToken.None);
            AssertSameResult(expected, actual);
            Assert.Equal(RetrievalStatus.Unavailable, actual.Evidence.Status);
        }

        Assert.Equal(14, rawInner.PostCount); // 7 single-day + 7 identical horizon probes
        Assert.Equal(8, coalescedInner.PostCount); // 7 single-day + 1 horizon probe
        Assert.Equal(7, coalescedInner.Bodies.Count(IsSingleDay));
    }

    [Fact]
    public async Task ARepeatedRefreshAfterTheWindow_GoesBackToTheNetwork_AndStaysCorrect()
    {
        var response = Fixture("dated-admission/published-window.json");
        var inner = new CountingPostClient((_, _) => Task.FromResult(response));
        var clock = new MutableClock(SampleUtcNow);
        var source = new DatedAdmissionSource(Coalescing(inner, clock), clock);
        var request = new RetrievalRequest("dated-admission.official", SourceCategory.DatedAdmissionTickets, new DateOnly(2026, 8, 21));

        var first = await source.RetrieveAsync(request, CancellationToken.None);
        clock.UtcNow += CoalescingRetrievalHttpClient.ReuseWindow + TimeSpan.FromSeconds(1);
        var second = await source.RetrieveAsync(request, CancellationToken.None);

        Assert.Equal(2, inner.PostCount);
        Assert.Equal(RetrievalStatus.Verified, first.Evidence.Status);
        Assert.Equal(RetrievalStatus.Verified, second.Evidence.Status);
        Assert.True(second.Evidence.TimeChecked > first.Evidence.TimeChecked); // each check keeps its own freshness time
    }

    [Fact]
    public async Task AFailedSharedPost_IsAnErrorForThatDate_AndTheNextDateRetries()
    {
        var good = Fixture("dated-admission/published-window.json");
        var inner = new CountingPostClient((n, _) => Task.FromResult(n == 1
            ? new RetrievalHttpResponse(HttpStatusCode.ServiceUnavailable, "text/html", "down")
            : good));
        var source = new DatedAdmissionSource(Coalescing(inner), new FakeClock(SampleUtcNow));

        var failed = await source.RetrieveAsync(new RetrievalRequest("dated-admission.official", SourceCategory.DatedAdmissionTickets, new DateOnly(2026, 8, 20)), CancellationToken.None);
        var next = await source.RetrieveAsync(new RetrievalRequest("dated-admission.official", SourceCategory.DatedAdmissionTickets, new DateOnly(2026, 8, 21)), CancellationToken.None);

        Assert.Equal(RetrievalStatus.Error, failed.Evidence.Status);
        Assert.Null(failed.Payload);
        Assert.Equal(RetrievalStatus.Verified, next.Evidence.Status);
        Assert.Equal(2, inner.PostCount);
    }

    private static bool IsSingleDay(string body)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(body)!;
        return fields["start_date"] == fields["end_date"];
    }

    private static void AssertSameResult<T>(SourceResult<T> expected, SourceResult<T> actual)
        where T : class
    {
        Assert.Equal(expected.Evidence.Status, actual.Evidence.Status);
        Assert.Equal(expected.Evidence.Detail, actual.Evidence.Detail);
        Assert.Equal(expected.Evidence.RequestedDate, actual.Evidence.RequestedDate);
        Assert.Equal(expected.Evidence.ConfirmedDate, actual.Evidence.ConfirmedDate);
        Assert.Equal(expected.Evidence.TimeChecked, actual.Evidence.TimeChecked);
        Assert.Equal(JsonSerializer.Serialize(expected.Payload), JsonSerializer.Serialize(actual.Payload));
    }
}
