using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Tests.TestSupport;

/// <summary>
/// Stand-in <see cref="IRetrievalHttpClient"/> that returns one queued response per call (or a
/// fixed exception), so adapters that make more than one HTTP call per <c>RetrieveAsync</c> —
/// e.g. <see cref="Carowinds.Passdown.Infrastructure.Retrieval.Sources.DatedFastLaneSource"/>'s
/// primary-then-horizon design — can be tested against each call's response in order. Records
/// every request so a test can assert an adapter made the right number of calls with the right
/// bodies/headers. <see cref="FakeRetrievalHttpClient"/> remains the simpler single-response stand-in
/// for adapters that only ever make one call.
/// </summary>
internal sealed class StubHttpClient : IRetrievalHttpClient
{
    private readonly Queue<RetrievalHttpResponse> _responses;
    private readonly Exception? _exception;

    public List<RecordedRequest> Requests { get; } = [];

    public StubHttpClient(IEnumerable<RetrievalHttpResponse> responses) => _responses = new Queue<RetrievalHttpResponse>(responses);

    public StubHttpClient(Exception exception)
    {
        _exception = exception;
        _responses = new Queue<RetrievalHttpResponse>();
    }

    public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        Requests.Add(new RecordedRequest(requestUri, null, null));
        return Respond();
    }

    public Task<RetrievalHttpResponse> PostJsonAsync(
        Uri requestUri,
        string jsonBody,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        Requests.Add(new RecordedRequest(requestUri, jsonBody, headers));
        return Respond();
    }

    private Task<RetrievalHttpResponse> Respond() =>
        _exception is not null
            ? Task.FromException<RetrievalHttpResponse>(_exception)
            : Task.FromResult(_responses.Dequeue());
}

internal sealed record RecordedRequest(Uri Uri, string? Body, IReadOnlyDictionary<string, string>? Headers);
