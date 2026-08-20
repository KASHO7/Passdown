using Microsoft.Playwright;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Browser;

/// <summary>
/// Real <see cref="IBrowserSessionFactory"/>: launches one headless Chromium browser and one
/// shared context per call (spec §6.4). Blocks image/media/font requests at the context level
/// so every page opened from the resulting session gets that behavior for free, without any
/// adapter having to remember to configure it per-source. Analytics-request blocking is left to
/// a later phase — it needs a specific domain blocklist this phase has no observed evidence for
/// (CLAUDE.md "Never invent data" applies just as much to guessed endpoints as guessed prices).
/// Checks cancellation before each launch step so a cancelled refresh job never spawns a browser
/// process it would only have to immediately tear down.
/// </summary>
public sealed class PlaywrightBrowserSessionFactory(PlaywrightBrowserSessionOptions options) : IBrowserSessionFactory
{
    private static readonly HashSet<string> BlockedResourceTypes = ["image", "media", "font"];

    public async Task<IBrowserSession> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var browser = await playwright.Chromium
                .LaunchAsync(new BrowserTypeLaunchOptions { Headless = options.Headless })
                .ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var context = await browser.NewContextAsync().ConfigureAwait(false);
                await context.RouteAsync("**/*", route =>
                {
                    if (BlockedResourceTypes.Contains(route.Request.ResourceType))
                    {
                        route.AbortAsync();
                    }
                    else
                    {
                        route.ContinueAsync();
                    }
                }).ConfigureAwait(false);

                return new PlaywrightBrowserSession(playwright, browser, context);
            }
            catch
            {
                await browser.CloseAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }
}
