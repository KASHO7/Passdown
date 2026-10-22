using System.Net;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>
/// RC.9 Windows VM defect: Computer A began checking before the official redesign, so its Gold
/// Season Pass — 2027 continues under the legacy CMS-id key (Phase 25 legacy identity), while
/// Computer B began after it and files the same product under the deterministic pass-offer key.
/// Both exporters correctly carry every observation, and B imports A's — but B's Price History
/// showed only its own row because A's observations sat under the other key, listed as a separate
/// retired imported-only product hidden by default. The read-time view must fold the two keys of
/// one pass offer into one history on both computers, without rewriting anything stored.
/// Two disposable SQLite profiles, the real adapter, reconciliation and sync — never a real profile.
/// </summary>
public sealed class Phase26LegacyPassIdentitySyncTests : IDisposable
{
    private const string SourceName = "Carowinds Season Passes";
    private const string LegacyGoldKey = "SeasonPasses:id:550e3db6-5bad-45f8-8404-5d608c367617";
    private const string LegacyPrestigeKey = "SeasonPasses:id:5de73366-26f8-4c9d-8983-407dc10132ea";

    private static readonly DateTimeOffset LegacyCheck = new(2026, 8, 22, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RedesignCheck = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    private static readonly string OfferGoldKey = PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027).ToKey().ToString();

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"carowinds-legacy-pass-sync-{Guid.NewGuid():N}");

    private string Root => Path.Combine(_workspace, "shared");

    public Phase26LegacyPassIdentitySyncTests() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private sealed class Computer(SqliteConnectionFactory factory, Guid deviceId)
    {
        public Guid DeviceId { get; } = deviceId;

        public SqliteCatalogProductRepository Catalog { get; } = new(factory);

        public SqliteSeasonPriceHistoryRepository Local { get; } = new(factory);

        public SqliteSyncedPriceObservationRepository Synced { get; } = new(factory);

        public SeasonPriceHistoryService History => new(Catalog, Local, Synced);

        public ProductReconciliationService Reconciliation() => new(
            Catalog, new SqliteProductHistoryRepository(factory), new SqliteUnitOfWorkFactory(factory), logger: null,
            new SeasonPriceHistoryRecorder(Local));

        public Task<OfficialPriceHistorySyncResult> SyncAsync(string root) =>
            new OfficialPriceHistorySync(Local, Synced).RunAsync(root, DeviceId, Now);
    }

    private Computer NewComputer(string name)
    {
        var factory = new SqliteConnectionFactory(Path.Combine(_workspace, name, "carowinds-passdown.db"));
        new MigrationRunner(factory, Phase26OfficialPriceHistorySyncTests.AllMigrations(), NullLogger<MigrationRunner>.Instance).Run();
        return new Computer(factory, Guid.NewGuid());
    }

    /// <summary>What the pre-redesign adapter recorded on Computer A (legacy CMS-id keys, $89 sale).</summary>
    private static async Task SeedLegacyPassesAsync(Computer computer)
    {
        var evidence = new SourceEvidence(
            SourceName, new Uri("https://www.sixflags.com/carowinds/season-passes"), LegacyCheck, RetrievalStatus.Verified, "2 season passes confirmed.", RetrievalStrategy.Html);
        await computer.Reconciliation().ReconcileAsync(SourceCategory.SeasonPasses, evidence,
        [
            new ObservedProduct(CatalogProductKey.Parse(LegacyGoldKey), "2027 Gold Pass", 89m, "$89", 115m, "Sale Price", false, null),
            new ObservedProduct(CatalogProductKey.Parse(LegacyPrestigeKey), "2027 Prestige Pass", 229m, "$229", 275m, "Sale Price", false, null),
        ]);
    }

    /// <summary>One official check of the redesigned page through the real adapter and registration.</summary>
    private static async Task CheckRedesignedPageAsync(Computer computer)
    {
        var source = new SeasonPassCatalogSource(
            new FakeRetrievalHttpClient(new RetrievalHttpResponse(HttpStatusCode.OK, "text/html", FixtureFile.ReadText("memberships-and-season-passes/published-page.html"))),
            new FakeClock(RedesignCheck));
        var outcome = await new CatalogSourceRegistration(source, computer.Reconciliation()).ExecuteAsync(requestedDate: null, CancellationToken.None);
        Assert.Equal(SourceOperationOutcomeKind.Success, outcome.Outcome);
    }

    /// <summary>A (pre-redesign + redesign) and B (redesign only), then Sync A → B → A.</summary>
    private async Task<(Computer A, Computer B)> TwoComputersSyncedAsync()
    {
        var a = NewComputer("Computer-A");
        await SeedLegacyPassesAsync(a);
        await CheckRedesignedPageAsync(a);

        var b = NewComputer("Computer-B");
        await CheckRedesignedPageAsync(b);

        await a.SyncAsync(Root);
        await b.SyncAsync(Root);
        await a.SyncAsync(Root);
        return (a, b);
    }

    private static PriceHistoryProduct VisibleGold(IReadOnlyList<PriceHistoryProduct> products) =>
        Assert.Single(products, p => p.IsActive && p.Family == PriceHistoryFamily.SeasonPass && p.DisplayName == "Gold Season Pass — 2027");

    [Fact]
    public async Task Precondition_TheTwoComputersFileGold2027UnderDifferentKeys()
    {
        var (a, b) = await TwoComputersSyncedAsync();

        Assert.Contains(LegacyGoldKey, await a.Local.GetTrackedProductKeysAsync());
        Assert.DoesNotContain(OfferGoldKey, await a.Local.GetTrackedProductKeysAsync());
        Assert.Contains(OfferGoldKey, await b.Local.GetTrackedProductKeysAsync());
        Assert.DoesNotContain(LegacyGoldKey, await b.Local.GetTrackedProductKeysAsync());

        // Export/import is correct: B holds A's legacy-key observations with their original fields.
        var importedLegacy = (await b.Synced.GetForProductAsync(LegacyGoldKey)).Select(i => i.Observation).OrderBy(o => o.FirstObservedUtc).ToList();
        Assert.Equal([89m, 99m], importedLegacy.Select(o => o.Amount));
        Assert.Equal(LegacyCheck, importedLegacy[0].FirstObservedUtc);
        Assert.Equal("2027 Gold Pass", importedLegacy[0].DisplayName);
        Assert.Equal(PriceKind.Sale, importedLegacy[0].PriceKind);
        Assert.All(importedLegacy, o => Assert.Equal(a.DeviceId, o.ContributorDeviceId));
    }

    [Fact]
    public async Task ComputerB_ShowsComputerAsOlderGoldHistory_UnderItsOneVisibleGoldProduct()
    {
        var (_, b) = await TwoComputersSyncedAsync();

        var products = await b.History.ListTrackedProductsAsync();
        var gold = VisibleGold(products);
        Assert.Equal(OfferGoldKey, gold.ProductKey);
        Assert.DoesNotContain(products, p => p.ProductKey == LegacyGoldKey); // no separate hidden "retired" copy

        var history = await b.History.GetHistoryAsync(gold.ProductKey);
        Assert.Equal(gold.StateCount, history.Count);
        Assert.Equal([89m, 99m], history.Select(e => e.Interval.Amount));
        Assert.Equal(LegacyCheck, history[0].Interval.FirstObservedAt); // original official check time
        Assert.Equal(PriceKind.Sale, history[0].Interval.PriceKind);      // never reclassified (e.g. as a renewal)
        Assert.Equal(PriceHistoryProvenance.ImportedThroughSync, history[0].Provenance);
        Assert.Equal(PriceHistoryProvenance.CollectedHere | PriceHistoryProvenance.ImportedThroughSync, history[1].Provenance);
        Assert.Equal(RedesignCheck, history[1].Interval.FirstObservedAt);
    }

    [Fact]
    public async Task BothComputers_ShowTheSameGoldHistory()
    {
        var (a, b) = await TwoComputersSyncedAsync();

        var goldA = VisibleGold(await a.History.ListTrackedProductsAsync());
        var goldB = VisibleGold(await b.History.ListTrackedProductsAsync());
        Assert.Equal(LegacyGoldKey, goldA.ProductKey);

        static IEnumerable<(decimal, PriceKind, DateTimeOffset, DateTimeOffset)> Logical(IReadOnlyList<PriceHistoryEntry> entries) =>
            entries.Select(e => (e.Interval.Amount, e.Interval.PriceKind, e.Interval.FirstObservedAt, e.Interval.LastConfirmedAt));
        Assert.Equal(Logical(await a.History.GetHistoryAsync(goldA.ProductKey)), Logical(await b.History.GetHistoryAsync(goldB.ProductKey)));
        Assert.DoesNotContain(await a.History.ListTrackedProductsAsync(), p => p.ProductKey == OfferGoldKey);
    }

    [Fact]
    public async Task FoldingIsReadOnly_StoredRowsAndCurrentPricesAreUnchanged()
    {
        var (a, b) = await TwoComputersSyncedAsync();
        var localB = await b.Local.GetHistoryAsync(OfferGoldKey);
        var catalogB = await b.Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, lifecycleState: null);
        var syncedB = await b.Synced.GetAllAsync();

        _ = await b.History.ListTrackedProductsAsync();
        _ = await b.History.GetHistoryAsync(OfferGoldKey);
        await b.SyncAsync(Root);

        Assert.Equal(localB, await b.Local.GetHistoryAsync(OfferGoldKey));
        Assert.Single(localB); // B's own series still holds only its own check
        Assert.Equal(catalogB, await b.Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, lifecycleState: null));
        Assert.Equal(99m, catalogB.Single(r => r.ProductKey == OfferGoldKey).CurrentPriceAmount);
        Assert.Equal(syncedB.Select(s => s.Observation), (await b.Synced.GetAllAsync()).Select(s => s.Observation));
        Assert.Empty(await b.Local.GetHistoryAsync(LegacyGoldKey));
        Assert.Equal(2, (await a.Local.GetHistoryAsync(LegacyGoldKey)).Count);
    }

    [Fact]
    public async Task OtherTiersAndProducts_KeepTheirOwnSeries()
    {
        var (_, b) = await TwoComputersSyncedAsync();
        var products = await b.History.ListTrackedProductsAsync();

        var prestige = Assert.Single(products, p => p.IsActive && p.DisplayName == "Prestige Season Pass — 2027");
        Assert.Equal([229m, 249m], (await b.History.GetHistoryAsync(prestige.ProductKey)).Select(e => e.Interval.Amount));

        // Renewal, Membership and initiation fee never adopt the legacy pass's history.
        foreach (var product in products.Where(p => p.ProductKey != OfferGoldKey && p.ProductKey != prestige.ProductKey))
        {
            Assert.DoesNotContain(await b.History.GetHistoryAsync(product.ProductKey), e => e.Interval.Amount == 89m && e.Interval.FirstObservedAt == LegacyCheck);
        }
    }
}
