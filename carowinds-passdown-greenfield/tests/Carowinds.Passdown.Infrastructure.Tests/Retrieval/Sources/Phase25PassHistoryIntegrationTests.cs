using System.Net;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.Retrieval.Sources;

/// <summary>
/// Phase 25 history integrity against real SQLite: an install that recorded Season Pass prices from
/// the pre-redesign page (legacy CMS-id keys "2027 Gold Pass"/"2027 Prestige Pass", exactly as the old
/// adapter stored them) then checks the redesigned page through the real adapter, registration and
/// reconciliation. Proves legacy rows and their history continue (no duplicate product, no rewritten
/// interval), Memberships/renewals/initiation fees get their own series, a partial check keeps
/// last-known-good values, and a failed check changes nothing.
/// </summary>
public class Phase25PassHistoryIntegrationTests : IDisposable
{
    private const string SourceName = "Carowinds Season Passes";

    private const string LegacyGoldKey = "SeasonPasses:id:550e3db6-5bad-45f8-8404-5d608c367617";
    private const string LegacyPrestigeKey = "SeasonPasses:id:5de73366-26f8-4c9d-8983-407dc10132ea";

    private static readonly DateTimeOffset Day1 = new(2026, 9, 1, 14, 0, 0, TimeSpan.Zero);

    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static DateTimeOffset Day(int n) => Day1.AddDays(n - 1);

    private SqliteCatalogProductRepository Catalog => new(_fixture.ConnectionFactory);

    private SqliteSeasonPriceHistoryRepository History => new(_fixture.ConnectionFactory);

    private ProductReconciliationService Reconciliation() => new(
        new SqliteCatalogProductRepository(_fixture.ConnectionFactory),
        new SqliteProductHistoryRepository(_fixture.ConnectionFactory),
        new SqliteUnitOfWorkFactory(_fixture.ConnectionFactory),
        logger: null,
        new SeasonPriceHistoryRecorder(new SqliteSeasonPriceHistoryRepository(_fixture.ConnectionFactory)));

    private static string Key(PassOffer offer) => offer.ToKey().ToString();

    /// <summary>What the pre-Phase 25 adapter recorded from the old season-passes page (fixtures/season-passes/published-page.html).</summary>
    private async Task SeedLegacyPassesAsync()
    {
        var evidence = new SourceEvidence(
            SourceName, new Uri("https://www.sixflags.com/carowinds/season-passes"), Day(1), RetrievalStatus.Verified, "2 season passes confirmed.", RetrievalStrategy.Html);
        await Reconciliation().ReconcileAsync(SourceCategory.SeasonPasses, evidence,
        [
            new ObservedProduct(CatalogProductKey.Parse(LegacyGoldKey), "2027 Gold Pass", 89m, "$89", 115m, "Sale Price", false, null),
            new ObservedProduct(CatalogProductKey.Parse(LegacyPrestigeKey), "2027 Prestige Pass", 229m, "$229", 275m, "Sale Price", false, null),
        ]);
    }

    /// <summary>One scheduled/Check Online style run of the real adapter through the real registration.</summary>
    private async Task<SourceOperationResult> CheckAsync(string fixture, DateTimeOffset at)
    {
        var source = new SeasonPassCatalogSource(
            new FakeRetrievalHttpClient(new RetrievalHttpResponse(HttpStatusCode.OK, "text/html", FixtureFile.ReadText("memberships-and-season-passes/" + fixture))),
            new FakeClock(at));
        return await new CatalogSourceRegistration(source, Reconciliation()).ExecuteAsync(requestedDate: null, CancellationToken.None);
    }

    [Fact]
    public async Task LegacyGoldAndPrestige_ContinueUnderTheirExistingIdentities_WithoutADuplicateProduct()
    {
        await SeedLegacyPassesAsync();

        var outcome = await CheckAsync("published-page.html", Day(2));

        Assert.Equal(SourceOperationOutcomeKind.Success, outcome.Outcome);
        var records = await Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, lifecycleState: null);
        var keys = records.Select(r => r.ProductKey).ToHashSet();
        Assert.Contains(LegacyGoldKey, keys);
        Assert.Contains(LegacyPrestigeKey, keys);
        Assert.DoesNotContain(Key(PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027)), keys);
        Assert.DoesNotContain(Key(PassOffer.SeasonPass("Prestige", PassPriceType.NewPurchase, 2027)), keys);
        Assert.Contains(Key(PassOffer.SeasonPass("Silver", PassPriceType.NewPurchase, 2027)), keys); // never existed before: new identity
        Assert.Equal(10, records.Count);
        Assert.All(records, r => Assert.Equal(ProductLifecycleState.Active, r.LifecycleState));

        var gold = records.Single(r => r.ProductKey == LegacyGoldKey);
        Assert.Equal("Gold Season Pass — 2027", gold.DisplayName);
        Assert.Equal(99m, gold.CurrentPriceAmount);
        Assert.Equal(PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027), PassOffer.Resolve(gold.Category, gold.ProductKey, gold.DisplayName));
    }

    [Fact]
    public async Task LegacyHistory_IsUnchanged_AndTheNewObservationAppendsOneInterval()
    {
        await SeedLegacyPassesAsync();
        var before = await History.GetHistoryAsync(LegacyGoldKey);

        await CheckAsync("published-page.html", Day(2));

        var gold = await History.GetHistoryAsync(LegacyGoldKey);
        Assert.Equal(2, gold.Count);
        Assert.Equal(before[0], gold[0]); // the old interval is byte-for-byte what it was
        Assert.Equal(89m, gold[0].Amount);
        Assert.Equal("2027 Gold Pass", gold[0].DisplayName);
        Assert.Equal(Day(1), gold[0].FirstObservedAt);
        Assert.Equal(99m, gold[1].Amount);
        Assert.Equal(PriceKind.Sale, gold[1].PriceKind); // a reference price is still shown → same price type as before
        Assert.Equal(Day(2), gold[1].FirstObservedAt);

        var prestige = await History.GetHistoryAsync(LegacyPrestigeKey);
        Assert.Equal([229m, 249m], prestige.Select(i => i.Amount));
    }

    [Fact]
    public async Task RepeatedChecksOfTheSamePage_ConfirmRatherThanAppend()
    {
        await SeedLegacyPassesAsync();
        await CheckAsync("published-page.html", Day(2));

        await CheckAsync("duplicated-offers-page.html", Day(3));
        await CheckAsync("reordered-page.html", Day(4));

        var gold = await History.GetHistoryAsync(LegacyGoldKey);
        Assert.Equal(2, gold.Count);
        Assert.Equal(Day(4), gold[1].LastConfirmedAt);
        Assert.Single(await History.GetHistoryAsync(Key(PassOffer.Membership("Gold", PassPriceType.MonthlyRate))));
        Assert.Equal(10, (await Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, lifecycleState: null)).Count);
    }

    [Fact]
    public async Task MembershipRenewalAndInitiationFee_EachHaveTheirOwnSeries_NeverMergedWithThePass()
    {
        await SeedLegacyPassesAsync();
        await CheckAsync("published-page.html", Day(2));

        var membership = Assert.Single(await History.GetHistoryAsync(Key(PassOffer.Membership("Gold", PassPriceType.MonthlyRate))));
        Assert.Equal(7m, membership.Amount);
        Assert.Equal("Gold Membership", membership.DisplayName);
        var fee = Assert.Single(await History.GetHistoryAsync(Key(PassOffer.Membership("Gold", PassPriceType.InitiationFee))));
        Assert.Equal(20m, fee.Amount);
        var renewal = Assert.Single(await History.GetHistoryAsync(Key(PassOffer.SeasonPass("Gold", PassPriceType.Renewal, 2027))));
        Assert.Equal(89m, renewal.Amount);
        Assert.Equal(Day(2), renewal.FirstObservedAt); // never backfilled into the past

        Assert.DoesNotContain(await History.GetHistoryAsync(LegacyGoldKey), i => i.Amount is 7m or 20m);
    }

    [Fact]
    public async Task UnavailableSilverMembership_IsListedWithoutAPrice_AndNeverRecordsAPriceState()
    {
        await CheckAsync("published-page.html", Day(2));

        var key = Key(PassOffer.Membership("Silver", PassPriceType.MonthlyRate));
        var record = await Catalog.GetAsync(CatalogProductKey.Parse(key));
        Assert.Equal(ProductLifecycleState.Active, record!.LifecycleState);
        Assert.Null(record.CurrentPriceAmount);
        Assert.Equal("Not Available", record.CurrentDisplayPrice);
        Assert.Empty(await History.GetHistoryAsync(key));
    }

    [Fact]
    public async Task PartialCheck_KeepsTheUnreadableOffersLastKnownGood_AndVerifiesTheRest()
    {
        await CheckAsync("published-page.html", Day(2));

        var outcome = await CheckAsync("missing-membership-price-page.html", Day(3));
        await CheckAsync("missing-membership-price-page.html", Day(4)); // a second partial check must not retire it either

        Assert.Equal(SourceOperationOutcomeKind.Success, outcome.Outcome);
        var membership = await Catalog.GetAsync(PassOffer.Membership("Gold", PassPriceType.MonthlyRate).ToKey());
        Assert.Equal(ProductLifecycleState.Active, membership!.LifecycleState);
        Assert.Equal(0, membership.ConsecutiveConfirmedAbsenceCount);
        Assert.Equal(7m, membership.CurrentPriceAmount);
        Assert.Equal(Day(2), membership.LastVerifiedAt);
        Assert.Equal(Day(2), Assert.Single(await History.GetHistoryAsync(Key(PassOffer.Membership("Gold", PassPriceType.MonthlyRate)))).LastConfirmedAt);

        var pass = await Catalog.GetAsync(PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027).ToKey());
        Assert.Equal(Day(4), pass!.LastVerifiedAt);
    }

    [Fact]
    public async Task FailedCheck_ChangesNothing()
    {
        await SeedLegacyPassesAsync();
        await CheckAsync("published-page.html", Day(2));
        var before = await Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, lifecycleState: null);

        var outcome = await CheckAsync("truncated-data-page.html", Day(3));

        Assert.Equal(SourceOperationOutcomeKind.Failure, outcome.Outcome);
        Assert.Equal(RetrievalStatus.Error, outcome.Status);
        var after = await Catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, lifecycleState: null);
        Assert.Equal(before.OrderBy(r => r.ProductKey), after.OrderBy(r => r.ProductKey));
        Assert.Equal(2, (await History.GetHistoryAsync(LegacyGoldKey)).Count);
    }

    [Fact]
    public async Task ATierGenuinelyGoneFromTheSource_RetiresThroughTheNormalTwoAbsenceRule_KeepingItsHistory()
    {
        await SeedLegacyPassesAsync();
        await CheckAsync("published-page.html", Day(2));

        await CheckAsync("missing-tier-page.html", Day(3));
        Assert.Equal(ProductLifecycleState.PendingConfirmation, (await Catalog.GetAsync(CatalogProductKey.Parse(LegacyPrestigeKey)))!.LifecycleState);
        await CheckAsync("missing-tier-page.html", Day(4));

        Assert.Equal(ProductLifecycleState.Archived, (await Catalog.GetAsync(CatalogProductKey.Parse(LegacyPrestigeKey)))!.LifecycleState);
        Assert.Equal([229m, 249m], (await History.GetHistoryAsync(LegacyPrestigeKey)).Select(i => i.Amount));
        Assert.Equal(ProductLifecycleState.Active, (await Catalog.GetAsync(CatalogProductKey.Parse(LegacyGoldKey)))!.LifecycleState);
    }

    [Fact]
    public async Task ADifferentYearsLegacyPass_IsNeverAdoptedByTheNewSeason()
    {
        var evidence = new SourceEvidence(
            SourceName, new Uri("https://www.sixflags.com/carowinds/season-passes"), Day(1), RetrievalStatus.Verified, "1 season pass confirmed.", RetrievalStrategy.Html);
        await Reconciliation().ReconcileAsync(SourceCategory.SeasonPasses, evidence,
            [new ObservedProduct(CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, "old-2026-gold"), "2026 Gold Pass", 79m, "$79", null, null, false, null)]);

        await CheckAsync("published-page.html", Day(2));

        Assert.NotNull(await Catalog.GetAsync(PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027).ToKey()));
        Assert.Equal([79m], (await History.GetHistoryAsync("SeasonPasses:id:old-2026-gold")).Select(i => i.Amount));
    }
}
