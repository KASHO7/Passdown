using System.Text;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Http;

/// <summary>
/// <see cref="IRetrievalHttpClient"/> over one shared, injected <see cref="HttpClient"/> (spec
/// §6.4: adapters share transport rather than each owning a client). Applies its own per-call
/// timeout via a linked <see cref="CancellationTokenSource"/> rather than
/// <see cref="HttpClient.Timeout"/>, so a timeout can be told apart from caller-initiated
/// cancellation — a timed-out call throws <see cref="TimeoutException"/> while a
/// caller-cancelled call throws <see cref="OperationCanceledException"/>, and adapters need that
/// distinction to report accurate <c>SourceEvidence</c> (spec §5.3).
/// </summary>
public sealed class RetrievalHttpClient(HttpClient httpClient, RetrievalHttpOptions options) : IRetrievalHttpClient
{
    public async Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestUri);

        using var timeoutCts = new CancellationTokenSource(options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using var response = await httpClient
                .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

            return new RetrievalHttpResponse(response.StatusCode, response.Content.Headers.ContentType?.MediaType, body);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Request to '{requestUri}' did not complete within {options.Timeout}.");
        }
    }

    public async Task<RetrievalHttpResponse> PostJsonAsync(
        Uri requestUri,
        string jsonBody,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(jsonBody);

        using var timeoutCts = new CancellationTokenSource(options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        try
        {
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

            return new RetrievalHttpResponse(response.StatusCode, response.Content.Headers.ContentType?.MediaType, body);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Request to '{requestUri}' did not complete within {options.Timeout}.");
        }
    }
}
