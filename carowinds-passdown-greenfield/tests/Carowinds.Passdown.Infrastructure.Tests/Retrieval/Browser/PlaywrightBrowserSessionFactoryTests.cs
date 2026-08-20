using Carowinds.Passdown.Infrastructure.Retrieval.Browser;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Browser;

/// <summary>
/// Covers only the pre-launch cancellation guard, which runs before Playwright's driver process
/// is ever started and so needs neither installed browsers nor network access. Full
/// launch/page/dispose lifecycle coverage against a real Chromium instance lives in
/// <see cref="PlaywrightBrowserSessionLiveSmokeTests"/>, gated behind
/// <c>Category=LiveSmoke</c> since it requires <c>playwright install</c> to have run first.
/// </summary>
public class PlaywrightBrowserSessionFactoryTests
{
    [Fact]
    public async Task StartAsync_WithAlreadyCancelledToken_ThrowsWithoutLaunchingABrowser()
    {
        var factory = new PlaywrightBrowserSessionFactory(new PlaywrightBrowserSessionOptions());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => factory.StartAsync(cts.Token));
    }
}
