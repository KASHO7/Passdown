using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>
/// Phase 31B: the 2027 Military Gold Pass through refresh → reconciliation → price history → the Price
/// History list. Failed checks never retire it; a stale "retired" record from the old misread is shown
/// as availability unconfirmed; it reactivates under the same identity with its history intact.
/// </summary>
public class Phase31BMilitaryAvailabilityTests
{
    private const string SourceName = "Carowinds Military & First Responder Tickets";
    private static readonly CatalogProductKey GoldKey = CatalogProductKey.FromOfficialId(SourceCategory.MilitaryFirstResponder, "FallMilitaryGoldSP2027");
    private static readonly CatalogProductKey TicketKey = CatalogProductKey.FromOfficialId(SourceCategory.MilitaryFirstResponder, "MilitaryWizard");

    private static DateTimeOffset Day(int day) => new(2026, 11, day, 9, 0, 0, TimeSpan.FromHours(-5));

    private static SourceEvidence Evidence(int day, RetrievalStatus status) =>
        new(SourceName, new Uri("https://www.sixflags.com/carowinds/military-discounts"), Day(day), status, status.ToString(), RetrievalStrategy.Html,
            requestedDate: null, technicalDetail: status == RetrievalStatus.Error ? "Simulated layout change." : null);

    private static ObservedProduct Gold(decimal price = 95m) => new(GoldKey, "2027 Military Gold Pass", price, $"${price}", null, "Only", DateSpecific: false, ApplicableDate: null);

    private static ObservedProduct Tickets() => new(TicketKey, "Military & First Responder Ticket", 40m, "$40", null, "Starting From", DateSpecific: false, ApplicableDate: null);

    private sealed class Harness
    {
        public InMemoryCatalogProductRepository Catalog { get; } = new();
        public InMemorySeasonPriceHistoryRepository PriceHistory { get; } = new();
        public InMemoryProductHistoryRepository ProductHistory { get; } = new();
        public ProductReconciliationService Reconciliation { get; }

        public Queue<SourceResult<CatalogPayload>> Results { get; } = new();

        public Harness() =>
            Reconciliation = new ProductReconciliationService(Catalog, ProductHistory, new FakeUnitOfWorkFactory(), logger: null, new SeasonPriceHistoryRecorder(PriceHistory));

        public Task RefreshAsync()
        {
            var source = new FakeOfficialDataSource<CatalogPayload>("military-first-responder.official", SourceCategory.MilitaryFirstResponder, (_, _) => Task.FromResult(Results.Dequeue()));
            return new CatalogSourceRegistration(source, Reconciliation).ExecuteAsync(null, CancellationToken.None);
        }

        public void EnqueueVerified(int day, params ObservedProduct[] products) =>
            Results.Enqueue(SourceResult<CatalogPayload>.Success("military-first-responder.official", Evidence(day, RetrievalStatus.Verified), new CatalogPayload(products)));

        public void EnqueueError(int day) =>
            Results.Enqueue(SourceResult<CatalogPayload>.WithoutPayload("military-first-responder.official", Evidence(day, RetrievalStatus.Error)));

        public async Task<PriceHistoryProduct> ListedGoldAsync() =>
            Assert.Single(await new SeasonPriceHistoryService(Catalog, PriceHistory).ListTrackedProductsAsync(), p => p.ProductKey == GoldKey.ToString());
    }

    [Fact]
    public async Task FailedChecks_NeverRetireTheMilitaryGoldPass()
    {
        var h = new Harness();
        h.EnqueueVerified(1, Gold(), Tickets());
        h.EnqueueError(2);
        h.EnqueueError(3);
        h.EnqueueError(4);

        for (var i = 0; i < 4; i++)
        {
            await h.RefreshAsync();
        }

        var record = await h.Catalog.GetAsync(GoldKey);
        Assert.Equal(ProductLifecycleState.Active, record!.LifecycleState);
        var listed = await h.ListedGoldAsync();
        Assert.True(listed.IsActive);
        Assert.Equal(ProductAvailability.Available, listed.Availability.Availability);
    }

    [Fact]
    public async Task AStaleRetirementFromTheOldMisread_IsShownAsUnconfirmed_NotRetired()
    {
        var h = new Harness();
        h.EnqueueVerified(1, Gold(), Tickets());
        h.EnqueueVerified(2); // the old parser: "verified", zero offers
        h.EnqueueVerified(3);
        for (var i = 0; i < 3; i++)
        {
            await h.RefreshAsync();
        }

        Assert.Equal(ProductLifecycleState.Archived, (await h.Catalog.GetAsync(GoldKey))!.LifecycleState);

        var listed = await h.ListedGoldAsync();
        Assert.True(listed.IsActive); // still listed without "Include retired products"
        Assert.Equal(ProductAvailability.Unconfirmed, listed.Availability.Availability);
        Assert.Equal("Availability unconfirmed", listed.Availability.Label);
    }

    [Fact]
    public async Task OnceReadAgain_ThePassReactivates_WithTheSameIdentityAndItsEarlierPriceHistory()
    {
        var h = new Harness();
        h.EnqueueVerified(1, Gold(89m), Tickets());
        h.EnqueueVerified(2);
        h.EnqueueVerified(3);
        h.EnqueueVerified(10, Gold(95m), Tickets());
        for (var i = 0; i < 4; i++)
        {
            await h.RefreshAsync();
        }

        var record = await h.Catalog.GetAsync(GoldKey);
        Assert.Equal(ProductLifecycleState.Active, record!.LifecycleState);
        Assert.Equal(Day(1), record.FirstSeenAt);

        var history = h.PriceHistory.All.Where(i => i.ProductKey == GoldKey.ToString()).OrderBy(i => i.Sequence).ToList();
        Assert.Equal([89m, 95m], history.Select(i => i.Amount));
        Assert.Equal(Day(1), history[0].FirstObservedAt);
        Assert.Equal(ProductAvailability.Available, (await h.ListedGoldAsync()).Availability.Availability);
    }

    [Fact]
    public async Task ATemporaryOmission_LeavesThePassUnconfirmed_ThenAvailable_WithoutAnyHistoryGap()
    {
        var h = new Harness();
        h.EnqueueVerified(1, Gold(), Tickets());
        h.EnqueueVerified(2, Tickets()); // card briefly missing from one successful check
        await h.RefreshAsync();
        await h.RefreshAsync();

        Assert.Equal(ProductAvailability.Unconfirmed, (await h.ListedGoldAsync()).Availability.Availability);

        h.EnqueueVerified(3, Gold(), Tickets());
        await h.RefreshAsync();

        Assert.Equal(ProductAvailability.Available, (await h.ListedGoldAsync()).Availability.Availability);
        Assert.Single(h.PriceHistory.All, i => i.ProductKey == GoldKey.ToString());
    }

    [Fact]
    public async Task AfterTheCorrectedRefresh_BothMilitaryProductsKeepTheirIdentities_AndHistoricalPricing()
    {
        var h = new Harness();
        h.EnqueueVerified(1, Gold(89m), Tickets());
        h.EnqueueVerified(2); // the old misread: "verified", zero offers
        h.EnqueueVerified(10, Gold(95m), new ObservedProduct(TicketKey, "Military & First Responder Ticket", 45m, "$45", null, "Starting From", DateSpecific: false, ApplicableDate: null));
        for (var i = 0; i < 3; i++)
        {
            await h.RefreshAsync();
        }

        foreach (var key in new[] { GoldKey, TicketKey })
        {
            var record = await h.Catalog.GetAsync(key);
            Assert.NotNull(record);
            Assert.Equal(ProductLifecycleState.Active, record!.LifecycleState);
            Assert.Equal(Day(1), record.FirstSeenAt);
        }

        Assert.Equal(2, (await h.Catalog.GetByCategoryAsync(SourceCategory.MilitaryFirstResponder)).Count); // no duplicate identities

        // The daily-admission Ticket is deliberately not in Season Price History (PriceHistoryTrackingPolicy);
        // its earlier official prices stay in the catalog's product history under the same key.
        Assert.DoesNotContain(h.PriceHistory.All, i => i.ProductKey == TicketKey.ToString());
        var tickets = await h.ProductHistory.GetHistoryAsync(TicketKey.ToString());
        Assert.Equal(40m, tickets[0].CurrentPriceAmount);
        Assert.Equal(Day(1), tickets[0].RecordedAt);
        Assert.Equal(45m, tickets[^1].CurrentPriceAmount);
        Assert.Equal(ProductLifecycleState.Active, tickets[^1].LifecycleState);

        var gold = h.PriceHistory.All.Where(i => i.ProductKey == GoldKey.ToString()).OrderBy(i => i.Sequence).ToList();
        Assert.Equal([89m, 95m], gold.Select(i => i.Amount));
    }
}
