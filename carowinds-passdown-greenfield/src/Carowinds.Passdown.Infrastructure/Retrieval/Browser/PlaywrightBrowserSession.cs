using Microsoft.Playwright;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Browser;

/// <summary>
/// <see cref="IBrowserSession"/> backed by a real Playwright browser/context. Cancellation is
/// honored at each call boundary — Playwright's .NET API does not accept a
/// <see cref="CancellationToken"/> on most calls, so this checks the token immediately before
/// starting a Playwright operation. Per-page navigation timeouts are each adapter's own concern
/// (this type has no source-specific navigation logic), and bound how long any single in-flight
/// Playwright call can run.
/// </summary>
internal sealed class PlaywrightBrowserSession(IPlaywright playwright, IBrowser browser, IBrowserContext context) : IBrowserSession
{
    private int _disposed;

    public async Task<IPage> NewPageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await context.NewPageAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await context.CloseAsync().ConfigureAwait(false);
        await browser.CloseAsync().ConfigureAwait(false);
        playwright.Dispose();
    }
}
