using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
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
    public void BuildServiceProvider_RegistersTenSourceRegistrations()
    {
        var provider = App.BuildServiceProvider();

        var registrations = provider.GetRequiredService<IReadOnlyList<SourceRegistration>>();

        // Phase 13C added 2: dated-bring-a-friend-duo.official and military-first-responder.official.
        Assert.Equal(10, registrations.Count);
        Assert.Equal(10, registrations.Select(r => r.SourceId).Distinct().Count());
    }

    /// <summary>
    /// Phase 13B Part F/Y source-coverage matrix: fails the build if a currently-supported source
    /// family is ever accidentally dropped from the registration list — the exact list
    /// <see cref="RefreshOrchestrator.RunUserForcedRefreshAsync"/> force-refreshes end to end,
    /// since it iterates every registration with no category filtering.
    /// </summary>
    [Fact]
    public void BuildServiceProvider_SourceRegistrations_CoverEveryExpectedFamily()
    {
        var provider = App.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IReadOnlyList<SourceRegistration>>();

        var datedCategories = registrations.Where(r => r.Scope == SourceScope.Dated).Select(r => r.Category).ToHashSet();
        var catalogCategories = registrations.Where(r => r.Scope == SourceScope.Catalog).Select(r => r.Category).ToList();

        Assert.Equal(
            new HashSet<SourceCategory>
            {
                SourceCategory.ParkHours,
                SourceCategory.DatedAdmissionTickets,
                SourceCategory.DatedFastLane,
                SourceCategory.HauntedAttractions,
                SourceCategory.BringAFriendDuo,
            },
            datedCategories);

        // AddOns appears twice — season-pass add-ons and general add-ons are independent source ids
        // sharing one category/freshness policy (spec §6.2) — both registrations must still exist.
        Assert.Equal(
            new[]
            {
                SourceCategory.Promotions, SourceCategory.SeasonPasses, SourceCategory.AddOns, SourceCategory.AddOns,
                SourceCategory.MilitaryFirstResponder,
            }.OrderBy(c => c),
            catalogCategories.OrderBy(c => c));
    }

    /// <summary>Phase 13B Part K: the overlap-lock factory <c>AssociatePassdownService</c> uses to guard <c>RefreshMode.UserForced</c> resolves from the same composition root.</summary>
    [Fact]
    public void BuildServiceProvider_ResolvesScheduledRefreshLockFactory()
    {
        var provider = App.BuildServiceProvider();

        var factory = provider.GetRequiredService<Func<IScheduledRefreshLock>>();

        Assert.NotNull(factory);
        using var scheduledLock = factory();
        Assert.NotNull(scheduledLock);
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

    // Phase 10A: the Dashboard's Application/Desktop seams resolve from the same composition root —
    // proves IOperationsDashboardService/OperationsDashboardViewModel are wired, and that
    // MainViewModel now resolves with its third (Dashboard) constructor argument satisfied.
    [Fact]
    public void BuildServiceProvider_ResolvesOperationsDashboardServices()
    {
        var provider = App.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOperationsDashboardService>());
        Assert.NotNull(provider.GetRequiredService<OperationsDashboardViewModel>());
        Assert.NotNull(provider.GetRequiredService<MainViewModel>());
    }

    // Phase 11A: the persistent Operational Notes foundation's Application-layer seams resolve from
    // the same composition root.
    [Fact]
    public void BuildServiceProvider_ResolvesOperationalNotesServices()
    {
        var provider = App.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOperationalNoteRepository>());
        Assert.NotNull(provider.GetRequiredService<IOperationalNotesService>());
    }

    // Phase 11B: the Operational Notes Manager screen's Desktop-layer seam resolves from the same
    // composition root, and MainViewModel now resolves with its fourth (Notes) constructor argument
    // satisfied.
    [Fact]
    public void BuildServiceProvider_ResolvesOperationalNotesViewModel()
    {
        var provider = App.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<OperationalNotesViewModel>());
        Assert.NotNull(provider.GetRequiredService<MainViewModel>());
    }

    // Phase 12A: the Operations Calendar foundation's Application/Desktop seams resolve from the same
    // composition root, and MainViewModel now resolves with its fifth (Calendar) constructor argument
    // satisfied.
    [Fact]
    public void BuildServiceProvider_ResolvesOperationsCalendarServices()
    {
        var provider = App.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOperationsCalendarService>());
        Assert.NotNull(provider.GetRequiredService<OperationsCalendarViewModel>());
        Assert.NotNull(provider.GetRequiredService<MainViewModel>());
    }
}
