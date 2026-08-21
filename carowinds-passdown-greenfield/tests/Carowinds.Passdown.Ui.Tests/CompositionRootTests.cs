using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop;
using Microsoft.Extensions.DependencyInjection;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Verifies the Desktop composition root (Phase 3G Part K) can actually resolve
/// <see cref="IRefreshOrchestrator"/> and everything it depends on. Resolving every singleton
/// registered in <c>App.BuildServiceProvider</c> does no I/O by itself (no repository opens a
/// SQLite connection, no adapter makes an HTTP call, no browser factory launches Playwright, until
/// something actually calls one of them) — so this stays a fast, offline test rather than a live
/// smoke test.
/// </summary>
public class CompositionRootTests
{
    [Fact]
    public void BuildServiceProvider_ResolvesRefreshOrchestrator()
    {
        var provider = App.BuildServiceProvider();

        var orchestrator = provider.GetRequiredService<IRefreshOrchestrator>();

        Assert.NotNull(orchestrator);
    }

    [Fact]
    public void BuildServiceProvider_RegistersEightSourceRegistrations()
    {
        var provider = App.BuildServiceProvider();

        var registrations = provider.GetRequiredService<IReadOnlyList<SourceRegistration>>();

        Assert.Equal(8, registrations.Count);
        Assert.Equal(8, registrations.Select(r => r.SourceId).Distinct().Count());
    }
}
