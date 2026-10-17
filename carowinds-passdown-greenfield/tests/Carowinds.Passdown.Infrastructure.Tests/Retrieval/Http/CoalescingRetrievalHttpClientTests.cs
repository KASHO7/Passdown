using System.Net;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Http;

/// <summary>Phase 21C: park hours and the show schedule share one official request per date.</summary>
public class CoalescingRetrievalHttpClientTests
{
    private static readonly Uri Endpoint = new("https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=20261017");

    private sealed class CountingClient(Func<int, Task<RetrievalHttpResponse>> respond) : IRetrievalHttpClient
    {
        public int GetCount;

        public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken) =>
            respond(Interlocked.Increment(ref GetCount));

        public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken) =>
            Task.FromResult(new RetrievalHttpResponse(HttpStatusCode.OK, null, "post"));
    }

    private sealed class MutableClock(DateTimeOffset now) : Application.Time.IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private static RetrievalHttpResponse Ok(int n) => new(HttpStatusCode.OK, "application/json", $"body-{n}");

    [Fact]
    public async Task ConcurrentIdenticalGets_ShareOneRequest()
    {
        var gate = new TaskCompletionSource<RetrievalHttpResponse>();
        var inner = new CountingClient(_ => gate.Task);
        var client = new CoalescingRetrievalHttpClient(inner, new FakeClock(DateTimeOffset.UtcNow));

        var first = client.GetAsync(Endpoint, CancellationToken.None);
        var second = client.GetAsync(Endpoint, CancellationToken.None);
        gate.SetResult(Ok(1));

        Assert.Equal("body-1", (await first).Body);
        Assert.Equal("body-1", (await second).Body);
        Assert.Equal(1, inner.GetCount);
    }

    [Fact]
    public async Task SuccessIsReusedWithinWindow_AndRefetchedAfterIt()
    {
        var inner = new CountingClient(n => Task.FromResult(Ok(n)));
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var client = new CoalescingRetrievalHttpClient(inner, clock);

        await client.GetAsync(Endpoint, CancellationToken.None);
        clock.UtcNow += TimeSpan.FromSeconds(60);
        Assert.Equal("body-1", (await client.GetAsync(Endpoint, CancellationToken.None)).Body);

        clock.UtcNow += CoalescingRetrievalHttpClient.ReuseWindow;
        Assert.Equal("body-2", (await client.GetAsync(Endpoint, CancellationToken.None)).Body);
        Assert.Equal(2, inner.GetCount);
    }

    [Fact]
    public async Task FailuresAreNeverCached()
    {
        var inner = new CountingClient(n => Task.FromResult(n == 1
            ? new RetrievalHttpResponse(HttpStatusCode.ServiceUnavailable, "text/html", "down")
            : Ok(n)));
        var client = new CoalescingRetrievalHttpClient(inner, new FakeClock(DateTimeOffset.UtcNow));

        Assert.False((await client.GetAsync(Endpoint, CancellationToken.None)).IsSuccessStatusCode);
        Assert.Equal("body-2", (await client.GetAsync(Endpoint, CancellationToken.None)).Body);
    }

    [Fact]
    public async Task OneCallerCancelling_DoesNotCancelTheSharedFetch()
    {
        var gate = new TaskCompletionSource<RetrievalHttpResponse>();
        var inner = new CountingClient(_ => gate.Task);
        var client = new CoalescingRetrievalHttpClient(inner, new FakeClock(DateTimeOffset.UtcNow));
        using var cts = new CancellationTokenSource();

        var cancelled = client.GetAsync(Endpoint, cts.Token);
        var other = client.GetAsync(Endpoint, CancellationToken.None);
        await cts.CancelAsync();
        gate.SetResult(Ok(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal("body-1", (await other).Body);
    }

    [Fact]
    public async Task DifferentDates_AreSeparateRequests()
    {
        var inner = new CountingClient(n => Task.FromResult(Ok(n)));
        var client = new CoalescingRetrievalHttpClient(inner, new FakeClock(DateTimeOffset.UtcNow));

        await client.GetAsync(Endpoint, CancellationToken.None);
        await client.GetAsync(new Uri("https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=20261018"), CancellationToken.None);

        Assert.Equal(2, inner.GetCount);
    }
}
