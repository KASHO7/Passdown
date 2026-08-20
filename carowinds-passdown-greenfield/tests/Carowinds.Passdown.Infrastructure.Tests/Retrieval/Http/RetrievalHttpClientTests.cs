using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Http;

public class RetrievalHttpClientTests
{
    private static readonly Uri SampleUri = new("https://example.invalid/park-hours");

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    [Fact]
    public async Task GetAsync_ReturnsStatusAndBody_OnSuccess()
    {
        using var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"open\":\"9:00 AM\"}", System.Text.Encoding.UTF8, "application/json"),
        }));
        using var httpClient = new HttpClient(handler);
        var client = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(5) });

        var response = await client.GetAsync(SampleUri, CancellationToken.None);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal("{\"open\":\"9:00 AM\"}", response.Body);
    }

    [Fact]
    public async Task GetAsync_WhenSlowerThanConfiguredTimeout_ThrowsTimeoutException()
    {
        using var handler = new FakeHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        var client = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromMilliseconds(50) });

        await Assert.ThrowsAsync<TimeoutException>(() => client.GetAsync(SampleUri, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_WhenCallerCancels_ThrowsOperationCanceledRatherThanTimeoutException()
    {
        using var handler = new FakeHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        var client = new RetrievalHttpClient(httpClient, new RetrievalHttpOptions { Timeout = TimeSpan.FromSeconds(30) });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(SampleUri, cts.Token));
    }
}
