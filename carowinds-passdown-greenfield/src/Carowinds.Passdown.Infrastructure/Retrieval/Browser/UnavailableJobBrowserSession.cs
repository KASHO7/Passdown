using Carowinds.Passdown.Application.Retrieval.Browser;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Browser;

/// <summary>
/// <see cref="IJobBrowserSession"/> for a build with no browser-automation dependency wired in.
/// Phase 7B removed Microsoft.Playwright from the shipped dependency graph because every
/// currently-implemented source adapter is DirectApi or Html strategy, never Browser
/// (docs/release-strategy.md "Playwright dependency decision") — its ~88 MB Node driver was pure
/// release weight for a feature nothing used. <see cref="EnsureStartedAsync"/> throws rather than
/// silently succeeding, so a future
/// source registration that sets <c>RequiresBrowserSession: true</c> fails loudly at the point a
/// browser session is actually needed instead of pretending browser support exists.
/// </summary>
public sealed class UnavailableJobBrowserSession : IJobBrowserSession
{
    public Task EnsureStartedAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "Browser-based retrieval is not available in this build: Microsoft.Playwright was " +
            "removed from the production dependency graph (Phase 7B) because no source adapter " +
            "used it. Restore a browser-backed IJobBrowserSessionFactory registration before " +
            "adding a source with RequiresBrowserSession: true.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary><see cref="IJobBrowserSessionFactory"/> that hands out <see cref="UnavailableJobBrowserSession"/> instances. <see cref="Create"/> itself does no I/O, matching the contract every other implementation of this interface must honor.</summary>
public sealed class UnavailableJobBrowserSessionFactory : IJobBrowserSessionFactory
{
    public IJobBrowserSession Create() => new UnavailableJobBrowserSession();
}
