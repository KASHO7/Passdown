using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>Phase 20A: the Price History read side and the Associate row's "Price history" eligibility.</summary>
public class SeasonPriceHistoryServiceTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static SourceEvidence Verified(DateTimeOffset at, string sourceName) => new(
        sourceName, new Uri("https://www.sixflags.com/carowinds"), at, RetrievalStatus.Verified, "Checked.", RetrievalStrategy.Html);

    private static ObservedProduct Product(SourceCategory category, string id, string name, decimal price) => new(
        CatalogProductKey.FromOfficialId(category, id), name, price, $"${price}", null, null, DateSpecific: false, ApplicableDate: null);

    private sealed class Harness
    {
        public FakeClock Clock { get; } = new(Day1);
        public InMemoryCatalogProductRepository Catalog { get; } = new();
        public InMemorySeasonPriceHistoryRepository PriceHistory { get; } = new();
        public ProductReconciliationService Reconciliation { get; }
        public SeasonPriceHistoryService Service { get; }

        public Harness()
        {
            Reconciliation = new ProductReconciliationService(
                Catalog, new InMemoryProductHistoryRepository(), new FakeUnitOfWorkFactory(), logger: null, new SeasonPriceHistoryRecorder(PriceHistory));
            Service = new SeasonPriceHistoryService(Catalog, PriceHistory);
        }

        public AssociatePassdownService Associate(bool withHistory = true) => new(
            new InMemoryAssociateDraftRepository(), new InMemoryDatedResultRepository(), Catalog, new FakeRefreshOrchestrator(),
            new OperationalNotesService(new InMemoryOperationalNoteRepository(), Clock), Clock,
            seasonPriceHistoryRepository: withHistory ? PriceHistory : null);
    }

    private static readonly ObservedProduct Gold = Product(SourceCategory.SeasonPasses, "gold", "2027 Gold Pass", 99m);
    private static readonly ObservedProduct Silver = Product(SourceCategory.SeasonPasses, "silver", "2027 Silver Pass", 79m);
    private static readonly ObservedProduct Dining = Product(SourceCategory.AddOns, "dining", "All Season Dining Access", 199m);
    private static readonly ObservedProduct DailyPhoto = Product(SourceCategory.AddOns, "photo1", "Single Day FunPix", 29.99m);
    private static readonly ObservedProduct PromoCard = Product(SourceCategory.Promotions, "promo", "Bring-A-Friend Ticket", 35m);

    private static async Task<Harness> SeededAsync()
    {
        var h = new Harness();
        await h.Reconciliation.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day1, "Carowinds Season Passes"), [Gold, Silver]);
        await h.Reconciliation.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day1.AddDays(5), "Carowinds Season Passes"), [Gold with { CurrentPriceAmount = 105m }, Silver]);
        await h.Reconciliation.ReconcileAsync(SourceCategory.AddOns, Verified(Day1, PriceHistoryTrackingPolicy.SeasonPassAddOnsSourceName), [Dining]);
        await h.Reconciliation.ReconcileAsync(SourceCategory.AddOns, Verified(Day1, "Carowinds General Add-Ons"), [DailyPhoto]);
        await h.Reconciliation.ReconcileAsync(SourceCategory.Promotions, Verified(Day1, "Carowinds Promotions"), [PromoCard]);
        return h;
    }

    [Fact]
    public async Task ListTrackedProducts_ContainsOnlySeasonPassesAndSeasonPassAddOns()
    {
        var h = await SeededAsync();

        var products = await h.Service.ListTrackedProductsAsync();

        Assert.Equal(["2027 Gold Pass", "2027 Silver Pass", "All Season Dining Access"], products.Select(p => p.DisplayName));
        Assert.Equal([PriceHistoryFamily.SeasonPass, PriceHistoryFamily.SeasonPass, PriceHistoryFamily.SeasonPassAddOn], products.Select(p => p.Family));
        Assert.Equal([2, 1, 1], products.Select(p => p.StateCount));
        Assert.All(products, p => Assert.True(p.IsActive));
    }

    [Fact]
    public async Task RetiredTrackedProduct_IsStillListed_AsInactive_WithItsHistory()
    {
        var h = await SeededAsync();
        await h.Reconciliation.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day1.AddDays(6), "Carowinds Season Passes"), [Gold with { CurrentPriceAmount = 105m }]);
        await h.Reconciliation.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day1.AddDays(7), "Carowinds Season Passes"), [Gold with { CurrentPriceAmount = 105m }]);

        var silver = (await h.Service.ListTrackedProductsAsync()).Single(p => p.DisplayName == "2027 Silver Pass");

        Assert.False(silver.IsActive);
        Assert.Single(await h.Service.GetHistoryAsync(silver.ProductKey));
    }

    [Fact]
    public async Task GetHistory_IsOldestFirst_WithDerivedChanges()
    {
        var h = await SeededAsync();

        var history = await h.Service.GetHistoryAsync(Gold.Key.ToString());

        Assert.Equal(2, history.Count);
        Assert.Null(history[0].ChangeFromPrevious);
        Assert.Equal(6m, history[1].ChangeFromPrevious!.AmountDelta);
        Assert.False(history[1].IsReappearanceAtSamePrice);
    }

    [Fact]
    public async Task AssociateComposition_ExposesPriceHistoryOnlyForTrackedRows()
    {
        var h = await SeededAsync();

        var workspace = await h.Associate().LoadAssociateDateAsync(new DateOnly(2026, 9, 6));

        var byName = workspace.Products.ToDictionary(p => p.DisplayName);
        Assert.Equal(Gold.Key.ToString(), byName["2027 Gold Pass"].PriceHistoryProductKey);
        Assert.Equal(Dining.Key.ToString(), byName["All Season Dining Access"].PriceHistoryProductKey);
        Assert.Null(byName["Single Day FunPix"].PriceHistoryProductKey);
        Assert.Null(byName["Bring-A-Friend Ticket"].PriceHistoryProductKey);
    }

    [Fact]
    public async Task AssociateComposition_StillWorks_WhenPriceHistoryCannotBeRead()
    {
        var h = await SeededAsync();
        h.PriceHistory.FailReads = true;

        var workspace = await h.Associate().LoadAssociateDateAsync(new DateOnly(2026, 9, 6));

        // Falls back to the tracking policy alone; nothing about the screen fails.
        Assert.Equal(Gold.Key.ToString(), workspace.Products.Single(p => p.DisplayName == "2027 Gold Pass").PriceHistoryProductKey);
    }
}
