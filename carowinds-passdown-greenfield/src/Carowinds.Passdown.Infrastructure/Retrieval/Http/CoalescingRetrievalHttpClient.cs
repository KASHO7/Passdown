using System.Text;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Http;

/// <summary>
/// Phase 21C: lets two adapters that read the same official GET endpoint share one request.
/// <see cref="Sources.ParkHoursSource"/> and <see cref="Sources.ShowScheduleSource"/> both read
/// the FIMS operating-hours JSON for a date; in the app they are given this one decorator, so a
/// refresh that checks a date's hours and shows issues a single request:
/// <list type="bullet">
/// <item>identical requests already in flight are joined, never duplicated;</item>
/// <item>a successful (2xx) response is reused for <see cref="ReuseWindow"/> — long enough to cover one
/// refresh job, short enough that a later Check Online still goes to the network;</item>
/// <item>failures (non-2xx responses and exceptions) are never cached, so a retry always really retries.</item>
/// </list>
/// Phase 34B-2: POSTs are coalesced under the same rules when they are equivalent — same endpoint, the
/// same headers (every name and value) and the same body. The optional <paramref name="postBodyKey"/>
/// maps a body to the form used for that comparison; the app supplies
/// <see cref="Sources.AccessoKeywordCalendarCoalescing.KeyBody"/>, which ignores only the accesso calendar's
/// client-generated per-request nonce (<c>request_token</c>). Without it, bodies must match byte for byte.
/// Each source still interprets the shared response for its own requested date, so freshness timestamps,
/// verification and last-known-good semantics are unchanged.
/// Holds only response bodies for a moment in memory — nothing is written to disk or the database.
/// </summary>
public sealed class CoalescingRetrievalHttpClient(
    IRetrievalHttpClient inner,
    IClock clock,
    Func<Uri, string, string>? postBodyKey = null) : IRetrievalHttpClient
{
    public static readonly TimeSpan ReuseWindow = TimeSpan.FromMinutes(2);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Task<RetrievalHttpResponse>> _inFlight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (RetrievalHttpResponse Response, DateTimeOffset StoredAt)> _recent = new(StringComparer.Ordinal);

    public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        return Coalesce("GET " + requestUri.AbsoluteUri, () => inner.GetAsync(requestUri, CancellationToken.None), cancellationToken);
    }

    public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(jsonBody);

        var key = PostKey(requestUri, jsonBody, headers);

        // The shared request sends the first caller's exact body and headers; every joined caller's request
        // is equivalent to it by construction of the key.
        return Coalesce(key, () => inner.PostJsonAsync(requestUri, jsonBody, headers, CancellationToken.None), cancellationToken);
    }

    private string PostKey(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers)
    {
        var builder = new StringBuilder("POST ").Append(requestUri.AbsoluteUri).Append('\n');
        if (headers is not null)
        {
            // Header names compare case-insensitively (HTTP); values exactly. Order never matters.
            foreach (var (name, value) in headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.Value, StringComparer.Ordinal))
            {
                builder.Append(name.ToLowerInvariant()).Append(':').Append(value).Append('\n');
            }
        }

        builder.Append('\n').Append(postBodyKey?.Invoke(requestUri, jsonBody) ?? jsonBody);
        return builder.ToString();
    }

    private Task<RetrievalHttpResponse> Coalesce(string key, Func<Task<RetrievalHttpResponse>> send, CancellationToken cancellationToken)
    {
        Task<RetrievalHttpResponse> shared;
        lock (_lock)
        {
            var now = clock.UtcNow;
            foreach (var expired in _recent.Where(entry => now - entry.Value.StoredAt >= ReuseWindow || now < entry.Value.StoredAt).Select(entry => entry.Key).ToList())
            {
                _recent.Remove(expired);
            }

            if (_recent.TryGetValue(key, out var recent))
            {
                return Task.FromResult(recent.Response);
            }

            if (!_inFlight.TryGetValue(key, out shared!))
            {
                // Started on the pool so the entry is registered before the fetch can complete and
                // remove it (FetchAsync's finally needs this lock).
                shared = Task.Run(() => FetchAsync(key, send), CancellationToken.None);
                _inFlight[key] = shared;
            }
        }

        // Each caller keeps its own cancellation: cancelling one waiter never cancels the shared fetch.
        return shared.WaitAsync(cancellationToken);
    }

    private async Task<RetrievalHttpResponse> FetchAsync(string key, Func<Task<RetrievalHttpResponse>> send)
    {
        try
        {
            // The shared fetch is not tied to any one caller's token; the inner client's own timeout bounds it.
            var response = await send().ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                lock (_lock)
                {
                    _recent[key] = (response, clock.UtcNow);
                }
            }

            return response;
        }
        finally
        {
            lock (_lock)
            {
                _inFlight.Remove(key);
            }
        }
    }
}
