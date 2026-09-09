using System.Diagnostics;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Carowinds.Passdown.Ui.Tests.Startup;

/// <summary>
/// Phase 13A audit: a temporary, non-invasive profiling harness measuring wall-clock time for the
/// real startup components traced by the Phase 13A audit (see the scratch findings the audit task
/// produced) — <see cref="App.BuildServiceProvider"/>, a cold <see cref="SqliteConnectionFactory"/>
/// open, <see cref="MigrationRunner.Run"/> from an empty database, constructing the same five child
/// view models <see cref="MainViewModel"/> wraps, and <see cref="OperationsDashboardService.GetTodayAsync"/>'s
/// initial local load. Every measurement uses either the exact production type (never a fake, so the
/// numbers reflect real cost) or an isolated temp-file SQLite database created fresh per iteration and
/// deleted immediately after (never the real per-user <c>AppPaths</c> database, and never anything a
/// live app installation depends on). This adds no telemetry to the shipped app and changes no
/// startup/refresh/business-logic code — it only measures what already runs. Safe to delete once
/// Phase 13A's findings are acted on; harmless to keep (it is a fast, fully local, non-flaky test) if
/// a later phase wants to re-run it as a regression check.
///
/// Phase 15D: re-run as part of the lifecycle/startup-delay investigation (see
/// docs/phase13-hardening.md's Phase 15D section) — the composition-root/DB/view-model portion of
/// startup this test measures still sums to low-single-digit milliseconds, confirming the reported
/// packaged-EXE launch delay is not explained here. Also fixed a real staleness bug found during that
/// re-run: <see cref="AllMigrations"/> was a hand-duplicated list stuck at 10 migrations (missing
/// Phase 13D's <c>Migration0011CreateDeepRefreshStatusTable</c>), silently under-measuring a full
/// cold-start migration run for several phases; it now reads the real registration instead.
/// </summary>
public class StartupProfilingTests
{
    private readonly ITestOutputHelper _output;

    public StartupProfilingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Phase 15D: derived from the real composition root (Carowinds.Passdown.Desktop.App) rather than
    // a second hand-maintained list — the prior hardcoded copy here had silently drifted behind the
    // real registration (stuck at 10 migrations after Phase 13D's Migration0011 shipped), which
    // would have quietly under-measured a full-migration cold start. Registering a migration does no
    // I/O by itself, so resolving this from a throwaway BuildServiceProvider() call is safe and
    // matches CompositionRootTests' own "read the real registration, don't duplicate it" convention.
    private static IReadOnlyList<IMigration> AllMigrations
    {
        get
        {
            var provider = App.BuildServiceProvider();
            try
            {
                return provider.GetRequiredService<IReadOnlyList<IMigration>>();
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }
        }
    }

    private static string NewTempDb() => Path.Combine(Path.GetTempPath(), $"carowinds-passdown-13a-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Profile_StartupComponents()
    {
        const int Iterations = 5;
        var allMigrations = AllMigrations;
        var expectedFinalVersion = allMigrations.Max(m => m.Version);

        var diBuildMs = new List<double>();
        var sqliteOpenMs = new List<double>();
        var migrationRunMs = new List<double>();
        var viewModelConstructionMs = new List<double>();
        var dashboardLoadMs = new List<double>();

        for (var i = 0; i < Iterations; i++)
        {
            // 1) DI ServiceProvider build: the exact App.BuildServiceProvider() App.axaml.cs's
            // Initialize() calls — config load (appsettings.json), logging setup, and every
            // registration, ending in ServiceCollection.BuildServiceProvider(). Registering a
            // singleton does no I/O by itself (CompositionRootTests documents the same assumption),
            // so this is pure config/logging/registration cost with zero database touch.
            var diSw = Stopwatch.StartNew();
            var provider = App.BuildServiceProvider();
            diSw.Stop();
            diBuildMs.Add(diSw.Elapsed.TotalMilliseconds);
            (provider as IDisposable)?.Dispose();

            var dbPath = NewTempDb();
            try
            {
                // 2) Cold SQLite connection open against a brand-new temp file (never the real
                // per-user AppPaths database).
                var connectionFactory = new SqliteConnectionFactory(dbPath);
                var openSw = Stopwatch.StartNew();
                using (var connection = connectionFactory.OpenConnection())
                {
                    openSw.Stop();
                }
                sqliteOpenMs.Add(openSw.Elapsed.TotalMilliseconds);

                // 3) MigrationRunner.Run() applying every registered migration from empty.
                var migrationRunner = new MigrationRunner(connectionFactory, allMigrations, NullLogger<MigrationRunner>.Instance);
                var migrationSw = Stopwatch.StartNew();
                var finalVersion = migrationRunner.Run();
                migrationSw.Stop();
                Assert.Equal(expectedFinalVersion, finalVersion);
                migrationRunMs.Add(migrationSw.Elapsed.TotalMilliseconds);

                // 4) The real production service graph (same concrete types the composition root
                // wires — no fakes for any of these) against this same migrated-but-otherwise-empty
                // temp database, then construct the same five child view models MainViewModel wraps.
                IClock clock = new SystemClock();
                var drafts = new SqliteAssociateDraftRepository(connectionFactory);
                var datedResults = new SqliteDatedResultRepository(connectionFactory);
                var catalogProducts = new SqliteCatalogProductRepository(connectionFactory);
                var operationalNoteRepository = new SqliteOperationalNoteRepository(connectionFactory);
                var finalizedPassdowns = new SqliteFinalizedPassdownRepository(connectionFactory);

                var operationalNotesService = new OperationalNotesService(operationalNoteRepository, clock);
                var associateService = new AssociatePassdownService(
                    drafts, datedResults, catalogProducts, new UnreachableRefreshOrchestrator(), operationalNotesService, clock);
                var dashboardService = new OperationsDashboardService(associateService, finalizedPassdowns, operationalNotesService, clock);
                var calendarService = new OperationsCalendarService(associateService, drafts, datedResults, finalizedPassdowns, operationalNotesService, clock);
                var historyService = new PassdownHistoryService(finalizedPassdowns);
                var pdfRetrievalService = new FinalizedPassdownPdfRetrievalService(finalizedPassdowns);

                var vmSw = Stopwatch.StartNew();
                var dashboardVm = new OperationsDashboardViewModel(dashboardService);
                var calendarVm = new OperationsCalendarViewModel(calendarService, clock);
                var associateVm = new AssociateViewModel(associateService, clock);
                var notesVm = new OperationalNotesViewModel(operationalNotesService, clock);
                var historyVm = new PassdownHistoryViewModel(
                    historyService, pdfRetrievalService, pdfSavePicker: new FakePdfSaveFilePicker(), pdfLauncher: new FakeFinalizedPdfLauncher());
                var settingsVm = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker());
                var mainVm = new MainViewModel(dashboardVm, calendarVm, associateVm, notesVm, historyVm, settingsVm);
                vmSw.Stop();
                viewModelConstructionMs.Add(vmSw.Elapsed.TotalMilliseconds);
                Assert.NotNull(mainVm);

                // Let every child view model's own fire-and-forget constructor load finish before this
                // iteration's temp db is deleted below — constructor return time (measured above) is
                // deliberately not gated on this, since none of these loads are awaited by the real
                // MainViewModel constructor either.
                await Task.WhenAll(dashboardVm.PendingLoad, calendarVm.PendingLoad, associateVm.PendingLoad, notesVm.PendingLoad, historyVm.PendingLoad);

                // 5) Dashboard's initial local load, timed directly against
                // IOperationsDashboardService.GetTodayAsync — the same call
                // OperationsDashboardViewModel's constructor already fired-and-forgot above, called
                // again here (against the same still-empty db) purely to get an isolated measurement.
                var dashboardSw = Stopwatch.StartNew();
                var snapshot = await dashboardService.GetTodayAsync();
                dashboardSw.Stop();
                Assert.NotNull(snapshot);
                dashboardLoadMs.Add(dashboardSw.Elapsed.TotalMilliseconds);
            }
            finally
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
        }

        Report("DI ServiceProvider build (config load + logging setup + registrations)", diBuildMs);
        Report("SQLite cold connection open (fresh temp db)", sqliteOpenMs);
        Report($"MigrationRunner.Run ({allMigrations.Count} migrations, empty db)", migrationRunMs);
        Report("5 child view models + MainViewModel construction (fire-and-forget loads not awaited)", viewModelConstructionMs);
        Report("OperationsDashboardService.GetTodayAsync (empty db)", dashboardLoadMs);
    }

    private void Report(string label, List<double> samplesMs)
    {
        var ordered = samplesMs.OrderBy(v => v).ToList();
        var median = ordered[ordered.Count / 2];
        var samples = string.Join(", ", ordered.Select(v => v.ToString("F2")));
        _output.WriteLine($"{label}: median={median:F2}ms min={ordered[0]:F2}ms max={ordered[^1]:F2}ms samples=[{samples}]ms");
    }
}
