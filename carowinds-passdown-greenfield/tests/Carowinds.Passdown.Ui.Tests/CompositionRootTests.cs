using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Infrastructure.Storage;
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

    // Phase 6A Part L #19/#14: the normal launch path still resolves MigrationRunner/MainViewModel
    // from the same composition root a scheduled-refresh launch reuses, proving Phase 6A's headless
    // addition left normal startup behavior unchanged. Deliberately does not resolve MainWindow
    // itself here: constructing a Window requires running on the headless dispatcher's own thread
    // (see AssociateViewTests' remarks on Window vs. UserControl construction) — App.axaml.cs's
    // registration of MainWindow is unchanged by Phase 6A, and MainWindow's own construction is
    // exercised by the app's normal interactive startup, not by this offline unit test. Also
    // deliberately does not call MigrationRunner.Run() here: doing so would write to the real
    // per-user AppPaths database during an offline unit test — Phase 6A's own
    // migration-runs-before-refresh coverage uses an isolated temp database instead
    // (ScheduledRefreshHostTests).
    [Fact]
    public void BuildServiceProvider_NormalMode_ResolvesMigrationRunnerAndMainViewModel()
    {
        var provider = App.BuildServiceProvider();

        var migrationRunner = provider.GetRequiredService<MigrationRunner>();
        var mainViewModel = provider.GetRequiredService<MainViewModel>();

        Assert.NotNull(migrationRunner);
        Assert.NotNull(mainViewModel);
    }

    // Phase 9D: the History screen's Application/Desktop seams resolve from the same composition
    // root — proves IPassdownHistoryService/IFinalizedPdfLauncher/PassdownHistoryViewModel are wired,
    // without doing any I/O (registering a singleton/transient does none by itself).
    [Fact]
    public void BuildServiceProvider_ResolvesPassdownHistoryServices()
    {
        var provider = App.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IPassdownHistoryService>());
        Assert.NotNull(provider.GetRequiredService<IFinalizedPdfLauncher>());
        Assert.NotNull(provider.GetRequiredService<PassdownHistoryViewModel>());
    }
}
