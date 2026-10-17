using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Http;

/// <summary>
/// Phase 21C: lets two adapters that read the same official GET endpoint share one request.
/// <see cref="Sources.ParkHoursSource"/> and <see cref="Sources.ShowScheduleSource"/> both read
/// the FIMS operating-hours JSON for a date; in the app they are given this one decorator, so a
/// refresh that checks a date's hours and shows issues a single request:
/// <list type="bullet">
/// <item>identical GETs already in flight are joined, never duplicated;</item>
/// <item>a successful (2xx) response is reused for <see cref="ReuseWindow"/> — long enough to cover one
/// refresh job, short enough that a later Check Online still goes to the network;</item>
/// <item>failures are never cached, so a retry always really retries.</item>
/// </list>
/// POSTs pass straight through. Holds only response bodies for a moment in memory — nothing is
/// written to disk or the database.
/// </summary>
public sealed class CoalescingRetrievalHttpClient(IRetrievalHttpClient inner, IClock clock) : IRetrievalHttpClient
{
    public static readonly TimeSpan ReuseWindow = TimeSpan.FromMinutes(2);

    private readonly Lock _lock = new();
    private readonly Dictionary<Uri, Task<RetrievalHttpResponse>> _inFlight = [];
    private readonly Dictionary<Uri, (RetrievalHttpResponse Response, DateTimeOffset StoredAt)> _recent = [];

    public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestUri);

        Task<RetrievalHttpResponse> shared;
        lock (_lock)
        {
            var now = clock.UtcNow;
            foreach (var expired in _recent.Where(entry => now - entry.Value.StoredAt >= ReuseWindow || now < entry.Value.StoredAt).Select(entry => entry.Key).ToList())
            {
                _recent.Remove(expired);
            }

            if (_recent.TryGetValue(requestUri, out var recent))
            {
                return Task.FromResult(recent.Response);
            }

            if (!_inFlight.TryGetValue(requestUri, out shared!))
            {
                // Started on the pool so the entry is registered before the fetch can complete and
                // remove it (FetchAsync's finally needs this lock).
                shared = Task.Run(() => FetchAsync(requestUri), CancellationToken.None);
                _inFlight[requestUri] = shared;
            }
        }

        // Each caller keeps its own cancellation: cancelling one waiter never cancels the shared fetch.
        return shared.WaitAsync(cancellationToken);
    }

    public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken) =>
        inner.PostJsonAsync(requestUri, jsonBody, headers, cancellationToken);

    private async Task<RetrievalHttpResponse> FetchAsync(Uri requestUri)
    {
        try
        {
            // The shared fetch is not tied to any one caller's token; the inner client's own timeout bounds it.
            var response = await inner.GetAsync(requestUri, CancellationToken.None).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                lock (_lock)
                {
                    _recent[requestUri] = (response, clock.UtcNow);
                }
            }

            return response;
        }
        finally
        {
            lock (_lock)
            {
                _inFlight.Remove(requestUri);
            }
        }
    }
}
