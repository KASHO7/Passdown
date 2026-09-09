using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Retrieval.Browser;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.Startup;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure;
using Carowinds.Passdown.Infrastructure.Concurrency;
using Carowinds.Passdown.Infrastructure.Diagnostics;
using Carowinds.Passdown.Infrastructure.Logging;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Carowinds.Passdown.Infrastructure.Retrieval.Browser;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Carowinds.Passdown.Ui.Tests")]

namespace Carowinds.Passdown.Desktop;

public partial class App : global::Avalonia.Application
{
    private IServiceProvider? _services;

    /// <summary>
    /// Phase 15D: process creation time, used as the fixed zero-point for the startup checkpoints
    /// logged below. Captured once, as early as this type is first touched (<see cref="Initialize"/>
    /// is the first thing <see cref="Program.Main"/>'s Avalonia branch calls), so the resulting
    /// deltas include CLR/self-contained-runtime startup cost that happens before <c>Main</c>'s own
    /// first line runs — the one phase of startup that cannot be measured any other way from managed
    /// code, and the one the Phase 15D investigation needed evidence for (see
    /// docs/phase13-hardening.md's Phase 15D section: "packaged EXE vs dotnet run").
    /// </summary>
    private static readonly DateTime ProcessStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    private double _xamlLoadedMs;
    private double _serviceProviderBuiltMs;

    private static double ElapsedSinceProcessStartMs() => (DateTime.UtcNow - ProcessStartUtc).TotalMilliseconds;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _xamlLoadedMs = ElapsedSinceProcessStartMs();

        _services = BuildServiceProvider();
        _serviceProviderBuiltMs = ElapsedSinceProcessStartMs();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = _services ?? throw new InvalidOperationException(
                $"{nameof(_services)} was not initialized; {nameof(Initialize)} must run first.");

            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

            // Storage must be ready (spec §17 Phase 1: "establish ... storage
            // migrations before building the production UI") before any
            // view model touches the database.
            services.GetRequiredService<MigrationRunner>().Run();
            var migrationsRunMs = ElapsedSinceProcessStartMs();

            var mainWindow = services.GetRequiredService<MainWindow>();
            var mainViewModel = services.GetRequiredService<MainViewModel>();
            mainWindow.DataContext = mainViewModel;
            var mainViewModelResolvedMs = ElapsedSinceProcessStartMs();

            var syncCoordinator = services.GetRequiredService<IPersonalSyncCoordinator>();
            AttachShutdownFlush(desktop, mainViewModel, syncCoordinator, logger);

            // Phase 15D: the closest available cross-platform proxy for "the shell became visible" —
            // Window.Opened fires once the platform has actually shown the window, well after
            // desktop.MainWindow is merely assigned below. Debug-level only (see the class doc above)
            // so this never appears in a normal Information-level launch; a developer/tester
            // investigating a slow launch can temporarily raise Logging:LogLevel:Default to see it.
            mainWindow.Opened += (_, _) => logger.LogDebug(
                "Startup: MainWindow.Opened fired at {ElapsedMs:F1}ms since process start", ElapsedSinceProcessStartMs());

            desktop.MainWindow = mainWindow;
            var mainWindowAssignedMs = ElapsedSinceProcessStartMs();

            logger.LogDebug(
                "Startup checkpoints (ms since process start): xamlLoaded={XamlLoadedMs:F1} " +
                "serviceProviderBuilt={ServiceProviderBuiltMs:F1} migrationsRun={MigrationsRunMs:F1} " +
                "mainViewModelResolved={MainViewModelResolvedMs:F1} mainWindowAssigned={MainWindowAssignedMs:F1}",
                _xamlLoadedMs, _serviceProviderBuiltMs, migrationsRunMs, mainViewModelResolvedMs, mainWindowAssignedMs);

            // Phase 16F §3: started after the window is already assigned/visible, before the catch-up
            // refresh below (whose own finalization step may call IPersonalSyncCoordinator.RequestSync)
            // — Start() itself returns immediately (the startup sync pass runs in the background), so
            // this can never delay the first usable window the way a blocking bootstrap would.
            syncCoordinator.Start();

            StartCatchUpRefresh(services);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Phase 15D: a committed edit made shortly before the window closes must not be silently lost
    /// waiting on the Associate screen's 800ms autosave debounce (the audit found no flush-on-shutdown
    /// path existed). The first <c>ShutdownRequested</c> is cancelled so the pending/in-flight save
    /// can drain via <see cref="FlushPendingAutosaveBeforeShutdownAsync"/> — the same
    /// <see cref="AssociateViewModel.FlushPendingAutosaveAsync"/> a date switch or Check Online
    /// already uses — then <see cref="IClassicDesktopStyleApplicationLifetime.Shutdown"/> is called
    /// again to actually close; the <c>flushed</c> guard prevents that second request from looping
    /// back into this handler.
    /// </summary>
    private static void AttachShutdownFlush(IClassicDesktopStyleApplicationLifetime desktop, MainViewModel mainViewModel, IPersonalSyncCoordinator syncCoordinator, ILogger logger)
    {
        var flushed = false;

        desktop.ShutdownRequested += (_, e) =>
        {
            if (flushed)
            {
                return;
            }

            e.Cancel = true;
            _ = FlushThenShutdownAsync();
        };

        async Task FlushThenShutdownAsync()
        {
            await FlushPendingAutosaveBeforeShutdownAsync(mainViewModel, logger).ConfigureAwait(true);

            // Phase 16F §23: stops the periodic loop and any pending debounce/retry timer only — it
            // deliberately does not wait for a sync pass that happens to be mid-flight (file I/O uses
            // atomic temp-then-rename, so an interrupted pass never leaves a half-written file visible
            // to anything else; pending local changes simply sync on next startup, §24). No bounded
            // final sync is added here — local autosave flush above is the only thing shutdown actually
            // waits on, matching the spec's "do not risk shutdown hangs waiting on OneDrive."
            try
            {
                await syncCoordinator.StopAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to stop the Personal Sync coordinator during shutdown; closing anyway.");
            }

            flushed = true;
            desktop.Shutdown();
        }
    }

    /// <summary>
    /// Same busy-timeout budget <see cref="SqliteConnectionFactory"/> already gives a single stuck
    /// write (see its own doc comment) — reused here as the ceiling for the whole shutdown flush, not
    /// because the two are the same operation, but because it is already the codebase's established
    /// answer to "how long is a database allowed to look stuck before this app stops waiting on it."
    /// </summary>
    private static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The actual drain-before-exit work, split out from <see cref="AttachShutdownFlush"/> so it can
    /// be exercised directly against a real <see cref="AssociateViewModel"/> without needing a real
    /// Avalonia desktop lifetime (see <c>AppShutdownFlushTests</c>). Bounded by
    /// <see cref="ShutdownFlushTimeout"/>: <see cref="AssociateViewModel.FlushPendingAutosaveAsync"/>'s
    /// retry loop only stops once a save actually succeeds — a persistently unavailable database
    /// (§13's "DB busy/locked" scenario, hit at exactly the wrong moment) would otherwise retry with
    /// no backoff forever, making the app impossible to close. Timing out here still loses that one
    /// last unsaved edit if the database genuinely never recovers, but an app the user cannot close no
    /// matter how many times they click X is a strictly worse outcome for a case this rare. A flush
    /// failure that throws directly (rather than timing out) is logged and swallowed the same way.
    /// <paramref name="timeout"/> defaults to <see cref="ShutdownFlushTimeout"/>; the override exists
    /// so a test can prove the timeout path itself without a real 5-second wait.
    /// </summary>
    internal static async Task FlushPendingAutosaveBeforeShutdownAsync(MainViewModel mainViewModel, ILogger logger, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? ShutdownFlushTimeout;

        try
        {
            var flush = mainViewModel.Associate.FlushPendingAutosaveAsync();
            var completed = await Task.WhenAny(flush, Task.Delay(effectiveTimeout)).ConfigureAwait(true);

            if (completed != flush)
            {
                logger.LogWarning("Flush-on-shutdown timed out after {TimeoutSeconds}s; closing anyway rather than blocking shutdown indefinitely.", effectiveTimeout.TotalSeconds);
                return;
            }

            await flush.ConfigureAwait(true); // Observe a faulted flush task so it isn't reported as unobserved.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Flush-on-shutdown failed; closing anyway rather than blocking shutdown indefinitely.");
        }
    }

    /// <summary>
    /// Phase 6B Part C: fires the missed-run catch-up (spec §6.6) as a detached background task —
    /// deliberately not awaited, so a slow or failing refresh can never delay or block the window
    /// that was just assigned above. See <see cref="StartupCatchUpRefresh"/> for the guard/failure
    /// semantics.
    /// </summary>
    private static void StartCatchUpRefresh(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StartupCatchUpRefresh");
        var scheduledLock = new FileScheduledRefreshLock(FileScheduledRefreshLock.DefaultPath);
        _ = StartupCatchUpRefresh.RunAsync(services, scheduledLock, logger);
    }

    /// <summary>
    /// Phase 15F: appsettings.json is an embedded resource (Carowinds.Passdown.Desktop.csproj),
    /// not a physical file read via AppContext.BaseDirectory — see the csproj comment for why a
    /// single-file publish makes the physical-file approach unreliable. Caller disposes the stream
    /// via <see cref="ConfigurationBuilder.Build"/>'s underlying JSON provider.
    /// </summary>
    private static Stream OpenEmbeddedAppSettings()
    {
        var assembly = typeof(App).Assembly;
        const string resourceName = "Carowinds.Passdown.Desktop.appsettings.json";
        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'.");
    }

    internal static IServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(OpenEmbeddedAppSettings())
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder
            .AddConfiguration(configuration.GetSection("Logging"))
            .AddConsole());

        services.Configure<StorageOptions>(configuration.GetSection("Storage"));

        services.AddSingleton(provider =>
        {
            var storageOptions = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            var databasePath = Path.Combine(AppPaths.LocalApplicationDataDirectory, storageOptions.DatabaseFileName);
            return new SqliteConnectionFactory(databasePath);
        });

        services.AddSingleton<IReadOnlyList<IMigration>>([
            new Migration0001CreateSchemaVersionTable(),
            new Migration0002CreateSourceStateTable(),
            new Migration0003CreateDatedResultsTable(),
            new Migration0004CreateCatalogProductsTable(),
            new Migration0005CreateProductHistoryTable(),
            new Migration0006CreateAssociateDraftsTable(),
            new Migration0007CreateManagerDraftsTable(),
            new Migration0008CreateFinalizedPassdownsTable(),
            new Migration0009AddFinalizedPassdownPdfColumns(),
            new Migration0010CreateOperationalNotesTable(),
            new Migration0011CreateDeepRefreshStatusTable(),
            new Migration0012CreateSyncEntityStateTable(),
            new Migration0013CreateSyncConflictsTable(),
        ]);
        services.AddSingleton<MigrationRunner>();

        AddRefreshOrchestration(services, configuration);

        // Phase 4B: the Associate Passdown screen (Part J). Reuses the same
        // IAssociateDraftRepository/IDatedResultRepository/ICatalogProductRepository/
        // IRefreshOrchestrator/IClock singletons AddRefreshOrchestration already registered above —
        // nothing here is a new storage or retrieval path.
        services.AddSingleton<IAssociateDraftRepository, SqliteAssociateDraftRepository>();

        // Phase 13B Part K/L: AssociatePassdownService.CheckAssociateDateOnlineAsync (the sole
        // RefreshMode.UserForced entry point, shared by Dashboard and Associate's own Check Online
        // actions) uses this factory to guard against running concurrently with a scheduled refresh
        // or startup catch-up already holding the same file lock. A fresh FileScheduledRefreshLock
        // instance per attempt is deliberate (mirrors ScheduledRefreshHost/StartupCatchUpRefresh's own
        // pattern) — OS-level exclusive file access is what actually enforces the single-holder
        // guarantee, not shared C# instance state.
        services.AddSingleton<Func<IScheduledRefreshLock>>(_ => () => new FileScheduledRefreshLock(FileScheduledRefreshLock.DefaultPath));
        services.AddSingleton<IAssociatePassdownService, AssociatePassdownService>();

        // Phase 4F Part I: the Associate PDF export pipeline (Documents project) plus the native
        // save-file picker seam (Desktop-only integration; see IPdfSaveFilePicker).
        services.AddSingleton<IAssociatePassdownPdfGenerator, AssociatePassdownPdfGenerator>();
        services.AddSingleton<IAssociatePassdownPdfExportService, AssociatePassdownPdfExportService>();
        services.AddSingleton<IPdfSaveFilePicker, AvaloniaPdfSaveFilePicker>();

        // Phase 9A: the immutable finalized-passdown foundation.
        services.AddSingleton<IFinalizedPassdownRepository, SqliteFinalizedPassdownRepository>();
        services.AddSingleton<IFinalizedPassdownSnapshotBuilder, FinalizedPassdownSnapshotBuilder>();
        services.AddSingleton<IFinalizedPassdownFinalizationService, FinalizedPassdownFinalizationService>();

        // Phase 9C Part V: the smallest seam a future History UI needs to retrieve an already-frozen
        // PDF. Reads IFinalizedPassdownRepository only — never invokes the PDF generator. Not wired
        // to any UI yet.
        services.AddSingleton<IFinalizedPassdownPdfRetrievalService, FinalizedPassdownPdfRetrievalService>();

        // Phase 9B: the 6:00 AM automatic finalization coordinator, invoked by ScheduledRefreshHost
        // and StartupCatchUpRefresh before either ever runs a source refresh.
        services.AddSingleton<IScheduledPassdownFinalizer, ScheduledPassdownFinalizer>();

        // Phase 9D: the read-only Passdown History screen. IPassdownHistoryService reads
        // IFinalizedPassdownRepository/FinalizedPassdownSnapshotSerializer only — it never generates a
        // PDF or touches the current Associate draft/catalog. IFinalizedPdfLauncher is the "Open Final
        // PDF" seam; Save PDF Copy reuses the already-registered IPdfSaveFilePicker.
        services.AddSingleton<IPassdownHistoryService, PassdownHistoryService>();
        services.AddSingleton<IFinalizedPdfLauncher, TempFileFinalizedPdfLauncher>();
        services.AddTransient<PassdownHistoryViewModel>();

        // Phase 10A: the Operations Dashboard foundation. IOperationsDashboardService reads
        // IAssociatePassdownService/IFinalizedPassdownRepository only (both already registered
        // above) for GetTodayAsync/LoadAsync — those never perform HTTP/Playwright retrieval and
        // never load a finalized PDF blob. Phase 13D Part O adds the manual "Run Deep Refresh"
        // action, which does need IRefreshOrchestrator/IDeepRefreshStatusRepository directly, plus
        // the same shared overlap-lock factory Check Online uses (Part AQ — never a second guard).
        services.AddSingleton<IOperationsDashboardService, OperationsDashboardService>();
        services.AddTransient<OperationsDashboardViewModel>();

        // Phase 12A: the Operations Calendar foundation — a local-only planning/routing surface.
        // IOperationsCalendarService depends only on already-registered local abstractions
        // (IAssociatePassdownService/IAssociateDraftRepository/IFinalizedPassdownRepository/
        // IOperationalNotesService/IClock); no HTTP/Playwright/Task Scheduler dependency exists.
        services.AddSingleton<IOperationsCalendarService, OperationsCalendarService>();
        services.AddTransient<OperationsCalendarViewModel>();

        // Phase 11A: the persistent Operational Notes domain/persistence/service foundation.
        // IOperationalNotesService depends on IOperationalNoteRepository/IClock only — nothing here
        // writes into an Associate draft. A relevant persistent note is never automatically copied
        // into a daily passdown (Phase 11D is the explicit, user-selected opt-in that will eventually
        // do that).
        services.AddSingleton<IOperationalNoteRepository, SqliteOperationalNoteRepository>();
        services.AddSingleton<IOperationalNotesService, OperationalNotesService>();

        // Phase 11B: the Operational Notes Manager screen. OperationalNotesViewModel depends only on
        // IOperationalNotesService/IClock (both already registered above) — the Notes Manager UI, not
        // Dashboard integration (11C) or Associate-passdown inclusion (11D).
        services.AddTransient<OperationalNotesViewModel>();

        // Phase 7F Part F/G: the sanitized diagnostic export pipeline. Reuses the already-registered
        // SqliteConnectionFactory/ISourceStateRepository/IReadOnlyList<SourceRegistration>/IClock —
        // no second storage or retrieval path. FileLoggerOptions is registered with its own
        // defaults (never overridden here) so the log excerpt reader looks at the same
        // %LOCALAPPDATA%\Carowinds Passdown\logs directory the scheduled-refresh host writes to
        // (see Program.BuildScheduledRefreshServiceProvider).
        services.AddSingleton<FileLoggerOptions>();
        services.AddSingleton<IDatabaseDiagnosticsProvider, SqliteDatabaseDiagnosticsProvider>();
        services.AddSingleton<IScheduledRefreshLogProvider, FileScheduledRefreshLogProvider>();
        services.AddSingleton<IScheduledTaskStatusProvider, ScheduledTaskStatusProvider>();
        services.AddSingleton<IDiagnosticReportBuilder, DiagnosticReportBuilder>();
        services.AddSingleton<IDiagnosticExportService, DiagnosticExportService>();
        services.AddSingleton<IDiagnosticExportFilePicker, AvaloniaDiagnosticExportFilePicker>();

        services.AddTransient<AssociateViewModel>();

        // Phase 16B: the local Personal Sync foundation — device ID, local sync configuration,
        // OneDrive suggestion, folder validation, and the manifest bootstrap. No entity (draft/note/
        // finalized passdown) is exported or imported by anything registered here; that begins in
        // 16C. Every piece is a flat local file under AppPaths.LocalApplicationDataDirectory, not a
        // SQLite table (CLAUDE.md "prefer no SQLite schema change").
        services.AddSingleton<IDeviceIdProvider>(_ => new FileDeviceIdProvider(FileDeviceIdProvider.DefaultPath));
        services.AddSingleton<ISyncConfigurationStore>(_ => new FileSyncConfigurationStore(FileSyncConfigurationStore.DefaultPath));
        services.AddSingleton<IOneDriveLocationSuggestionProvider, EnvironmentOneDriveLocationSuggestionProvider>();
        services.AddSingleton<ISyncFolderValidator, SyncFolderValidator>();
        services.AddSingleton<IPersonalSyncSettingsService, PersonalSyncSettingsService>();
        services.AddSingleton<ISyncFolderPicker, AvaloniaSyncFolderPicker>();

        // Phase 16C: the first real record-level sync — Associate drafts and Operational Notes.
        // ISyncEntityStateRepository/ISyncChangeTracker are resolved automatically into
        // AssociatePassdownService/OperationalNotesService's optional constructor parameters above;
        // nothing else needs to change at those registrations.
        services.AddSingleton<ISyncEntityStateRepository, SqliteSyncEntityStateRepository>();

        // Phase 16F §4/§8: every local draft/note save already calls ISyncChangeTracker once it saves
        // (Phase 16C §7) — AutomaticSyncChangeTracker wraps the plain SyncChangeTracker so that same
        // call also requests a debounced automatic sync pass, with no change to AssociatePassdownService/
        // OperationalNotesService or any ViewModel. SyncChangeTracker itself is kept resolvable under its
        // own concrete type for the wrapper to depend on, matching the decorator pattern used nowhere
        // else in this composition root only because nothing else here has needed one yet.
        services.AddSingleton<SyncChangeTracker>();
        services.AddSingleton<ISyncChangeTracker>(provider => new AutomaticSyncChangeTracker(
            provider.GetRequiredService<SyncChangeTracker>(),
            provider.GetRequiredService<IPersonalSyncCoordinator>()));
        // Phase 16E: finalized History/PDF sync. PersonalSyncService's constructor now also takes
        // IFinalizedPassdownRepository — already registered above (Phase 9A) — resolved automatically;
        // no separate finalization-to-sync hook was added (spec §22: export dirtiness is decided by
        // content-hash comparison during enumeration, so a never-synced finalized passdown is picked up
        // on the very next "Sync Now" without any change to FinalizedPassdownFinalizationService).
        services.AddSingleton<IPersonalSyncService, PersonalSyncService>();

        // Phase 16D: explicit conflict resolution and deletion/tombstone propagation. ISyncConflictRepository
        // persists the full conflicting remote envelope (DB 13's sync_conflicts table) so resolution
        // survives a restart; IPersonalSyncConflictResolutionService owns Keep This Device/Use Other
        // Device/Accept Deletion — no ViewModel implements any of that logic directly.
        services.AddSingleton<ISyncConflictRepository, SqliteSyncConflictRepository>();
        services.AddSingleton<IPersonalSyncConflictResolutionService, PersonalSyncConflictResolutionService>();

        // Phase 16F: the automatic-sync coordinator (debounce/coalescing/periodic-safety-sync/retry
        // backoff/status) — one singleton for the process's lifetime, started in
        // OnFrameworkInitializationCompleted and stopped during shutdown (AttachShutdownFlush). Never
        // resolved (and therefore never started) by the headless --scheduled-refresh entry point, which
        // reuses this same BuildServiceProvider but has no interactive session to run automatic sync for
        // (spec §31 — Personal Sync operates only while the interactive app is running).
        services.AddSingleton<IPersonalSyncCoordinator, PersonalSyncCoordinator>();

        services.AddTransient<PersonalSyncSettingsViewModel>();

        services.AddTransient<MainWindow>();
        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Wires Phase 2's persistence/freshness services and Phase 3's retrieval adapters into the
    /// composition root so <see cref="IRefreshOrchestrator"/> can be resolved (Phase 3G Part K).
    /// Nothing here is touched by any ViewModel/screen yet — that remains a later phase.
    /// </summary>
    private static void AddRefreshOrchestration(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();

        // Persistence (Phase 2 repository ports over the same SqliteConnectionFactory already registered above).
        services.AddSingleton<ISourceStateRepository, SqliteSourceStateRepository>();
        // Phase 13D Part U: the single-row Deep Refresh status table — consumed by RefreshOrchestrator
        // (writes truthfully after every RunDeepRefreshAsync) and by AssociatePassdownService/
        // OperationsDashboardService (read the coverage-based NeedsDeepRefresh decision).
        services.AddSingleton<IDeepRefreshStatusRepository, SqliteDeepRefreshStatusRepository>();
        services.AddSingleton<IDatedResultRepository, SqliteDatedResultRepository>();
        services.AddSingleton<ICatalogProductRepository, SqliteCatalogProductRepository>();
        services.AddSingleton<IProductHistoryRepository, SqliteProductHistoryRepository>();
        services.AddSingleton<IUnitOfWorkFactory, SqliteUnitOfWorkFactory>();
        services.AddSingleton<IProductReconciliationService, ProductReconciliationService>();

        // Freshness/scheduling (Phase 2).
        services.Configure<FreshnessOptions>(configuration.GetSection("Freshness"));
        services.Configure<RollingWindowOptions>(configuration.GetSection("RollingWindow"));
        services.AddSingleton<IReadOnlyDictionary<SourceCategory, FreshnessPolicy>>(provider =>
            FreshnessPolicyCatalog.Build(provider.GetRequiredService<IOptions<FreshnessOptions>>().Value));
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<RollingWindowOptions>>().Value);
        services.AddSingleton<IFreshnessPlanner, FreshnessPlanner>();
        services.AddSingleton<IRollingDatePlanner, RollingDatePlanner>();

        // Shared retrieval transport (Phase 3A).
        services.Configure<RetrievalHttpOptions>(configuration.GetSection("RetrievalHttp"));
        services.AddSingleton(new HttpClient());
        services.AddSingleton<IRetrievalHttpClient>(provider => new RetrievalHttpClient(
            provider.GetRequiredService<HttpClient>(),
            provider.GetRequiredService<IOptions<RetrievalHttpOptions>>().Value));

        // Phase 7B: Microsoft.Playwright was removed from the production dependency graph (no
        // source adapter used it — see docs/release-strategy.md "Playwright dependency decision").
        // UnavailableJobBrowserSessionFactory keeps the IJobBrowserSessionFactory contract intact
        // for RefreshOrchestrator without pulling in the Playwright package.
        services.AddSingleton<IJobBrowserSessionFactory, UnavailableJobBrowserSessionFactory>();

        // Completed Phase 3 source adapters (Part A "completed sources currently include").
        services.AddSingleton<ParkHoursSource>();
        services.AddSingleton<DatedAdmissionSource>();
        services.AddSingleton<DatedFastLaneSource>();
        services.AddSingleton<DatedHauntedSource>();
        services.AddSingleton<PromotionsCatalogSource>();
        services.AddSingleton<SeasonPassCatalogSource>();
        services.AddSingleton<SeasonPassAddOnsCatalogSource>();
        services.AddSingleton<GeneralAddOnsCatalogSource>();
        // Phase 13C: Bring-A-Friend Duo (dated, exact per-person price) and Military/First Responder
        // (catalog, "Starting From").
        services.AddSingleton<DatedBringAFriendDuoSource>();
        services.AddSingleton<MilitaryFirstResponderCatalogSource>();
        // Phase 15A: ordinary Bring-A-Friend's exact per-date price (anonymous, keyword
        // BAFWizardDailyTickets) — supersedes Phase 13C's passholder-login-only finding.
        services.AddSingleton<DatedBringAFriendSource>();

        // Refresh-orchestrator source registrations (Part A) — every adapter above, paired with
        // how its successful payload is persisted (dated) or reconciled (catalog). None of the
        // currently-implemented adapters requires the shared browser session (all are DirectApi
        // or Html; spec §6.4).
        services.AddSingleton<IReadOnlyList<SourceRegistration>>(provider =>
        {
            var datedResultRepository = provider.GetRequiredService<IDatedResultRepository>();
            var reconciliationService = provider.GetRequiredService<IProductReconciliationService>();

            return new List<SourceRegistration>
            {
                new DatedSourceRegistration<ParkHoursPayload>(provider.GetRequiredService<ParkHoursSource>(), datedResultRepository),
                new DatedSourceRegistration<DatedAdmissionPayload>(provider.GetRequiredService<DatedAdmissionSource>(), datedResultRepository),
                new DatedSourceRegistration<DatedFastLanePayload>(provider.GetRequiredService<DatedFastLaneSource>(), datedResultRepository),
                new DatedSourceRegistration<DatedHauntedPayload>(provider.GetRequiredService<DatedHauntedSource>(), datedResultRepository),
                new CatalogSourceRegistration(provider.GetRequiredService<PromotionsCatalogSource>(), reconciliationService),
                new CatalogSourceRegistration(provider.GetRequiredService<SeasonPassCatalogSource>(), reconciliationService),
                new CatalogSourceRegistration(provider.GetRequiredService<SeasonPassAddOnsCatalogSource>(), reconciliationService),
                new CatalogSourceRegistration(provider.GetRequiredService<GeneralAddOnsCatalogSource>(), reconciliationService),
                new DatedSourceRegistration<DatedBringAFriendDuoPayload>(provider.GetRequiredService<DatedBringAFriendDuoSource>(), datedResultRepository),
                new CatalogSourceRegistration(provider.GetRequiredService<MilitaryFirstResponderCatalogSource>(), reconciliationService),
                new DatedSourceRegistration<DatedBringAFriendPayload>(provider.GetRequiredService<DatedBringAFriendSource>(), datedResultRepository),
            };
        });

        services.Configure<RefreshOrchestratorOptions>(configuration.GetSection("Refresh"));
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<RefreshOrchestratorOptions>>().Value);
        services.AddSingleton<IRefreshOrchestrator, RefreshOrchestrator>();

        // Phase 6A: the headless --scheduled-refresh entry point's only Application-layer
        // dependency. Registered here (not conditionally) since registering a type does no I/O and
        // the normal desktop launch simply never resolves it (Part B "one authoritative
        // service-registration path").
        services.AddSingleton<IScheduledRefreshRunner, ScheduledRefreshRunner>();
    }
}
