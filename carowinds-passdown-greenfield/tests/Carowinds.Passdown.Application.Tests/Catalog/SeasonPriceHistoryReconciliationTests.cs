using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>
/// Phase 20A: season price history, driven end to end through the real
/// <see cref="ProductReconciliationService"/> + <see cref="SeasonPriceHistoryRecorder"/> — the only
/// path that writes history (successful official catalog checks).
/// </summary>
public class SeasonPriceHistoryReconciliationTests
{
    private const string SeasonPassesSource = "Carowinds Season Passes";
    private static readonly DateTimeOffset Sep1 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Day(int n) => Sep1.AddDays(n - 1);

    private static SourceEvidence Verified(DateTimeOffset at, string sourceName = SeasonPassesSource) => new(
        sourceName, new Uri("https://www.sixflags.com/carowinds/season-passes"), at, RetrievalStatus.Verified,
        "Checked.", RetrievalStrategy.Html);

    private static ObservedProduct Pass(decimal? price, string? qualifier = null, decimal? comparison = null, string name = "2027 Gold Pass", string id = "gold-pass") => new(
        CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, id),
        name, price, price is null ? null : $"${price}", comparison, qualifier, DateSpecific: false, ApplicableDate: null);

    private sealed class Harness
    {
        public InMemoryCatalogProductRepository Catalog { get; } = new();
        public InMemoryProductHistoryRepository ProductHistory { get; } = new();
        public InMemorySeasonPriceHistoryRepository PriceHistory { get; } = new();
        public ProductReconciliationService Service { get; }

        public Harness()
        {
            Service = new ProductReconciliationService(
                Catalog, ProductHistory, new FakeUnitOfWorkFactory(), logger: null, new SeasonPriceHistoryRecorder(PriceHistory));
        }

        public Task<ReconciliationResult> CheckAsync(int day, params ObservedProduct[] products) =>
            Service.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(day)), products);

        public IReadOnlyList<SeasonPriceHistoryInterval> History(string id = "gold-pass") =>
            PriceHistory.All.Where(i => i.ProductKey == CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, id).ToString())
                .OrderBy(i => i.Sequence).ToList();
    }

    [Fact]
    public async Task FirstObservation_CreatesOneBaselineState()
    {
        var h = new Harness();

        var result = await h.CheckAsync(1, Pass(99m));

        var interval = Assert.Single(h.History());
        Assert.Equal(1, interval.Sequence);
        Assert.Equal(99m, interval.Amount);
        Assert.Equal(PriceKind.StandardCatalog, interval.PriceKind);
        Assert.Equal(Day(1), interval.FirstObservedAt);
        Assert.Equal(Day(1), interval.LastConfirmedAt);
        Assert.Equal(SeasonPriceHistoryOrigin.Observed, interval.Origin);
        Assert.Equal(SeasonPassesSource, interval.SourceName);
        Assert.Equal(SeasonPriceObservationOutcome.Baseline, result.Products.Single().PriceHistoryOutcome);
    }

    [Fact]
    public async Task SamePriceRepeated_StaysOneState_AndOnlyMovesLastConfirmed()
    {
        var h = new Harness();

        await h.CheckAsync(1, Pass(105m));
        await h.CheckAsync(2, Pass(105m));
        var third = await h.CheckAsync(3, Pass(105m));

        var interval = Assert.Single(h.History());
        Assert.Equal(Day(1), interval.FirstObservedAt);
        Assert.Equal(Day(3), interval.LastConfirmedAt);
        Assert.Equal(SeasonPriceObservationOutcome.Confirmed, third.Products.Single().PriceHistoryOutcome);
    }

    [Fact]
    public async Task ManualRefreshTwiceAtTheSameTime_DoesNotDuplicate()
    {
        var h = new Harness();

        await h.CheckAsync(1, Pass(105m));
        await h.CheckAsync(1, Pass(105m));

        Assert.Single(h.History());
    }

    [Fact]
    public async Task ExactIncrease_ClosesTheOldStateAndStartsANewOne()
    {
        var h = new Harness();

        await h.CheckAsync(1, Pass(99m));
        await h.CheckAsync(2, Pass(99m));
        var changed = await h.CheckAsync(3, Pass(105m));

        var history = h.History();
        Assert.Equal(2, history.Count);
        Assert.Equal((99m, Day(1), Day(2)), (history[0].Amount, history[0].FirstObservedAt, history[0].LastConfirmedAt));
        Assert.Equal((105m, Day(3), Day(3)), (history[1].Amount, history[1].FirstObservedAt, history[1].LastConfirmedAt));
        Assert.Equal(SeasonPriceObservationOutcome.PriceChanged, changed.Products.Single().PriceHistoryOutcome);
    }

    [Fact]
    public async Task ExactDecrease_IsRecorded()
    {
        var h = new Harness();

        await h.CheckAsync(1, Pass(105m));
        await h.CheckAsync(2, Pass(95m));

        Assert.Equal([105m, 95m], h.History().Select(i => i.Amount));
    }

    [Fact]
    public async Task TemporarySale_AndReturnToPriorPrice_AreThreeStates()
    {
        var h = new Harness();

        await h.CheckAsync(1, Pass(105m));
        await h.CheckAsync(2, Pass(95m));
        await h.CheckAsync(3, Pass(105m));

        var history = h.History();
        Assert.Equal([105m, 95m, 105m], history.Select(i => i.Amount));
        Assert.Equal([1, 2, 3], history.Select(i => i.Sequence));
        Assert.Equal(Day(1), history[0].LastConfirmedAt); // the old row is never rewritten by the later return
    }

    [Fact]
    public async Task ExactToFrom_SameAmount_IsASemanticChange()
    {
        var h = new Harness();

        await h.CheckAsync(1, Pass(105m));
        var result = await h.CheckAsync(2, Pass(105m, qualifier: "Starting From"));

        Assert.Equal([PriceKind.StandardCatalog, PriceKind.StartingFrom], h.History().Select(i => i.PriceKind));
        Assert.Equal(SeasonPriceObservationOutcome.SemanticsChanged, result.Products.Single().PriceHistoryOutcome);
    }

    [Fact]
    public async Task FromToExact_SameAmount_IsASemanticChange()
    {
        var h = new Harness();

        await h.CheckAsync(1, Pass(105m, qualifier: "Starting From"));
        await h.CheckAsync(2, Pass(105m));

        Assert.Equal([PriceKind.StartingFrom, PriceKind.StandardCatalog], h.History().Select(i => i.PriceKind));
    }

    [Fact]
    public async Task AmountAndSemanticChange_IsOneNewState()
    {
        var h = new Harness();

        await h.CheckAsync(1, Pass(99m));
        var result = await h.CheckAsync(2, Pass(105m, qualifier: "Starting From"));

        Assert.Equal(2, h.History().Count);
        Assert.Equal(SeasonPriceObservationOutcome.PriceAndSemanticsChanged, result.Products.Single().PriceHistoryOutcome);
    }

    [Fact]
    public async Task ComparisonPriceAppearing_ChangesTheKindToSale()
    {
        var h = new Harness();

        await h.CheckAsync(1, Pass(89m));
        await h.CheckAsync(2, Pass(89m, comparison: 115m));

        Assert.Equal([PriceKind.StandardCatalog, PriceKind.Sale], h.History().Select(i => i.PriceKind));
    }

    [Fact]
    public async Task FailedRetrieval_ChangesNothing()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass(105m));
        var registration = new CatalogSourceRegistration(
            new FakeOfficialDataSource<CatalogPayload>("season-passes.official", SourceCategory.SeasonPasses, (_, _) =>
                Task.FromResult(SourceResult<CatalogPayload>.WithoutPayload("season-passes.official", FakeEvidence.Error(Day(2))))),
            h.Service);

        var outcome = await registration.ExecuteAsync(requestedDate: null, CancellationToken.None);

        Assert.Equal(SourceOperationOutcomeKind.Failure, outcome.Outcome);
        var interval = Assert.Single(h.History());
        Assert.Equal((105m, Day(1)), (interval.Amount, interval.LastConfirmedAt));
    }

    [Fact]
    public async Task ObservationWithoutAPrice_IsNotAPriceState()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass(105m));

        await h.CheckAsync(2, Pass(null));

        var interval = Assert.Single(h.History());
        Assert.Equal(Day(1), interval.LastConfirmedAt);
        Assert.DoesNotContain(h.PriceHistory.All, i => i.Amount == 0m);
    }

    [Fact]
    public async Task MissingProduct_RetiresWithoutAnyPriceEvent_AndHistoryRemains()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass(105m));

        await h.CheckAsync(2); // candidate absence
        await h.CheckAsync(3); // retired

        var record = await h.Catalog.GetAsync(Pass(105m).Key);
        Assert.Equal(ProductLifecycleState.Archived, record!.LifecycleState);
        var interval = Assert.Single(h.History());
        Assert.Equal((105m, Day(1)), (interval.Amount, interval.LastConfirmedAt));
    }

    [Fact]
    public async Task ExplicitlyUnavailableProduct_IsNotAPriceEvent()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass(105m));

        await h.CheckAsync(2, Pass(0m) with { IsExplicitlyUnavailable = true });

        var interval = Assert.Single(h.History());
        Assert.Equal(105m, interval.Amount);
    }

    [Fact]
    public async Task SingleMissedCheck_DoesNotInterruptTheInterval()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass(105m));

        await h.CheckAsync(2); // pending confirmation only
        await h.CheckAsync(3, Pass(105m));

        var interval = Assert.Single(h.History());
        Assert.Equal(Day(3), interval.LastConfirmedAt);
    }

    [Fact]
    public async Task Reactivation_SamePrice_ContinuesTheSameIdentityWithANewInterval()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass(105m));
        await h.CheckAsync(2);
        await h.CheckAsync(3); // retired

        var result = await h.CheckAsync(10, Pass(105m));

        var history = h.History();
        Assert.Equal(2, history.Count);
        Assert.All(history, i => Assert.Equal(Pass(105m).Key.ToString(), i.ProductKey));
        Assert.Equal((105m, Day(10)), (history[1].Amount, history[1].FirstObservedAt));
        Assert.Equal(SeasonPriceObservationOutcome.Reappeared, result.Products.Single().PriceHistoryOutcome);
    }

    [Fact]
    public async Task Reactivation_NewPrice_IsAPriceChange()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass(105m));
        await h.CheckAsync(2);
        await h.CheckAsync(3);

        await h.CheckAsync(10, Pass(119m));

        Assert.Equal([105m, 119m], h.History().Select(i => i.Amount));
    }

    [Fact]
    public async Task Rename_WithTheSameOfficialId_KeepsOneHistory()
    {
        var h = new Harness();
        await h.CheckAsync(1, Pass(105m, name: "2027 Gold Pass"));

        await h.CheckAsync(2, Pass(105m, name: "Gold Season Pass 2027"));

        var interval = Assert.Single(h.History());
        Assert.Equal(Day(2), interval.LastConfirmedAt);
    }

    [Fact]
    public async Task HistoryWriteFailure_FailsTheReconciliation_SoNothingIsSilentlyInconsistent()
    {
        var h = new Harness();
        h.PriceHistory.FailInserts = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.CheckAsync(1, Pass(105m)));
        // Real cross-table rollback of the catalog row is proven against SQLite in Infrastructure
        // (SeasonPriceHistorySqliteTests.HistoryWriteFailure_RollsBackTheCatalogUpsert).
    }

    [Fact]
    public async Task SeasonPassAddOn_FromTheSeasonPassAddOnsPage_IsTracked()
    {
        var h = new Harness();
        var dining = new ObservedProduct(
            CatalogProductKey.FromOfficialId(SourceCategory.AddOns, "27AddonsDining"), "All Season Dining Access",
            199m, "$199", null, null, DateSpecific: false, ApplicableDate: null);

        await h.Service.ReconcileAsync(SourceCategory.AddOns, Verified(Day(1), PriceHistoryTrackingPolicy.SeasonPassAddOnsSourceName), [dining]);

        Assert.Single(h.PriceHistory.All);
    }

    [Fact]
    public async Task GeneralAddOn_IsNotTracked()
    {
        var h = new Harness();
        var photo = new ObservedProduct(
            CatalogProductKey.FromOfficialId(SourceCategory.AddOns, "2026SinglePhoto"), "Single Day FunPix",
            29.99m, "$29.99", null, null, DateSpecific: false, ApplicableDate: null);

        await h.Service.ReconcileAsync(SourceCategory.AddOns, Verified(Day(1), "Carowinds General Add-Ons"), [photo]);

        Assert.Empty(h.PriceHistory.All);
    }

    [Fact]
    public async Task PromotionsCard_IsNotTracked_EvenForAPassName()
    {
        var h = new Harness();
        var promo = new ObservedProduct(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "promo-gold"), "2027 Gold Pass",
            89m, "$89", 115m, "Sale Price", DateSpecific: false, ApplicableDate: null);

        await h.Service.ReconcileAsync(SourceCategory.Promotions, Verified(Day(1), "Carowinds Promotions"), [promo]);

        Assert.Empty(h.PriceHistory.All);
    }

    [Fact]
    public async Task WithoutARecorder_ReconciliationBehavesExactlyAsBefore()
    {
        var catalog = new InMemoryCatalogProductRepository();
        var uow = new FakeUnitOfWorkFactory();
        var service = new ProductReconciliationService(catalog, new InMemoryProductHistoryRepository(), uow);

        await service.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(1)), [Pass(105m)]);
        var unchanged = await service.ReconcileAsync(SourceCategory.SeasonPasses, Verified(Day(2)), [Pass(105m)]);

        Assert.Null(unchanged.Products.Single().PriceHistoryOutcome);
        Assert.Equal(1, uow.BeginCount); // only the "new product" history append opened a unit of work
    }
}
