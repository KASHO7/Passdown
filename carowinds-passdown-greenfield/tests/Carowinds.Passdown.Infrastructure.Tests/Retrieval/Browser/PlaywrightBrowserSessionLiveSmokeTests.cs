using Carowinds.Passdown.Infrastructure.Retrieval.Browser;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Browser;

/// <summary>
/// Opt-in real-browser lifecycle check: launches an actual headless Chromium instance, opens a
/// page from the shared session, and disposes it. Requires <c>playwright install chromium</c> to
/// have been run first, so — like other <c>LiveSmoke</c> tests — it is excluded from a normal
/// <c>dotnet test</c> run (see the <c>build-and-test</c> skill).
/// </summary>
[Trait("Category", TestCategories.LiveSmoke)]
public class PlaywrightBrowserSessionLiveSmokeTests
{
    [Fact]
    public async Task StartAsync_OpensAPage_ThenDisposesCleanly()
    {
        var factory = new PlaywrightBrowserSessionFactory(new PlaywrightBrowserSessionOptions { Headless = true });

        await using var session = await factory.StartAsync(CancellationToken.None);
        var page = await session.NewPageAsync(CancellationToken.None);
        await page.GotoAsync("about:blank");

        Assert.NotNull(page);
    }
}
