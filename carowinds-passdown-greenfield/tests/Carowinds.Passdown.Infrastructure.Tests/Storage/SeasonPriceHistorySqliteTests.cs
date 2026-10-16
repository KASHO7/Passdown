using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>
/// Phase 20A: season price history against real SQLite — the repository, the shared unit of work
/// (rollback), cross-connection concurrency, and the schema-16 migration's backfill/upgrade safety.
/// </summary>
public class SeasonPriceHistorySqliteTests : IDisposable
{
    private const string SeasonPassesSource = "Carowinds Season Passes";
    private static readonly DateTimeOffset Day1 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static DateTimeOffset Day(int n) => Day1.AddDays(n - 1);

    private static SourceEvidence Verified(DateTimeOffset at, string sourceName = SeasonPassesSource) => new(
        sourceName, new Uri("https://www.sixflags.com/carowinds/season-passes"), at, RetrievalStatus.Verified, "Checked.", RetrievalStrategy.Html);

    private static ObservedProduct Gold(decimal price, string? qualifier = null) => new(
        CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, "gold"), "2027 Gold Pass", price, $"${price}", null, qualifier, false, null);

    private ProductReconciliationService Reconciliation(SqliteConnectionFactory factory, ISeasonPriceHistoryRepository? history = null) => new(
        new SqliteCatalogProductRepository(factory),
        new SqliteProductHistoryRepository(factory),
        new SqliteUnitOfWorkFactory(factory),
        logger: null,
        new SeasonPriceHistoryRecorder(history ?? new SqliteSeasonPriceHistoryRepository(factory)));

    private SqliteSeasonPriceHistoryRepository Repository() => new(_fixture.ConnectionFactory);

    private static SeasonPriceHistoryInterval Interval(int sequence, decimal amount, DateTimeOffset at) => new(
        "SeasonPasses:id:gold", sequence, amount, PriceKind.StandardCatalog, $"${amount}", null, "2027 Gold Pass", SeasonPassesSource, at, at, SeasonPriceHistoryOrigin.Observed);

    [Fact]
    public async Task Repository_RoundTripsIntervals_OldestFirst()
    {
        var repository = Repository();
        await repository.InsertAsync(Interval(2, 105.00m, Day(3)) with { PriceKind = PriceKind.StartingFrom, PriceQualifier = "Starting From" });
        await repository.InsertAsync(Interval(1, 99m, Day(1)));

        var history = await repository.GetHistoryAsync("SeasonPasses:id:gold");

        Assert.Equal([1, 2], history.Select(i => i.Sequence));
        Assert.Equal(105m, history[1].Amount);
        Assert.Equal(PriceKind.StartingFrom, history[1].PriceKind);
        Assert.Equal("Starting From", history[1].PriceQualifier);
        Assert.Equal(Day(3), history[1].FirstObservedAt);
        Assert.Equal(2, (await repository.GetLatestAsync("SeasonPasses:id:gold"))!.Sequence);
    }

    [Fact]
    public async Task Repository_RejectsADuplicateSequence()
    {
        var repository = Repository();
        await repository.InsertAsync(Interval(1, 99m, Day(1)));

        await Assert.ThrowsAsync<SqliteException>(() => repository.InsertAsync(Interval(1, 105m, Day(2))));
    }

    [Fact]
    public async Task Repository_ConfirmOnlyMovesForward()
    {
        var repository = Repository();
        await repository.InsertAsync(Interval(1, 99m, Day(1)));

        await repository.ConfirmAsync("SeasonPasses:id:gold", 1, Day(5));
        await repository.ConfirmAsync("SeasonPasses:id:gold", 1, Day(2));

        Assert.Equal(Day(5), (await repository.GetLatestAsync("SeasonPasses:id:gold"))!.LastConfirmedAt);
    }

    [Fact]
    public async Task Reconciliation_PersistsBaselineConfirmationAndChanges_AndTheSummaryCountsThem()
    {
        var service = Reconciliation(_fixture.ConnectionFactory);

        await service.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(1)), [Gold(99m)]);
        await service.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(2)), [Gold(99m)]);
        await service.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(3)), [Gold(105m)]);
        await service.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(4)), [Gold(105m, "Starting From")]);

        var history = await Repository().GetHistoryAsync(Gold(0m).Key.ToString());
        Assert.Equal([99m, 105m, 105m], history.Select(i => i.Amount));
        Assert.Equal(Day(2), history[0].LastConfirmedAt);
        Assert.Equal(PriceKind.StartingFrom, history[2].PriceKind);

        var summary = await Repository().GetSummaryAsync();
        Assert.Equal((1, 3, (DateTimeOffset?)Day(4)), (summary.TrackedProductCount, summary.PriceStateCount, summary.MostRecentChangeAt));
    }

    [Fact]
    public async Task HistoryWriteFailure_RollsBackTheCatalogUpsert()
    {
        var service = Reconciliation(_fixture.ConnectionFactory, new FailingInsertRepository(Repository()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(1)), [Gold(99m)]));

        Assert.Null(await new SqliteCatalogProductRepository(_fixture.ConnectionFactory).GetAsync(Gold(99m).Key));
        Assert.Empty(await new SqliteProductHistoryRepository(_fixture.ConnectionFactory).GetHistoryAsync(Gold(99m).Key.ToString()));
        Assert.Empty(await Repository().GetHistoryAsync(Gold(99m).Key.ToString()));
    }

    [Fact]
    public async Task HistoryWriteFailure_OnAPriceChange_KeepsTheLastKnownGoodPrice()
    {
        await Reconciliation(_fixture.ConnectionFactory).ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(1)), [Gold(99m)]);
        var failing = Reconciliation(_fixture.ConnectionFactory, new FailingInsertRepository(Repository()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(2)), [Gold(105m)]));

        var record = await new SqliteCatalogProductRepository(_fixture.ConnectionFactory).GetAsync(Gold(99m).Key);
        Assert.Equal(99m, record!.CurrentPriceAmount);
        Assert.Single(await Repository().GetHistoryAsync(Gold(99m).Key.ToString()));
    }

    [Fact]
    public async Task ConcurrentIdenticalObservations_FromTwoConnections_CreateOneBaseline()
    {
        // Simulates the scheduled-refresh process and the interactive app checking at the same moment.
        var first = Reconciliation(_fixture.ConnectionFactory);
        var second = Reconciliation(_fixture.ReopenConnectionFactory());

        await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() =>
            (i % 2 == 0 ? first : second).ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(1).AddMinutes(i)), [Gold(105m)]))));

        var history = await Repository().GetHistoryAsync(Gold(105m).Key.ToString());
        var interval = Assert.Single(history);
        Assert.Equal(1, interval.Sequence);
    }

    private sealed class FailingInsertRepository(ISeasonPriceHistoryRepository inner) : ISeasonPriceHistoryRepository
    {
        public Task<SeasonPriceHistoryInterval?> GetLatestAsync(string productKey, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default) =>
            inner.GetLatestAsync(productKey, unitOfWork, cancellationToken);

        public Task InsertAsync(SeasonPriceHistoryInterval interval, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated price-history write failure.");

        public Task ConfirmAsync(string productKey, int sequence, DateTimeOffset confirmedAt, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default) =>
            inner.ConfirmAsync(productKey, sequence, confirmedAt, unitOfWork, cancellationToken);

        public Task<IReadOnlyList<SeasonPriceHistoryInterval>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default) =>
            inner.GetHistoryAsync(productKey, cancellationToken);

        public Task<IReadOnlySet<string>> GetTrackedProductKeysAsync(CancellationToken cancellationToken = default) =>
            inner.GetTrackedProductKeysAsync(cancellationToken);

        public Task<SeasonPriceHistorySummary> GetSummaryAsync(CancellationToken cancellationToken = default) => inner.GetSummaryAsync(cancellationToken);
    }
}

/// <summary>Phase 20A: upgrading a schema-15 database — backfill from authoritative product_history rows only.</summary>
public class Migration0016BackfillTests : IDisposable
{
    private static readonly DateTimeOffset Day1 = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-m16-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private static DateTimeOffset Day(int n) => Day1.AddDays(n - 1);

    private SqliteConnectionFactory Factory() => new(_databasePath);

    private static IMigration[] All() =>
    [
        new Migration0001CreateSchemaVersionTable(), new Migration0002CreateSourceStateTable(), new Migration0003CreateDatedResultsTable(),
        new Migration0004CreateCatalogProductsTable(), new Migration0005CreateProductHistoryTable(), new Migration0006CreateAssociateDraftsTable(),
        new Migration0007CreateManagerDraftsTable(), new Migration0008CreateFinalizedPassdownsTable(), new Migration0009AddFinalizedPassdownPdfColumns(),
        new Migration0010CreateOperationalNotesTable(), new Migration0011CreateDeepRefreshStatusTable(), new Migration0012CreateSyncEntityStateTable(),
        new Migration0013CreateSyncConflictsTable(), new Migration0014CreateMonthlyErtConfigurationsTable(), new Migration0015CreateErtDateOverridesTable(),
        new Migration0016CreateSeasonPriceHistoryTable(),
    ];

    private int Migrate(int count) => new MigrationRunner(Factory(), All().Take(count).ToArray(), NullLogger<MigrationRunner>.Instance).Run();

    private static SourceEvidence Evidence(DateTimeOffset at, string source) => new(
        source, new Uri("https://www.sixflags.com/carowinds"), at, RetrievalStatus.Verified, "Checked.", RetrievalStrategy.Html);

    private static ObservedProduct Product(SourceCategory category, string id, string name, decimal price) => new(
        CatalogProductKey.FromOfficialId(category, id), name, price, $"${price}", null, null, false, null);

    /// <summary>Builds a realistic schema-15 history using the pre-Phase-20A reconciliation (no recorder).</summary>
    private async Task SeedSchema15Async()
    {
        Assert.Equal(15, Migrate(15));
        var factory = Factory();
        var legacy = new ProductReconciliationService(
            new SqliteCatalogProductRepository(factory), new SqliteProductHistoryRepository(factory), new SqliteUnitOfWorkFactory(factory));

        var gold = Product(SourceCategory.SeasonPasses, "gold", "2027 Gold Pass", 99m);
        await legacy.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day(1), "Carowinds Season Passes"), [gold]);              // new: $99
        await legacy.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day(2), "Carowinds Season Passes"), [gold]);              // unchanged (no row)
        await legacy.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day(39), "Carowinds Season Passes"), [gold with { CurrentPriceAmount = 105m }]); // Sep 19: $105
        await legacy.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day(40), "Carowinds Season Passes"), []);                 // missing once
        await legacy.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day(41), "Carowinds Season Passes"), []);                 // retired
        await legacy.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day(50), "Carowinds Season Passes"), [gold with { CurrentPriceAmount = 105m }]); // back at $105
        await legacy.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day(52), "Carowinds Season Passes"), [gold with { CurrentPriceAmount = 105m }]); // confirmed (no row)

        await legacy.ReconcileAsync(SourceCategory.Promotions, Evidence(Day(1), "Carowinds Promotions"),
            [Product(SourceCategory.Promotions, "promo", "Bring-A-Friend Ticket", 35m)]);
        await legacy.ReconcileAsync(SourceCategory.AddOns, Evidence(Day(1), "Carowinds General Add-Ons"),
            [Product(SourceCategory.AddOns, "photo1", "Single Day FunPix", 29.99m)]);
        await legacy.ReconcileAsync(SourceCategory.AddOns, Evidence(Day(1), "Carowinds Season Pass Add-Ons"),
            [Product(SourceCategory.AddOns, "dining", "All Season Dining Access", 199m)]);
    }

    private long Scalar(string sql)
    {
        using var connection = Factory().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public async Task Upgrade_BackfillsTrackedProductsFromOfficialObservations_Only()
    {
        await SeedSchema15Async();
        var productHistoryRowsBefore = Scalar("SELECT COUNT(*) FROM product_history;");
        var catalogRowsBefore = Scalar("SELECT COUNT(*) FROM catalog_products;");

        Assert.Equal(16, Migrate(16));

        var repository = new SqliteSeasonPriceHistoryRepository(Factory());
        var gold = await repository.GetHistoryAsync("SeasonPasses:id:gold");
        Assert.Equal([99m, 105m, 105m], gold.Select(i => i.Amount));
        Assert.All(gold, i => Assert.Equal(SeasonPriceHistoryOrigin.Backfilled, i.Origin));
        Assert.Equal((Day(1), Day(1)), (gold[0].FirstObservedAt, gold[0].LastConfirmedAt));
        Assert.Equal((Day(39), Day(39)), (gold[1].FirstObservedAt, gold[1].LastConfirmedAt));
        // Reappearance after retirement is its own interval; its last confirmation comes from the catalog's last-seen time.
        Assert.Equal((Day(50), Day(52)), (gold[2].FirstObservedAt, gold[2].LastConfirmedAt));

        Assert.Single(await repository.GetHistoryAsync("AddOns:id:dining"));
        Assert.Empty(await repository.GetHistoryAsync("AddOns:id:photo1"));
        Assert.Empty(await repository.GetHistoryAsync("Promotions:id:promo"));

        Assert.Equal(productHistoryRowsBefore, Scalar("SELECT COUNT(*) FROM product_history;"));
        Assert.Equal(catalogRowsBefore, Scalar("SELECT COUNT(*) FROM catalog_products;"));
    }

    [Fact]
    public async Task AfterUpgrade_LiveRecordingContinuesTheBackfilledTimeline()
    {
        await SeedSchema15Async();
        Migrate(16);
        var factory = Factory();
        var service = new ProductReconciliationService(
            new SqliteCatalogProductRepository(factory), new SqliteProductHistoryRepository(factory), new SqliteUnitOfWorkFactory(factory),
            logger: null, new SeasonPriceHistoryRecorder(new SqliteSeasonPriceHistoryRepository(factory)));

        await service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day(60), "Carowinds Season Passes"),
            [Product(SourceCategory.SeasonPasses, "gold", "2027 Gold Pass", 105m)]);
        await service.ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day(61), "Carowinds Season Passes"),
            [Product(SourceCategory.SeasonPasses, "gold", "2027 Gold Pass", 109m)]);

        var gold = await new SqliteSeasonPriceHistoryRepository(factory).GetHistoryAsync("SeasonPasses:id:gold");
        Assert.Equal([99m, 105m, 105m, 109m], gold.Select(i => i.Amount));
        Assert.Equal(Day(60), gold[2].LastConfirmedAt);
        Assert.Equal(SeasonPriceHistoryOrigin.Observed, gold[3].Origin);
    }

    [Fact]
    public void Upgrade_OfAnEmptySchema15Database_CreatesAnEmptyTable()
    {
        Assert.Equal(15, Migrate(15));

        Assert.Equal(16, Migrate(16));

        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM season_price_history;"));
        Assert.Equal(16, Scalar("PRAGMA user_version;"));
    }
}
