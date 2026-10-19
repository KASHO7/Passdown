using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;
using Carowinds.Passdown.Infrastructure.Updates;

namespace Carowinds.Passdown.Infrastructure.Tests.Updates;

/// <summary>Phase 23D: the GitHub Releases provider against synthetic HTTP responses — never the live API.</summary>
public class GitHubReleaseSourceTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    private static UpdateRepository Repository => UpdateRepository.TryParse("example-owner/example-repo", out var r) ? r : throw new InvalidOperationException();

    private static GitHubReleaseSource Source(HttpMessageHandler handler) => new(new HttpClient(handler), Repository, "0.1.0-rc.7", () => new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Listing = """
        [
          { "tag_name": "v0.1.0-rc.8", "name": "RC 8", "draft": false, "prerelease": true, "published_at": "2026-10-20T15:00:00Z",
            "assets": [ { "name": "Carowinds-Operations-0.1.0-rc.8-win-x64.zip", "size": 90000000, "browser_download_url": "https://evil.example/x.zip" } ] },
          { "tag_name": "v0.1.0-rc.9", "name": "Draft", "draft": true, "assets": [] },
          { "tag_name": "nightly", "draft": false, "assets": [] },
          { "tag_name": "v0.1.0-rc.10", "assets": [] }
        ]
        """;

    [Fact]
    public async Task Listing_UsesThePublicApi_WithoutCredentials_AndParsesReleases()
    {
        var handler = new FakeHandler(_ => Json(Listing));
        var releases = await Source(handler).ListReleasesAsync();

        var request = handler.Requests.Single();
        Assert.Equal("https://api.github.com/repos/example-owner/example-repo/releases?per_page=30", request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.Contains("CarowindsOperations/0.1.0-rc.7", request.Headers.UserAgent.ToString());

        Assert.Equal(["v0.1.0-rc.8", "v0.1.0-rc.9", "v0.1.0-rc.10"], releases.Select(r => r.Tag));
        Assert.False(releases[0].IsDraft);
        Assert.True(releases[1].IsDraft);
        Assert.True(releases[2].IsDraft); // no "draft": false → treated as a draft (fail closed)
        Assert.Equal(90000000, releases[0].Assets.Single().Size);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero), releases[0].PublishedAtUtc);
    }

    [Fact]
    public async Task UnchangedListing_IsRevalidatedWithETag()
    {
        var calls = 0;
        var handler = new FakeHandler(request =>
        {
            calls++;
            if (request.Headers.IfNoneMatch.Count > 0)
            {
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            var response = Json(Listing);
            response.Headers.ETag = new EntityTagHeaderValue("\"abc\"");
            return response;
        });
        var source = Source(handler);

        var first = await source.ListReleasesAsync();
        var second = await source.ListReleasesAsync();

        Assert.Equal(2, calls);
        Assert.Equal("\"abc\"", handler.Requests[1].Headers.IfNoneMatch.Single().ToString());
        Assert.Same(first, second);
    }

    [Fact]
    public async Task RateLimit_FromHeaders_IsRateLimitedWithResetTime()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = Json("{\"message\":\"API rate limit exceeded\"}", HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", "1792339200");
            return response;
        });

        var ex = await Assert.ThrowsAsync<UpdateSourceException>(() => Source(handler).ListReleasesAsync());
        Assert.Equal(UpdateSourceFailureKind.RateLimited, ex.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792339200), ex.RetryAfterUtc);
    }

    [Fact]
    public async Task TooManyRequests_WithRetryAfter_IsRateLimited()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return response;
        });

        var ex = await Assert.ThrowsAsync<UpdateSourceException>(() => Source(handler).ListReleasesAsync());
        Assert.Equal(UpdateSourceFailureKind.RateLimited, ex.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 12, 2, 0, TimeSpan.Zero), ex.RetryAfterUtc);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task HttpErrors_AreHttpError(HttpStatusCode status)
    {
        var ex = await Assert.ThrowsAsync<UpdateSourceException>(() => Source(new FakeHandler(_ => Json("{}", status))).ListReleasesAsync());
        Assert.Equal(UpdateSourceFailureKind.HttpError, ex.Kind);
    }

    [Fact]
    public async Task Offline_IsOffline()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("No such host is known."));
        var ex = await Assert.ThrowsAsync<UpdateSourceException>(() => Source(handler).ListReleasesAsync());
        Assert.Equal(UpdateSourceFailureKind.Offline, ex.Kind);
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsTimeout()
    {
        using var cancel = new CancellationTokenSource();
        var task = Source(new HangingHandler()).ListReleasesAsync(cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Theory]
    [InlineData("{\"not\":\"a list\"}")]
    [InlineData("<html>maintenance</html>")]
    public async Task MalformedListing_IsInvalidResponse(string body)
    {
        var ex = await Assert.ThrowsAsync<UpdateSourceException>(() => Source(new FakeHandler(_ => Json(body))).ListReleasesAsync());
        Assert.Equal(UpdateSourceFailureKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public async Task OversizeListing_IsRefused()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[GitHubReleaseSource.MaximumListingBytes + 1]) });
        var ex = await Assert.ThrowsAsync<UpdateSourceException>(() => Source(handler).ListReleasesAsync());
        Assert.Equal(UpdateSourceFailureKind.InvalidResponse, ex.Kind);
    }

    private static ReleaseCandidate Release => new("v0.1.0-rc.8", SemanticVersion.Parse("0.1.0-rc.8"), null, null, false, []);

    [Fact]
    public async Task AssetDownloads_UseAConstructedGitHubUrl_NotMetadataUrls_AndFollowAllowedRedirects()
    {
        var handler = new FakeHandler(request => request.RequestUri!.Host switch
        {
            "github.com" => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://release-assets.githubusercontent.com/abc?sig=1") } },
            "release-assets.githubusercontent.com" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("manifest"u8.ToArray()) },
            _ => throw new InvalidOperationException("unexpected host"),
        });

        var bytes = await Source(handler).DownloadSmallAssetAsync(Release, "Carowinds-Operations-0.1.0-rc.8-win-x64.update.json", 1024);

        Assert.Equal("manifest", Encoding.UTF8.GetString(bytes));
        Assert.Equal("https://github.com/example-owner/example-repo/releases/download/v0.1.0-rc.8/Carowinds-Operations-0.1.0-rc.8-win-x64.update.json", handler.Requests[0].RequestUri!.ToString());
    }

    [Theory]
    [InlineData("https://evil.example/payload.zip")]
    [InlineData("http://objects.githubusercontent.com/payload.zip")]
    [InlineData("https://objects.githubusercontent.com:8443/payload.zip")]
    [InlineData("https://user@objects.githubusercontent.com/payload.zip")]
    public async Task RedirectToAnUntrustedOrInsecureAddress_IsRefused(string location)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } });
        var ex = await Assert.ThrowsAsync<UpdateSourceException>(() => Source(handler).DownloadSmallAssetAsync(Release, "a.json", 1024));
        Assert.Equal(UpdateSourceFailureKind.InvalidResponse, ex.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RedirectLoop_IsRefused()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://github.com/loop") } });
        await Assert.ThrowsAsync<UpdateSourceException>(() => Source(handler).DownloadSmallAssetAsync(Release, "a.json", 1024));
        Assert.Equal(GitHubReleaseSource.MaximumRedirects + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task OversizeSmallAsset_IsRefused()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2048]) });
        await Assert.ThrowsAsync<UpdateSourceException>(() => Source(handler).DownloadSmallAssetAsync(Release, "a.json", 1024));
    }

    [Fact]
    public async Task PackageDownload_ReportsProgress_AndRefusesAnythingLongerOrShorterThanSigned()
    {
        var payload = new byte[200_000];
        Random.Shared.NextBytes(payload);
        HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

        var reported = new List<long>();
        using (var output = new MemoryStream())
        {
            await Source(new FakeHandler(_ => Ok(payload))).DownloadPackageAsync(Release, "p.zip", output, payload.Length, new SyncProgress(reported.Add));
            Assert.Equal(payload, output.ToArray());
            Assert.Equal(payload.Length, reported[^1]);
        }

        await Assert.ThrowsAsync<UpdateSourceException>(() => Source(new FakeHandler(_ => Ok(payload))).DownloadPackageAsync(Release, "p.zip", new MemoryStream(), payload.Length - 1, null));
        await Assert.ThrowsAsync<UpdateSourceException>(() => Source(new FakeHandler(_ => Ok(payload[..1000]))).DownloadPackageAsync(Release, "p.zip", new MemoryStream(), payload.Length, null));
    }

    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
