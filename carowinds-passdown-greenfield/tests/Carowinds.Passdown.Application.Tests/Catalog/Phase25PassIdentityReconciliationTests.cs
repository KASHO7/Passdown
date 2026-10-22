using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>
/// Phase 25 reconciliation rules for the redesigned pass page: legacy Season Pass identities are
/// adopted only for the same real product (tier + year, new-purchase price, one unambiguous match),
/// and unconfirmed (unreadable) offers are neither confirmed nor counted absent.
/// </summary>
public class Phase25PassIdentityReconciliationTests
{
    private const string SourceName = "Carowinds Season Passes";
    private static readonly DateTimeOffset Day1 = new(2026, 9, 1, 14, 0, 0, TimeSpan.Zero);

    private static readonly PassOffer GoldPass = PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027);
    private static readonly PassOffer GoldMembership = PassOffer.Membership("Gold", PassPriceType.MonthlyRate);

    private readonly InMemoryCatalogProductRepository _catalog = new();

    private ProductReconciliationService Service() => new(_catalog, new InMemoryProductHistoryRepository(), new FakeUnitOfWorkFactory());

    private static SourceEvidence Evidence(DateTimeOffset at) =>
        new(SourceName, new Uri("https://www.sixflags.com/carowinds/memberships-and-season-passes"), at, RetrievalStatus.Verified, "Verified.", RetrievalStrategy.Html);

    private static ObservedProduct Observed(CatalogProductKey key, string name, decimal? price) =>
        new(key, name, price, price is null ? null : $"${price}", null, null, false, null);

    private Task SeedLegacyAsync(params (string Id, string Name)[] passes) =>
        Service().ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1),
            passes.Select(p => Observed(CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, p.Id), p.Name, 89m)).ToList());

    private async Task<IReadOnlyDictionary<string, ProductLifecycleState>> StatesAsync() =>
        (await _catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, lifecycleState: null)).ToDictionary(r => r.ProductKey, r => r.LifecycleState);

    [Fact]
    public async Task AmbiguousLegacyMatch_AdoptsNothing()
    {
        await SeedLegacyAsync(("legacy-a", "2027 Gold Pass"), ("legacy-b", "2027 Gold Pass"));

        await Service().ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1.AddDays(1)), [Observed(GoldPass.ToKey(), GoldPass.DisplayName, 99m)]);

        var states = await StatesAsync();
        Assert.Equal(ProductLifecycleState.Active, states[GoldPass.ToKey().ToString()]);
        Assert.Equal(ProductLifecycleState.PendingConfirmation, states["SeasonPasses:id:legacy-a"]);
        Assert.Equal(ProductLifecycleState.PendingConfirmation, states["SeasonPasses:id:legacy-b"]);
    }

    [Fact]
    public async Task Membership_NeverAdoptsALegacySeasonPassIdentity()
    {
        await SeedLegacyAsync(("legacy-gold", "2027 Gold Pass"));

        await Service().ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1.AddDays(1)), [Observed(GoldMembership.ToKey(), GoldMembership.DisplayName, 7m)]);

        var records = await _catalog.GetByCategoryAsync(SourceCategory.SeasonPasses, lifecycleState: null);
        Assert.Equal("2027 Gold Pass", records.Single(r => r.ProductKey == "SeasonPasses:id:legacy-gold").DisplayName);
        Assert.Equal(89m, records.Single(r => r.ProductKey == "SeasonPasses:id:legacy-gold").CurrentPriceAmount);
        Assert.Equal(7m, records.Single(r => r.ProductKey == GoldMembership.ToKey().ToString()).CurrentPriceAmount);
    }

    [Fact]
    public async Task LegacyAdoption_IsScopedToTheSeasonPassesCategory()
    {
        await Service().ReconcileAsync(SourceCategory.Promotions, Evidence(Day1),
            [Observed(CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "promo-gold"), "2027 Gold Pass", 89m)]);

        await Service().ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1.AddDays(1)), [Observed(GoldPass.ToKey(), GoldPass.DisplayName, 99m)]);

        Assert.NotNull(await _catalog.GetAsync(GoldPass.ToKey()));
        Assert.Equal("2027 Gold Pass", (await _catalog.GetAsync(CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "promo-gold")))!.DisplayName);
    }

    [Fact]
    public async Task UnconfirmedOffer_UnderItsLegacyIdentity_IsNeitherConfirmedNorCountedAbsent()
    {
        await SeedLegacyAsync(("legacy-gold", "2027 Gold Pass"));
        var before = await _catalog.GetAsync(CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, "legacy-gold"));

        await Service().ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1.AddDays(1)),
            [Observed(GoldMembership.ToKey(), GoldMembership.DisplayName, 7m)],
            unconfirmedProductKeys: [GoldPass.ToKey()]);

        var after = await _catalog.GetAsync(CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, "legacy-gold"));
        Assert.Equal(before, after);
        Assert.Null(await _catalog.GetAsync(GoldPass.ToKey()));
    }

    [Fact]
    public async Task WithoutUnconfirmedKeys_AbsenceRulesAreUnchanged()
    {
        await SeedLegacyAsync(("legacy-silver", "2027 Silver Pass"));

        await Service().ReconcileAsync(SourceCategory.SeasonPasses, Evidence(Day1.AddDays(1)), [Observed(GoldMembership.ToKey(), GoldMembership.DisplayName, 7m)]);

        Assert.Equal(ProductLifecycleState.PendingConfirmation, (await StatesAsync())["SeasonPasses:id:legacy-silver"]);
    }
}
