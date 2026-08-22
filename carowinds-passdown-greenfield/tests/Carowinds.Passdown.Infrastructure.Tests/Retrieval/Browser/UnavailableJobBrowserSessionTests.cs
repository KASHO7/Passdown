using Carowinds.Passdown.Infrastructure.Retrieval.Browser;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Browser;

/// <summary>
/// Phase 7B's replacement for the removed Playwright-backed <c>IJobBrowserSessionFactory</c>
/// (docs/release-strategy.md "Playwright dependency decision"). No current source registration
/// sets <c>RequiresBrowserSession: true</c>, so <see cref="UnavailableJobBrowserSession.EnsureStartedAsync"/>
/// is never actually called in production — this only proves the contract behaves the way a
/// future caller would need if that ever changed: no I/O at creation, a loud, clear failure
/// rather than a silent no-op if a browser session is ever actually requested.
/// </summary>
public class UnavailableJobBrowserSessionTests
{
    [Fact]
    public void Create_DoesNotThrowAndDoesNoIo()
    {
        var factory = new UnavailableJobBrowserSessionFactory();

        var session = factory.Create();

        Assert.NotNull(session);
    }

    [Fact]
    public async Task EnsureStartedAsync_Throws_NotSupportedException()
    {
        var session = new UnavailableJobBrowserSession();

        await Assert.ThrowsAsync<NotSupportedException>(() => session.EnsureStartedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DisposeAsync_WhenNeverStarted_DoesNotThrow()
    {
        var session = new UnavailableJobBrowserSession();

        await session.DisposeAsync();
    }
}
