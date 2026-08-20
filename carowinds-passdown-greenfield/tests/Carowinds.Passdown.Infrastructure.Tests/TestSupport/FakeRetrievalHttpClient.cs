using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Tests.TestSupport;

/// <summary>
/// Stand-in <see cref="IRetrievalHttpClient"/> that returns a fixed response or throws a fixed
/// exception, so adapter contract tests never touch the network (CLAUDE.md "Testing"). Records
/// the requested URI so a test can assert an adapter built the right request.
/// </summary>
internal sealed class FakeRetrievalHttpClient : IRetrievalHttpClient
{
    private readonly RetrievalHttpResponse? _response;
    private readonly Exception? _exception;

    public Uri? LastRequestedUri { get; private set; }

    public FakeRetrievalHttpClient(RetrievalHttpResponse response) => _response = response;

    public FakeRetrievalHttpClient(Exception exception) => _exception = exception;

    public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        LastRequestedUri = requestUri;
        return _exception is not null
            ? Task.FromException<RetrievalHttpResponse>(_exception)
            : Task.FromResult(_response!);
    }
}
