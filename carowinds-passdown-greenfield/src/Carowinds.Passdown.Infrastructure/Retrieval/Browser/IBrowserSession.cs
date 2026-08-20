using Microsoft.Playwright;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Browser;

/// <summary>
/// One shared Playwright browser and browsing context for a single refresh job (spec §6.4: "one
/// browser per refresh job, not per source" / "reuse the browser context"). Adapters that need
/// Playwright ask a session for a page rather than launching their own browser, so a job with
/// several due Playwright-backed sources still starts exactly one browser process. Carries no
/// source-specific navigation/selector logic — that belongs entirely to whichever adapter uses
/// the page it gets back.
/// </summary>
public interface IBrowserSession : IAsyncDisposable
{
    /// <summary>
    /// Opens a new page in the session's shared context. Each caller gets its own page so
    /// concurrent sources don't interfere with each other's navigation/DOM state, while still
    /// sharing the one browser process and context — cookies, storage, request routing — per
    /// spec §6.4.
    /// </summary>
    Task<IPage> NewPageAsync(CancellationToken cancellationToken);
}
