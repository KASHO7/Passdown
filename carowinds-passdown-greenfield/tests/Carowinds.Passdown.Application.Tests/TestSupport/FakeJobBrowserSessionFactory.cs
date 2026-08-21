using Carowinds.Passdown.Application.Retrieval.Browser;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class FakeJobBrowserSessionFactory : IJobBrowserSessionFactory
{
    public int SessionsCreated { get; private set; }

    public List<FakeJobBrowserSession> Sessions { get; } = [];

    public IJobBrowserSession Create()
    {
        SessionsCreated++;
        var session = new FakeJobBrowserSession();
        Sessions.Add(session);
        return session;
    }
}

internal sealed class FakeJobBrowserSession : IJobBrowserSession
{
    public int StartCount { get; private set; }

    public bool Disposed { get; private set; }

    public Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        StartCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
