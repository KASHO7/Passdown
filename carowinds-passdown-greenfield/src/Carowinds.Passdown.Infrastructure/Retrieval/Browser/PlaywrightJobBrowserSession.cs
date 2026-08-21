using Carowinds.Passdown.Application.Retrieval.Browser;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Browser;

/// <summary>
/// <see cref="IJobBrowserSession"/> over <see cref="IBrowserSessionFactory"/>: the actual
/// Playwright browser/context is only launched the first time <see cref="EnsureStartedAsync"/> is
/// awaited (spec §6.4/Phase 3G Part E "browser creation must remain lazy"), and every later call
/// on the same instance is a no-op — so a job with several browser-requiring due sources still
/// starts exactly one browser process, shared across all of them via the one underlying
/// <see cref="IBrowserSession"/>.
/// </summary>
public sealed class PlaywrightJobBrowserSession(IBrowserSessionFactory sessionFactory) : IJobBrowserSession
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private IBrowserSession? _session;

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            return;
        }

        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _session ??= await sessionFactory.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }

        _startLock.Dispose();
    }
}

/// <summary><see cref="IJobBrowserSessionFactory"/> that hands out <see cref="PlaywrightJobBrowserSession"/> instances.</summary>
public sealed class PlaywrightJobBrowserSessionFactory(IBrowserSessionFactory sessionFactory) : IJobBrowserSessionFactory
{
    public IJobBrowserSession Create() => new PlaywrightJobBrowserSession(sessionFactory);
}
