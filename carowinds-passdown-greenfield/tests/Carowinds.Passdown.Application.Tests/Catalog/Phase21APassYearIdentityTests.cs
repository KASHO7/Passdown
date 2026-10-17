using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>
/// Phase 21A pass-year identity, driven through the real <see cref="ProductReconciliationService"/> +
/// <see cref="SeasonPriceHistoryRecorder"/>: passes for different validity years keep distinct
/// identities and separate price timelines — including the defensive case of the official site
/// reusing one CMS id across years — while existing keys, history and the Phase 19B lifecycle are
/// untouched.
/// </summary>
public class Phase21APassYearIdentityTests
{
    private const string SeasonPassesSource = "Carowinds Season Passes";
    private static readonly DateTimeOffset Oct1 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Day(int n) => Oct1.AddDays(n - 1);

    private static SourceEvidence Verified(DateTimeOffset at) => new(
        SeasonPassesSource, new Uri("https://www.sixflags.com/carowinds/season-passes"), at, RetrievalStatus.Verified,
        "Checked.", RetrievalStrategy.Html);

    private static CatalogProductKey Key(string id) => CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, id);

    private static ObservedProduct Pass(string name, decimal price, string id = "gold-pass") => new(
        Key(id), name, price, $"${price}", null, null, DateSpecific: false, ApplicableDate: null);

    private sealed class Harness
    {
        public InMemoryCatalogProductRepository Catalog { get; } = new();
        public InMemorySeasonPriceHistoryRepository PriceHistory { get; } = new();
        public ProductReconciliationService Service { get; }

        public Harness() => Service = new ProductReconciliationService(
            Catalog, new InMemoryProductHistoryRepository(), new FakeUnitOfWorkFactory(), logger: null, new SeasonPriceHistoryRecorder(PriceHistory));

        public Task<ReconciliationResult> CheckAsync(int day, params ObservedProduct[] products) =>
            Service.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(day)), products);

        public IReadOnlyList<SeasonPriceHistoryInterval> History(string productKey) =>
            PriceHistory.All.Where(i => i.ProductKey == productKey).OrderBy(i => i.Sequence).ToList();

        public async Task<CatalogProductRecord?> GetAsync(string productKey) =>
            (await Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, null)).SingleOrDefault(r => r.ProductKey == productKey);
    }

    [Fact]
    public async Task DistinctOfficialIds_2026And2027GoldPass_StayDistinctWithSeparateTimelines()
    {
        var h = new Harness();

        await h.CheckAsync(1, Pass("2026 Gold Pass", 120m, "gold-2026"), Pass("2027 Gold Pass", 99m, "gold-2027"));
        await h.CheckAsync(2, Pass("2026 Gold Pass", 120m, "gold-2026"), Pass("2027 Gold Pass", 109m, "gold-2027"));

        Assert.Single(h.History(Key("gold-2026").ToString()));
        Assert.Equal(new[] { 99m, 109m }, h.History(Key("gold-2027").ToString()).Select(i => i.Amount));
    }

    [Fact]
    public async Task SameOfficialIdReusedForTheNextPassYear_BecomesADistinctProductAndTimeline()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass("2026 Gold Pass", 120m));

        var result = await h.CheckAsync(2, Pass("2027 Gold Pass", 99m));

        var yearKey = Key("gold-pass").WithPassYear(2027).ToString();
        Assert.Contains(result.Products, p => p.ProductKey == yearKey && p.Outcome == ReconciliationOutcome.Added);
        Assert.Equal("2026 Gold Pass", (await h.GetAsync(Key("gold-pass").ToString()))!.DisplayName); // never renamed into 2027
        Assert.Equal(new[] { 120m }, h.History(Key("gold-pass").ToString()).Select(i => i.Amount));
        Assert.Equal(new[] { 99m }, h.History(yearKey).Select(i => i.Amount));
    }

    [Fact]
    public async Task PreviousYearPass_RetiresAfterTwoConfirmedAbsences_AndKeepsItsHistory()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass("2026 Gold Pass", 120m));
        await h.CheckAsync(2, Pass("2027 Gold Pass", 99m));
        await h.CheckAsync(3, Pass("2027 Gold Pass", 99m));

        var old = await h.GetAsync(Key("gold-pass").ToString());

        Assert.Equal(ProductLifecycleState.Archived, old!.LifecycleState);
        Assert.Single(h.History(Key("gold-pass").ToString()));
        Assert.Equal(ProductLifecycleState.Active, (await h.GetAsync(Key("gold-pass").WithPassYear(2027).ToString()))!.LifecycleState);
    }

    [Fact]
    public async Task RepeatedRefreshes_NeverDuplicateTheYearProductOrItsHistory()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass("2026 Gold Pass", 120m));
        for (var day = 2; day <= 5; day++)
        {
            await h.CheckAsync(day, Pass("2027 Gold Pass", 99m));
        }

        var all = await h.Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, null);
        Assert.Equal(2, all.Count);
        Assert.Single(h.History(Key("gold-pass").WithPassYear(2027).ToString()));
    }

    [Fact]
    public async Task PassSoldInThePriorCalendarYear_KeepsItsOwnPassYearNotTheObservationYear()
    {
        var h = new Harness();

        // Observed in October 2026; it is a 2027 pass.
        await h.CheckAsync(1, Pass("2027 Gold Pass", 99m));

        var record = await h.GetAsync(Key("gold-pass").ToString());
        Assert.Equal(2027, PassValidityYear.FromDisplayName(record!.DisplayName));
        Assert.Equal(2026, Day(1).Year);
    }

    [Fact]
    public async Task UnknownPassYear_NeverSplitsAndIsNeverDefaultedToTheCurrentYear()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass("2027 Gold Pass", 99m));

        await h.CheckAsync(2, Pass("Gold Pass", 99m));

        var all = await h.Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, null);
        var only = Assert.Single(all);
        Assert.Equal("Gold Pass", only.DisplayName);
        Assert.Null(PassValidityYear.FromDisplayName("Gold Pass"));
    }

    [Fact]
    public async Task RenameWithinTheSamePassYear_KeepsIdentityAndHistory()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass("2027 Gold Pass", 99m));

        await h.CheckAsync(2, Pass("2027 Gold Season Pass", 99m));

        var only = Assert.Single(await h.Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, null));
        Assert.Equal(Key("gold-pass").ToString(), only.ProductKey);
        Assert.Single(h.History(only.ProductKey));
    }

    [Fact]
    public async Task ReactivationOfTheYearProduct_ReusesItsStableIdentity()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass("2026 Gold Pass", 120m));
        await h.CheckAsync(2, Pass("2027 Gold Pass", 99m));
        await h.CheckAsync(3);
        await h.CheckAsync(4); // both retired after two successful absences

        var result = await h.CheckAsync(5, Pass("2027 Gold Pass", 99m));

        var yearKey = Key("gold-pass").WithPassYear(2027).ToString();
        Assert.Contains(result.Products, p => p.ProductKey == yearKey && p.Outcome == ReconciliationOutcome.Reactivated);
        Assert.Equal(2, (await h.Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, null)).Count);
    }

    [Fact]
    public async Task NonPassCategories_AreNeverSplitByYear()
    {
        var h = new Harness();
        var promo = (string name) => new ObservedProduct(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "promo"), name, 50m, "$50", null, null, false, null);
        await h.Service.ReconcileAsync(SourceCategory.Promotions, Verified(Day(1)), [promo("2026 Holiday Bundle")]);

        await h.Service.ReconcileAsync(SourceCategory.Promotions, Verified(Day(2)), [promo("2027 Holiday Bundle")]);

        Assert.Single(await h.Catalog.GetByCategoryAsync(SourceCategory.Promotions, null));
    }
}
