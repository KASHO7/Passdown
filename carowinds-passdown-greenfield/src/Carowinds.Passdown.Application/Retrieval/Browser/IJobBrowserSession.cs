namespace Carowinds.Passdown.Application.Retrieval.Browser;

/// <summary>
/// One lazily-started, job-scoped browser session (spec §6.4: "one browser per refresh job, not
/// per source"). Application depends only on this shape — not on Playwright — so orchestration
/// stays free of any browser-automation dependency (CLAUDE.md "Architecture"); the concrete
/// Playwright-backed implementation lives in Infrastructure. <see cref="EnsureStartedAsync"/> is
/// idempotent and must not do any work (let alone launch a browser) until first called, so a job
/// with no browser-requiring due source never starts one (spec §6.4/Phase 3G Part E).
/// </summary>
public interface IJobBrowserSession : IAsyncDisposable
{
    /// <summary>Starts the underlying browser/context on first call; a no-op on every later call for this same session instance.</summary>
    Task EnsureStartedAsync(CancellationToken cancellationToken);
}

/// <summary>Creates one fresh, not-yet-started <see cref="IJobBrowserSession"/> per refresh job. <see cref="Create"/> itself must do no I/O — only <see cref="IJobBrowserSession.EnsureStartedAsync"/> may.</summary>
public interface IJobBrowserSessionFactory
{
    IJobBrowserSession Create();
}
