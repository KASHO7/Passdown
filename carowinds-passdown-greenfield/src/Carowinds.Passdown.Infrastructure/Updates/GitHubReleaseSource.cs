using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Carowinds.Passdown.Application.Updates;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Updates;

/// <summary>
/// Phase 23D: <see cref="IUpdateReleaseSource"/> for a public GitHub repository's Releases — no login, no
/// token, no secret. Uses the documented REST endpoint <c>GET /repos/{owner}/{repo}/releases</c> (which,
/// unlike "latest release", includes prereleases), with ETag revalidation so unchanged listings cost little.
/// Download URLs are built from the configured repository, the tag and the expected asset name — never taken
/// from release metadata or release notes — and every hop (including redirects to GitHub's asset CDN) must be
/// HTTPS on an allow-listed GitHub host. Response sizes are bounded. Everything returned is untrusted until
/// the signed manifest verifies.
/// </summary>
public sealed class GitHubReleaseSource : IUpdateReleaseSource
{
    public const string ApiHost = "api.github.com";

    /// <summary>Hosts a release-asset download may pass through (github.com redirects to its asset CDN).</summary>
    public static IReadOnlySet<string> AllowedDownloadHosts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "github-releases.githubusercontent.com",
    };

    public const int MaximumListingBytes = 4 * 1024 * 1024;

    public const int MaximumRedirects = 5;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>A package download fails when no bytes arrive for this long (slow is fine; stalled is not).</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(45);

    private readonly HttpClient _httpClient;
    private readonly UpdateRepository _repository;
    private readonly string _userAgent;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly ILogger? _logger;
    private string? _etag;
    private IReadOnlyList<ReleaseCandidate>? _cachedListing;

    /// <param name="httpClient">Must be created with automatic redirects disabled; this class follows and validates redirects itself.</param>
    public GitHubReleaseSource(HttpClient httpClient, UpdateRepository repository, string applicationVersion, Func<DateTimeOffset>? utcNow = null, ILogger<GitHubReleaseSource>? logger = null)
    {
        _httpClient = httpClient;
        _repository = repository;
        _userAgent = $"CarowindsOperations/{applicationVersion}";
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _logger = logger;
    }

    /// <summary>Creates the HTTP client this source expects: no automatic redirects, no cookies, TLS via the OS.</summary>
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

    public Uri ListingUri => new($"https://{ApiHost}/repos/{_repository.Owner}/{_repository.Name}/releases?per_page=30");

    public Uri AssetUri(ReleaseCandidate release, string assetName) =>
        new($"https://github.com/{_repository.Owner}/{_repository.Name}/releases/download/{Uri.EscapeDataString(release.Tag)}/{Uri.EscapeDataString(assetName)}");

    public async Task<IReadOnlyList<ReleaseCandidate>> ListReleasesAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, ListingUri);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (_etag is not null && _cachedListing is not null && EntityTagHeaderValue.TryParse(_etag, out var etag))
        {
            request.Headers.IfNoneMatch.Add(etag);
        }

        using var response = await SendAsync(request, timeout.Token, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && _cachedListing is not null)
        {
            return _cachedListing;
        }

        ThrowIfUnsuccessful(response);
        var body = await ReadBoundedAsync(response, MaximumListingBytes, timeout.Token, cancellationToken).ConfigureAwait(false);
        var listing = ParseListing(body);
        _etag = response.Headers.ETag?.ToString();
        _cachedListing = listing;
        return listing;
    }

    public async Task<byte[]> DownloadSmallAssetAsync(ReleaseCandidate release, string assetName, int maximumBytes, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var response = await GetAssetAsync(release, assetName, timeout.Token, cancellationToken).ConfigureAwait(false);
        return await ReadBoundedAsync(response, maximumBytes, timeout.Token, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadPackageAsync(ReleaseCandidate release, string assetName, Stream destination, long expectedBytes, IProgress<long>? progress, CancellationToken cancellationToken = default)
    {
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connect.CancelAfter(RequestTimeout);
        using var response = await GetAssetAsync(release, assetName, connect.Token, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is { } length && length != expectedBytes)
        {
            throw new UpdateSourceException(UpdateSourceFailureKind.InvalidResponse, "The package size reported by the server does not match the signed manifest.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(StallTimeout);
            int read;
            try
            {
                read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpdateSourceException(UpdateSourceFailureKind.Timeout, "The download stopped receiving data.");
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                throw new UpdateSourceException(UpdateSourceFailureKind.Offline, "The download was interrupted.", innerException: ex);
            }

            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > expectedBytes)
            {
                throw new UpdateSourceException(UpdateSourceFailureKind.InvalidResponse, "The download is larger than the signed manifest allows.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progress?.Report(total);
        }

        if (total != expectedBytes)
        {
            throw new UpdateSourceException(UpdateSourceFailureKind.Offline, "The download ended early.");
        }
    }

    /// <summary>Parses GitHub's release listing. Releases whose tag is not "v" + a semantic version are skipped.</summary>
    public static IReadOnlyList<ReleaseCandidate> ParseListing(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new UpdateSourceException(UpdateSourceFailureKind.InvalidResponse, "The release listing is not a list.");
            }

            var releases = new List<ReleaseCandidate>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String
                    || !UpdateAssetNames.TryParseTag(tagElement.GetString(), out var version))
                {
                    continue;
                }

                var draft = !element.TryGetProperty("draft", out var draftElement) || draftElement.ValueKind != JsonValueKind.False;
                string? title = element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
                DateTimeOffset? published = element.TryGetProperty("published_at", out var publishedElement) && publishedElement.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(publishedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                    ? parsed
                    : null;

                var assets = new List<ReleaseAsset>();
                if (element.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsElement.EnumerateArray())
                    {
                        if (asset.ValueKind == JsonValueKind.Object
                            && asset.TryGetProperty("name", out var assetName) && assetName.ValueKind == JsonValueKind.String
                            && asset.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var bytes) && bytes >= 0)
                        {
                            assets.Add(new ReleaseAsset(assetName.GetString()!, bytes));
                        }
                    }
                }

                // A release that does not say draft:false is treated as a draft (fail closed).
                releases.Add(new ReleaseCandidate(tagElement.GetString()!, version, title, published, draft, assets));
            }

            return releases;
        }
        catch (JsonException ex)
        {
            throw new UpdateSourceException(UpdateSourceFailureKind.InvalidResponse, "The release listing could not be read.", innerException: ex);
        }
    }

    private async Task<HttpResponseMessage> GetAssetAsync(ReleaseCandidate release, string assetName, CancellationToken timeoutToken, CancellationToken callerToken)
    {
        var uri = AssetUri(release, assetName);
        for (var hop = 0; ; hop++)
        {
            if (!IsAllowedDownloadUri(uri))
            {
                throw new UpdateSourceException(UpdateSourceFailureKind.InvalidResponse, "A download redirected to an untrusted address.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            var response = await SendAsync(request, timeoutToken, callerToken).ConfigureAwait(false);
            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null || hop >= MaximumRedirects)
                {
                    throw new UpdateSourceException(UpdateSourceFailureKind.InvalidResponse, "A download redirected too many times.");
                }

                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }

            try
            {
                ThrowIfUnsuccessful(response);
            }
            catch
            {
                response.Dispose();
                throw;
            }

            return response;
        }
    }

    public static bool IsAllowedDownloadUri(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && AllowedDownloadHosts.Contains(uri.Host);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken timeoutToken, CancellationToken callerToken)
    {
        request.Headers.UserAgent.ParseAdd(_userAgent);
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new UpdateSourceException(UpdateSourceFailureKind.Timeout, "GitHub did not respond in time.");
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogDebug("Release source request failed ({Error}).", ex.HttpRequestError);
            throw new UpdateSourceException(UpdateSourceFailureKind.Offline, "GitHub could not be reached.", innerException: ex);
        }
    }

    private void ThrowIfUnsuccessful(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden && IsRateLimited(response, out var retryAfter))
        {
            throw new UpdateSourceException(UpdateSourceFailureKind.RateLimited, "GitHub asked to wait before checking again.", retryAfter);
        }

        throw new UpdateSourceException(UpdateSourceFailureKind.HttpError, $"GitHub answered {(int)response.StatusCode}.");
    }

    private bool IsRateLimited(HttpResponseMessage response, out DateTimeOffset? retryAfter)
    {
        retryAfter = null;
        if (response.Headers.RetryAfter is { } header)
        {
            retryAfter = header.Date ?? (header.Delta is { } delta ? _utcNow() + delta : null);
            return true;
        }

        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0")
        {
            if (response.Headers.TryGetValues("x-ratelimit-reset", out var reset)
                && long.TryParse(reset.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var epoch))
            {
                retryAfter = DateTimeOffset.FromUnixTimeSeconds(epoch);
            }

            return true;
        }

        return response.StatusCode == HttpStatusCode.TooManyRequests;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximumBytes, CancellationToken timeoutToken, CancellationToken callerToken)
    {
        if (response.Content.Headers.ContentLength is { } length && length > maximumBytes)
        {
            throw new UpdateSourceException(UpdateSourceFailureKind.InvalidResponse, "The response is larger than expected.");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(timeoutToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeoutToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maximumBytes)
                {
                    throw new UpdateSourceException(UpdateSourceFailureKind.InvalidResponse, "The response is larger than expected.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new UpdateSourceException(UpdateSourceFailureKind.Timeout, "GitHub stopped responding.");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new UpdateSourceException(UpdateSourceFailureKind.Offline, "The connection was interrupted.", innerException: ex);
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
