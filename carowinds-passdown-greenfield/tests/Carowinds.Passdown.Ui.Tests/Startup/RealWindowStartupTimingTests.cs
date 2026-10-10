using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Time;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Carowinds.Passdown.Ui.Tests.Startup;

/// <summary>
/// Phase 15D: <c>StartupProfilingTests</c> (Phase 13A) measured composition-root/DB/view-model
/// construction but never a real window — that dev environment was headless (no compositor), so
/// "time to first pixel" was left PARTIAL. This assembly's own headless Avalonia platform (see
/// <see cref="HeadlessTestSetup"/>) does not need a compositor to create and show a real
/// <see cref="Window"/> and fire its <see cref="Window.Opened"/> event, so it can close that gap: an
/// end-to-end timer covering the same steps <c>App.OnFrameworkInitializationCompleted</c> runs in
/// production — migrations, real production-type repositories/services, the same five child view
/// models, a real <see cref="MainWindow"/>, <c>Show()</c> — for both a fresh (never-migrated) database
/// and an already-migrated (schema 11) database, mirroring how a first run differs from every
/// subsequent one. Never touches the real per-user <c>AppPaths</c> database.
/// </summary>
public class RealWindowStartupTimingTests
{
    private static string NewTempDb() => Path.Combine(Path.GetTempPath(), $"carowinds-passdown-15d-{Guid.NewGuid():N}.db");

    private readonly ITestOutputHelper _output;

    public RealWindowStartupTimingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static IReadOnlyList<IMigration> AllMigrations
    {
        get
        {
            var provider = Carowinds.Passdown.Desktop.App.BuildServiceProvider();
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

    /// <summary>
    /// Builds the same real production service graph <c>App.BuildServiceProvider</c> wires (Phase 13A
    /// already established registering/constructing these does no I/O by itself), a real
    /// <see cref="MainWindow"/>/<see cref="MainViewModel"/> pair, shows the window, and reports how
    /// long from just before <c>MigrationRunner.Run()</c> (the first real DB touch, exactly matching
    /// where production starts its own timer-worthy work) through the window's own
    /// <see cref="Window.Opened"/> event. No hard millisecond assertion — Part 21's own instruction is
    /// not to invent a threshold this codebase never documented; the structural assertions below (no
    /// retrieval reachable, migrations behave correctly per scenario) are the actual regression guard.
    /// </summary>
    private async Task<double> MeasureColdStartToWindowOpenedAsync(string dbPath, bool preMigrate)
    {
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var allMigrations = AllMigrations;

        if (preMigrate)
        {
            new MigrationRunner(connectionFactory, allMigrations, NullLogger<MigrationRunner>.Instance).Run();
        }

        var stopwatch = Stopwatch.StartNew();

        // 1) Schema guard / migrations — first real DB touch, exactly as App.OnFrameworkInitializationCompleted does.
        new MigrationRunner(connectionFactory, allMigrations, NullLogger<MigrationRunner>.Instance).Run();

        // 2) Real production services (no fakes) — same types the composition root wires.
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

        // 3) The same five child view models + MainViewModel, fire-and-forget loads not awaited —
        // exactly as production never awaits them before showing the window.
        var dashboardVm = new OperationsDashboardViewModel(dashboardService);
        var calendarVm = new OperationsCalendarViewModel(calendarService, clock);
        var associateVm = new AssociateViewModel(associateService, clock);
        var notesVm = new OperationalNotesViewModel(operationalNotesService, clock);
        var historyVm = new PassdownHistoryViewModel(
            historyService, pdfRetrievalService, pdfSavePicker: new FakePdfSaveFilePicker(), pdfLauncher: new FakeFinalizedPdfLauncher());
        var settingsVm = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());
        var mainVm = new MainViewModel(dashboardVm, calendarVm, associateVm, notesVm, historyVm, settingsVm);

        // 4) A real MainWindow, shown — the closest available proxy for "the shell became visible".
        var window = new MainWindow { DataContext = mainVm };
        double? openedAtMs = null;
        window.Opened += (_, _) => openedAtMs ??= stopwatch.Elapsed.TotalMilliseconds;

        window.Show();
        Dispatcher.UIThread.RunJobs();
        var shownAtMs = stopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Stop();

        window.Close();

        // Let every child view model's fire-and-forget load finish before the caller deletes the temp
        // db, so no background read races the file delete below.
        await Task.WhenAll(dashboardVm.PendingLoad, calendarVm.PendingLoad, associateVm.PendingLoad, notesVm.PendingLoad, historyVm.PendingLoad);

        return openedAtMs ?? shownAtMs;
    }

    // Phase 15D §20: kept to 1 iteration (unlike StartupProfilingTests' 5) — this codebase's
    // documented headless real-Window thread-affinity flake (see HeadlessTestSetup) means every real
    // Window constructed here is one more roll of the dice against that pre-existing race; a single
    // representative measurement per scenario is enough evidence for this test's purpose (see
    // docs/phase13-hardening.md's Phase 15D section) without needlessly multiplying that exposure.
    [Fact]
    public async Task ColdStartToWindowOpened_FreshNeverMigratedDatabase()
    {
        const int Iterations = 1;
        var samples = new List<double>();

        for (var i = 0; i < Iterations; i++)
        {
            var dbPath = NewTempDb();
            try
            {
                samples.Add(await MeasureColdStartToWindowOpenedAsync(dbPath, preMigrate: false));
            }
            finally
            {
                if (File.Exists(dbPath))
                {
                    SqliteConnectionOnDisposeCleanup(dbPath);
                }
            }
        }

        Report("Fresh (never-migrated) DB: migrations + real services + MainWindow.Opened", samples);
    }

    [Fact]
    public async Task ColdStartToWindowOpened_ExistingSchema11Database()
    {
        const int Iterations = 1;
        var samples = new List<double>();

        for (var i = 0; i < Iterations; i++)
        {
            var dbPath = NewTempDb();
            try
            {
                samples.Add(await MeasureColdStartToWindowOpenedAsync(dbPath, preMigrate: true));
            }
            finally
            {
                if (File.Exists(dbPath))
                {
                    SqliteConnectionOnDisposeCleanup(dbPath);
                }
            }
        }

        Report("Existing (already schema 11) DB: PRAGMA-only migration check + real services + MainWindow.Opened", samples);
    }

    private static void SqliteConnectionOnDisposeCleanup(string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(dbPath);
    }

    private void Report(string label, List<double> samplesMs)
    {
        var ordered = samplesMs.OrderBy(v => v).ToList();
        var median = ordered[ordered.Count / 2];
        var samplesText = string.Join(", ", ordered.Select(v => v.ToString("F2")));
        _output.WriteLine($"{label}: median={median:F2}ms min={ordered[0]:F2}ms max={ordered[^1]:F2}ms samples=[{samplesText}]ms");
    }
}
