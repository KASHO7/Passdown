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
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Carowinds.Passdown.Ui.Tests")]

namespace Carowinds.Passdown.Desktop;

public partial class App : global::Avalonia.Application
{
    private IServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _services = BuildServiceProvider();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = _services ?? throw new InvalidOperationException(
                $"{nameof(_services)} was not initialized; {nameof(Initialize)} must run first.");

            // Storage must be ready (spec §17 Phase 1: "establish ... storage
            // migrations before building the production UI") before any
            // view model touches the database.
            services.GetRequiredService<MigrationRunner>().Run();

            var mainWindow = services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = mainWindow;

            StartCatchUpRefresh(services);
        }

        base.OnFrameworkInitializationCompleted();
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

    internal static IServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
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
        // above) — it never performs HTTP/Playwright retrieval and never loads a finalized PDF blob.
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
