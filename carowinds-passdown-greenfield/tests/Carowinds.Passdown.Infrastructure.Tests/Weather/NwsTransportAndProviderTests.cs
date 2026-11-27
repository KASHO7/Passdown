using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Carowinds.Passdown.Application.Weather;
using Carowinds.Passdown.Domain.Weather;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Carowinds.Passdown.Infrastructure.Weather.Nws;

namespace Carowinds.Passdown.Infrastructure.Tests.Weather;

public class NwsHttpTransportTests
{
    private const string UserAgent = "CarowindsOperations/0.1.0-rc.15 (+https://github.com/KASHO7/Passdown)";

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

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/geo+json") };

    private static readonly Uri Forecast = new("https://api.weather.gov/gridpoints/GSP/116,59/forecast");

    [Fact]
    public void UserAgent_IsVersionedWithThePublicRepository_AndNoEmail()
    {
        var agent = NwsHttpTransport.BuildUserAgent("0.1.0-rc.15+3d603f05a1f6");

        Assert.Equal(UserAgent, agent);
        Assert.DoesNotContain("@", agent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_SendsIdentificationAcceptAndValidators_AndReturnsCacheHeaders()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = Json("{}");
            response.Headers.ETag = new EntityTagHeaderValue("\"abc\"", isWeak: true);
            response.Headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromSeconds(3600) };
            response.Content.Headers.LastModified = new DateTimeOffset(2026, 11, 27, 11, 13, 44, TimeSpan.Zero);
            response.Content.Headers.Expires = new DateTimeOffset(2026, 11, 27, 14, 49, 58, TimeSpan.Zero);
            return response;
        });
        var transport = new NwsHttpTransport(new HttpClient(handler), UserAgent);
        var lastModified = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

        var result = await transport.GetAsync(Forecast, new WeatherConditionalHeaders("W/\"old\"", lastModified), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(UserAgent, string.Join(' ', request.Headers.GetValues("User-Agent")));
        Assert.Equal("application/geo+json", request.Headers.Accept.Single().MediaType);
        Assert.Equal("W/\"old\"", request.Headers.GetValues("If-None-Match").Single());
        Assert.Equal(lastModified, request.Headers.IfModifiedSince);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("{}", result.Body);
        Assert.Equal("W/\"abc\"", result.ETag);
        Assert.Equal(TimeSpan.FromHours(1), result.MaxAge);
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 11, 13, 44, TimeSpan.Zero), result.LastModified);
        Assert.NotNull(result.Expires);
    }

    [Fact]
    public async Task Get_WithoutValidators_SendsNoConditionalHeaders()
    {
        var handler = new FakeHandler(_ => Json("{}"));
        await new NwsHttpTransport(new HttpClient(handler), UserAgent).GetAsync(Forecast, WeatherConditionalHeaders.None, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains("If-None-Match"));
        Assert.Null(request.Headers.IfModifiedSince);
    }

    [Fact]
    public async Task Get_NotModifiedAndRateLimited_CarryNoBody_ButKeepRetryAfter()
    {
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("forecast", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("slow down") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });
        var transport = new NwsHttpTransport(new HttpClient(handler), UserAgent);

        var notModified = await transport.GetAsync(Forecast, WeatherConditionalHeaders.None, CancellationToken.None);
        var limited = await transport.GetAsync(new Uri("https://api.weather.gov/alerts/active?point=35.1050,-80.9420"), WeatherConditionalHeaders.None, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Null(notModified.Body);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Null(limited.Body);
        Assert.Equal(TimeSpan.FromSeconds(7), limited.RetryAfter);
    }

    [Theory]
    [InlineData("http://api.weather.gov/points/35.1,-80.9")]
    [InlineData("https://api.weather.gov.evil.example/points/35.1,-80.9")]
    [InlineData("https://forecast.weather.gov/MapClick.php")]
    [InlineData("https://api.weather.gov:8443/points/35.1,-80.9")]
    public async Task Get_RefusesAnythingButHttpsApiWeatherGov(string address)
    {
        var handler = new FakeHandler(_ => Json("{}"));
        var transport = new NwsHttpTransport(new HttpClient(handler), UserAgent);

        await Assert.ThrowsAsync<ArgumentException>(() => transport.GetAsync(new Uri(address), WeatherConditionalHeaders.None, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Get_FollowsARedirectOnlyWithinTheApiHost()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/points/35.10504,-80.94201" => new HttpResponseMessage(HttpStatusCode.MovedPermanently) { Headers = { Location = new Uri("/points/35.105,-80.942", UriKind.Relative) } },
            "/points/35.105,-80.942" => Json("{\"ok\":true}"),
            _ => new HttpResponseMessage(HttpStatusCode.MovedPermanently) { Headers = { Location = new Uri("https://evil.example/x") } },
        });
        var transport = new NwsHttpTransport(new HttpClient(handler), UserAgent);

        var followed = await transport.GetAsync(new Uri("https://api.weather.gov/points/35.10504,-80.94201"), WeatherConditionalHeaders.None, CancellationToken.None);
        var refused = await transport.GetAsync(new Uri("https://api.weather.gov/other"), WeatherConditionalHeaders.None, CancellationToken.None);

        Assert.Equal("{\"ok\":true}", followed.Body);
        Assert.Equal(HttpStatusCode.MovedPermanently, refused.StatusCode);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.Host == "evil.example");
    }

    [Fact]
    public async Task Get_TimeoutAndCallerCancellation_AreDistinct()
    {
        var transport = new NwsHttpTransport(new HttpClient(new HangingHandler()), UserAgent, TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<TimeoutException>(() => transport.GetAsync(Forecast, WeatherConditionalHeaders.None, CancellationToken.None));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var patient = new NwsHttpTransport(new HttpClient(new HangingHandler()), UserAgent, TimeSpan.FromMinutes(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => patient.GetAsync(Forecast, WeatherConditionalHeaders.None, cts.Token));
    }

    [Fact]
    public async Task Get_OversizedBody_IsRefused()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[NwsHttpTransport.MaxResponseBytes + 1]) });
        var transport = new NwsHttpTransport(new HttpClient(handler), UserAgent);

        await Assert.ThrowsAsync<InvalidDataException>(() => transport.GetAsync(Forecast, WeatherConditionalHeaders.None, CancellationToken.None));
    }
}

public class NwsWeatherProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 13, 50, 0, TimeSpan.Zero);

    private sealed class StubTransport(Func<Uri, NwsHttpResponse> respond) : INwsHttpTransport
    {
        public List<(Uri Uri, WeatherConditionalHeaders Conditional)> Requests { get; } = [];

        public Func<Exception>? Throw { get; set; }

        public Task<NwsHttpResponse> GetAsync(Uri requestUri, WeatherConditionalHeaders conditional, CancellationToken cancellationToken)
        {
            Requests.Add((requestUri, conditional));
            cancellationToken.ThrowIfCancellationRequested();
            return Throw is { } fail ? Task.FromException<NwsHttpResponse>(fail()) : Task.FromResult(respond(requestUri));
        }
    }

    private static NwsHttpResponse Ok(string body, TimeSpan? maxAge = null, string? etag = null) =>
        new(HttpStatusCode.OK, body, "application/geo+json", etag, null, null, maxAge, null);

    private static NwsHttpResponse Status(HttpStatusCode status, TimeSpan? retryAfter = null) =>
        new(status, null, null, null, null, null, null, retryAfter);

    private static string Fixture(string name) => FixtureFile.ReadText($"weather/nws/{name}");

    private static NwsWeatherProvider Provider(StubTransport transport) => new(transport, () => Now);

    private static WeatherPointMetadata Grid => NwsPointParser.Parse(Fixture("points-nc-check-35.1037-80.9426-2026-11-27.json"));

    [Fact]
    public async Task Point_RequestsFourDecimalCoordinates_AndExpiresByMaxAge()
    {
        var transport = new StubTransport(_ => Ok(Fixture("points-nc-check-35.1037-80.9426-2026-11-27.json"), TimeSpan.FromDays(1)));

        var result = await Provider(transport).GetPointAsync(CarowindsWeatherLocation.NorthCarolina, CancellationToken.None);

        Assert.Equal("https://api.weather.gov/points/35.1050,-80.9420", transport.Requests.Single().Uri.AbsoluteUri);
        Assert.Equal(WeatherFetchOutcome.Retrieved, result.Outcome);
        Assert.Equal(Now.AddDays(1), result.ExpiresAt);
        Assert.Equal("GSP", result.Value!.GridId);
    }

    [Fact]
    public async Task Requests_UseTheExpectedPaths()
    {
        var transport = new StubTransport(uri => uri.AbsolutePath switch
        {
            var path when path.StartsWith("/alerts", StringComparison.Ordinal) => Ok(Fixture("alerts-active-none-2026-11-27.json")),
            var path when path.StartsWith("/stations/KCLT", StringComparison.Ordinal) => Ok(Fixture("observation-latest-kclt-2026-11-27.json")),
            var path when path.EndsWith("/stations", StringComparison.Ordinal) => Ok(Fixture("stations-gsp-116-59-2026-11-27-first3.json")),
            var path when path.EndsWith("/hourly", StringComparison.Ordinal) => Ok(Fixture("forecast-hourly-gsp-116-59-2026-11-27-first30.json")),
            _ => Ok(Fixture("forecast-gsp-116-59-2026-11-27.json")),
        });
        var provider = Provider(transport);
        var station = new WeatherStation("KCLT", "Charlotte/Douglas International Airport", 35.20833, -80.96139);

        Assert.Equal(WeatherFetchOutcome.Retrieved, (await provider.GetForecastAsync(Grid, WeatherConditionalHeaders.None, CancellationToken.None)).Outcome);
        Assert.Equal(WeatherFetchOutcome.Retrieved, (await provider.GetHourlyForecastAsync(Grid, WeatherConditionalHeaders.None, CancellationToken.None)).Outcome);
        Assert.Equal(3, (await provider.GetStationsAsync(Grid, CancellationToken.None)).Value!.Count);
        var observation = await provider.GetLatestObservationAsync(station, CarowindsWeatherLocation.NorthCarolina, WeatherConditionalHeaders.None, CancellationToken.None);
        Assert.Equal(11.6, observation.Value!.DistanceKm);
        Assert.Empty((await provider.GetActiveAlertsAsync(CarowindsWeatherLocation.SouthCarolina, WeatherConditionalHeaders.None, CancellationToken.None)).Value!);

        Assert.Equal(
            [
                "https://api.weather.gov/gridpoints/GSP/116,59/forecast",
                "https://api.weather.gov/gridpoints/GSP/116,59/forecast/hourly",
                "https://api.weather.gov/gridpoints/GSP/116,59/stations",
                "https://api.weather.gov/stations/KCLT/observations/latest",
                "https://api.weather.gov/alerts/active?point=35.1002,-80.9420",
            ],
            transport.Requests.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Conditional_IsPassedThrough_AndNotModifiedIsReported()
    {
        var transport = new StubTransport(_ => new NwsHttpResponse(HttpStatusCode.NotModified, null, null, "W/\"x\"", null, null, TimeSpan.FromSeconds(5), null));
        var conditional = new WeatherConditionalHeaders("W/\"x\"", null);

        var result = await Provider(transport).GetActiveAlertsAsync(CarowindsWeatherLocation.NorthCarolina, conditional, CancellationToken.None);

        Assert.Same(conditional, transport.Requests.Single().Conditional);
        Assert.Equal(WeatherFetchOutcome.NotModified, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal(Now.AddSeconds(5), result.ExpiresAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, WeatherFailureKind.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, WeatherFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, WeatherFailureKind.ProviderError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, WeatherFailureKind.ProviderError)]
    [InlineData(HttpStatusCode.Forbidden, WeatherFailureKind.ProviderError)]
    [InlineData(HttpStatusCode.MovedPermanently, WeatherFailureKind.ProviderError)]
    public async Task HttpErrors_MapToFailureKinds_WithRetryAfter(HttpStatusCode status, WeatherFailureKind expected)
    {
        var transport = new StubTransport(_ => Status(status, TimeSpan.FromSeconds(9)));

        var result = await Provider(transport).GetForecastAsync(Grid, WeatherConditionalHeaders.None, CancellationToken.None);

        Assert.Equal((WeatherFetchOutcome.Failed, expected), (result.Outcome, result.Failure));
        Assert.Null(result.Value);
        if (expected != WeatherFailureKind.NotFound)
        {
            Assert.Equal(TimeSpan.FromSeconds(9), result.RetryAfter);
        }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "properties": { "periods": [] } }""")]
    [InlineData("[]")]
    public async Task UnusableBodies_AreInvalidResponse(string body)
    {
        var transport = new StubTransport(_ => Ok(body));

        var result = await Provider(transport).GetForecastAsync(Grid, WeatherConditionalHeaders.None, CancellationToken.None);

        Assert.Equal(WeatherFailureKind.InvalidResponse, result.Failure);
    }

    [Fact]
    public async Task TransportExceptions_MapToFailureKinds_ButCallerCancellationIsRethrown()
    {
        var transport = new StubTransport(_ => Ok("{}"));
        var provider = Provider(transport);

        transport.Throw = () => new TimeoutException();
        Assert.Equal(WeatherFailureKind.Timeout, (await provider.GetForecastAsync(Grid, WeatherConditionalHeaders.None, CancellationToken.None)).Failure);
        transport.Throw = () => new HttpRequestException("offline");
        Assert.Equal(WeatherFailureKind.Network, (await provider.GetForecastAsync(Grid, WeatherConditionalHeaders.None, CancellationToken.None)).Failure);
        transport.Throw = () => new InvalidDataException();
        Assert.Equal(WeatherFailureKind.InvalidResponse, (await provider.GetForecastAsync(Grid, WeatherConditionalHeaders.None, CancellationToken.None)).Failure);

        transport.Throw = null;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetForecastAsync(Grid, WeatherConditionalHeaders.None, cts.Token));
    }

    [Fact]
    public async Task UnexpectedStationId_MakesNoRequest()
    {
        var transport = new StubTransport(_ => Ok("{}"));

        var result = await Provider(transport).GetLatestObservationAsync(new WeatherStation("../../alerts", "x", 0, 0), CarowindsWeatherLocation.NorthCarolina, WeatherConditionalHeaders.None, CancellationToken.None);

        Assert.Equal(WeatherFailureKind.InvalidResponse, result.Failure);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task PointWithAForeignForecastLink_IsInvalidResponse()
    {
        var json = Fixture("points-nc-check-35.1037-80.9426-2026-11-27.json").Replace("https://api.weather.gov/gridpoints/GSP/116,59/forecast/hourly", "https://evil.example/hourly", StringComparison.Ordinal);
        var transport = new StubTransport(_ => Ok(json));

        var result = await Provider(transport).GetPointAsync(CarowindsWeatherLocation.NorthCarolina, CancellationToken.None);

        Assert.Equal(WeatherFailureKind.InvalidResponse, result.Failure);
    }
}
