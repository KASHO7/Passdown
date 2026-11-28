using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Carowinds.Passdown.Application.Weather;

namespace Carowinds.Passdown.Infrastructure.Weather.Nws;

/// <summary>One NWS HTTP response: the status, the body (for 2xx), and the cache and retry headers the provider uses.</summary>
public sealed record NwsHttpResponse(
    HttpStatusCode StatusCode,
    string? Body,
    string? ContentType,
    string? ETag,
    DateTimeOffset? LastModified,
    DateTimeOffset? Expires,
    TimeSpan? MaxAge,
    TimeSpan? RetryAfter);

/// <summary>Transport seam for <see cref="NwsWeatherProvider"/>; tests replace it or its <see cref="HttpMessageHandler"/>.</summary>
public interface INwsHttpTransport
{
    Task<NwsHttpResponse> GetAsync(Uri requestUri, WeatherConditionalHeaders conditional, CancellationToken cancellationToken);
}

/// <summary>
/// Phase 37B: the HTTP transport for <c>api.weather.gov</c> only. Deliberately separate from the shared official
/// <c>IRetrievalHttpClient</c>, which exposes no response headers and sends no User-Agent. Sends the
/// identifying User-Agent NWS requires (owner decision D3: versioned, with the public repository URL, no
/// private contact), <c>Accept: application/geo+json</c>, and <c>If-None-Match</c> / <c>If-Modified-Since</c>
/// when validators exist. Follows at most two redirects, and only to <c>https://api.weather.gov</c>. Bodies are
/// capped at <see cref="MaxResponseBytes"/>. A timeout throws <see cref="TimeoutException"/>; caller
/// cancellation throws <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class NwsHttpTransport : INwsHttpTransport
{
    public const string ApiHost = "api.weather.gov";

    public const string PublicRepositoryUrl = "https://github.com/KASHO7/Passdown";

    public const int MaxResponseBytes = 2 * 1024 * 1024;

    private const int MaxRedirects = 2;

    private readonly HttpClient _httpClient;
    private readonly string _userAgent;
    private readonly TimeSpan _timeout;

    /// <param name="httpClient">Create with <see cref="CreateHttpClient"/>: automatic redirects off, so this class validates every hop.</param>
    public NwsHttpTransport(HttpClient httpClient, string userAgent, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);
        _httpClient = httpClient;
        _userAgent = userAgent;
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
    }

    /// <summary><c>CarowindsOperations/0.1.0-rc.15 (+https://github.com/KASHO7/Passdown)</c>.</summary>
    public static string BuildUserAgent(string applicationVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        var version = applicationVersion.Split('+', 2)[0].Trim();
        return $"CarowindsOperations/{version} (+{PublicRepositoryUrl})";
    }

    /// <summary>No automatic redirects, no cookies, gzip/deflate, TLS via the OS.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>True only for an absolute <c>https://api.weather.gov</c> address on the default port without user-info.</summary>
    public static bool IsApiUri(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.Equals(uri.IdnHost, ApiHost, StringComparison.OrdinalIgnoreCase);

    public async Task<NwsHttpResponse> GetAsync(Uri requestUri, WeatherConditionalHeaders conditional, CancellationToken cancellationToken)
    {
        if (!IsApiUri(requestUri))
        {
            throw new ArgumentException($"Only https://{ApiHost} addresses are requested.", nameof(requestUri));
        }

        ArgumentNullException.ThrowIfNull(conditional);

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            var uri = requestUri;
            for (var hop = 0; ; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/geo+json"));
                if (!string.IsNullOrEmpty(conditional.ETag))
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", conditional.ETag);
                }

                if (conditional.LastModified is { } lastModified)
                {
                    request.Headers.IfModifiedSince = lastModified;
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token).ConfigureAwait(false);

                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    var next = location is null ? null : location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (hop >= MaxRedirects || !IsApiUri(next))
                    {
                        return ToResponse(response, body: null);
                    }

                    uri = next!;
                    continue;
                }

                var body = response.IsSuccessStatusCode
                    ? await ReadCappedAsync(response.Content, linkedCts.Token).ConfigureAwait(false)
                    : null;
                return ToResponse(response, body);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Request to '{requestUri}' did not complete within {_timeout}.");
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static NwsHttpResponse ToResponse(HttpResponseMessage response, string? body)
    {
        TimeSpan? retryAfter = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - (response.Headers.Date ?? DateTimeOffset.UtcNow) is var wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero,
            _ => null,
        };

        return new NwsHttpResponse(
            response.StatusCode,
            body,
            response.Content.Headers.ContentType?.MediaType,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified,
            response.Content.Headers.Expires,
            response.Headers.CacheControl?.MaxAge,
            retryAfter);
    }

    private static async Task<string> ReadCappedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxResponseBytes)
        {
            throw new InvalidDataException($"NWS response exceeds {MaxResponseBytes} bytes.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                throw new InvalidDataException($"NWS response exceeds {MaxResponseBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
