using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Retrieval.Browser;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure;
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
        }

        base.OnFrameworkInitializationCompleted();
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
        ]);
        services.AddSingleton<MigrationRunner>();

        AddRefreshOrchestration(services, configuration);

        // Phase 4B: the Associate Passdown screen (Part J). Reuses the same
        // IAssociateDraftRepository/IDatedResultRepository/ICatalogProductRepository/
        // IRefreshOrchestrator/IClock singletons AddRefreshOrchestration already registered above —
        // nothing here is a new storage or retrieval path.
        services.AddSingleton<IAssociateDraftRepository, SqliteAssociateDraftRepository>();
        services.AddSingleton<IAssociatePassdownService, AssociatePassdownService>();

        // Phase 4F Part I: the Associate PDF export pipeline (Documents project) plus the native
        // save-file picker seam (Desktop-only integration; see IPdfSaveFilePicker).
        services.AddSingleton<IAssociatePassdownPdfGenerator, AssociatePassdownPdfGenerator>();
        services.AddSingleton<IAssociatePassdownPdfExportService, AssociatePassdownPdfExportService>();
        services.AddSingleton<IPdfSaveFilePicker, AvaloniaPdfSaveFilePicker>();

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

        services.Configure<PlaywrightBrowserSessionOptions>(configuration.GetSection("Browser"));
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<PlaywrightBrowserSessionOptions>>().Value);
        services.AddSingleton<IBrowserSessionFactory, PlaywrightBrowserSessionFactory>();
        services.AddSingleton<IJobBrowserSessionFactory, PlaywrightJobBrowserSessionFactory>();

        // Completed Phase 3 source adapters (Part A "completed sources currently include").
        services.AddSingleton<ParkHoursSource>();
        services.AddSingleton<DatedAdmissionSource>();
        services.AddSingleton<DatedFastLaneSource>();
        services.AddSingleton<DatedHauntedSource>();
        services.AddSingleton<PromotionsCatalogSource>();
        services.AddSingleton<SeasonPassCatalogSource>();
        services.AddSingleton<SeasonPassAddOnsCatalogSource>();
        services.AddSingleton<GeneralAddOnsCatalogSource>();

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
            };
        });

        services.Configure<RefreshOrchestratorOptions>(configuration.GetSection("Refresh"));
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<RefreshOrchestratorOptions>>().Value);
        services.AddSingleton<IRefreshOrchestrator, RefreshOrchestrator>();
    }
}
