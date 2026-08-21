using Carowinds.Passdown.Infrastructure.Retrieval.Browser;
using Microsoft.Playwright;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Browser;

/// <summary>
/// <see cref="PlaywrightJobBrowserSession"/>'s "start at most once, lazily, shared across every
/// caller in the job" contract (spec §6.4/Phase 3G Part E) currently has no adapter that exercises
/// it in production (every Phase 3 registration sets <c>requiresBrowserSession: false</c>), so
/// this is covered directly against a fake <see cref="IBrowserSessionFactory"/> rather than only
/// indirectly through an adapter — Phase 3H review gap.
/// </summary>
public class PlaywrightJobBrowserSessionTests
{
    [Fact]
    public async Task EnsureStartedAsync_CalledTwiceSequentially_StartsTheUnderlyingSessionOnlyOnce()
    {
        var factory = new CountingBrowserSessionFactory();
        var jobSession = new PlaywrightJobBrowserSession(factory);

        await jobSession.EnsureStartedAsync(CancellationToken.None);
        await jobSession.EnsureStartedAsync(CancellationToken.None);

        Assert.Equal(1, factory.StartCount);
    }

    [Fact]
    public async Task EnsureStartedAsync_CalledConcurrentlyByManySources_StartsTheUnderlyingSessionOnlyOnce()
    {
        var factory = new CountingBrowserSessionFactory();
        var jobSession = new PlaywrightJobBrowserSession(factory);

        var callers = Enumerable.Range(0, 8).Select(_ => jobSession.EnsureStartedAsync(CancellationToken.None));
        await Task.WhenAll(callers);

        Assert.Equal(1, factory.StartCount);
    }

    [Fact]
    public async Task Create_DoesNoIoUntilEnsureStartedAsyncIsAwaited()
    {
        var factory = new CountingBrowserSessionFactory();
        var jobSessionFactory = new PlaywrightJobBrowserSessionFactory(factory);

        var jobSession = jobSessionFactory.Create();

        Assert.Equal(0, factory.StartCount);
        await jobSession.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WhenNeverStarted_DoesNotThrowAndDoesNotStart()
    {
        var factory = new CountingBrowserSessionFactory();
        var jobSession = new PlaywrightJobBrowserSession(factory);

        await jobSession.DisposeAsync();

        Assert.Equal(0, factory.StartCount);
    }

    [Fact]
    public async Task DisposeAsync_WhenStarted_DisposesTheUnderlyingSession()
    {
        var factory = new CountingBrowserSessionFactory();
        var jobSession = new PlaywrightJobBrowserSession(factory);

        await jobSession.EnsureStartedAsync(CancellationToken.None);
        await jobSession.DisposeAsync();

        Assert.True(factory.LastSession!.Disposed);
    }

    private sealed class CountingBrowserSessionFactory : IBrowserSessionFactory
    {
        private int _startCount;

        public int StartCount => _startCount;
        public FakeBrowserSession? LastSession { get; private set; }

        public Task<IBrowserSession> StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startCount);
            var session = new FakeBrowserSession();
            LastSession = session;
            return Task.FromResult<IBrowserSession>(session);
        }
    }

    private sealed class FakeBrowserSession : IBrowserSession
    {
        public bool Disposed { get; private set; }

        public Task<IPage> NewPageAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by these laziness/once-only tests.");

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
