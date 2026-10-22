using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Associate;

/// <summary>
/// Phase 25: composition of the official Memberships &amp; Season Passes offers into Associate rows —
/// offer identity and unit on each row, renewal/initiation amounts folded onto their own product (never
/// into the headline price), Memberships as their own group, partial-check stale labeling, legacy rows,
/// and the price-history family split. Values are fixtures, not production constants.
/// </summary>
public class Phase25PassOfferCompositionTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 22);
    private const string SourceName = "Carowinds Season Passes";

    private static readonly PassOffer GoldPass = PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027);
    private static readonly PassOffer GoldRenewal = PassOffer.SeasonPass("Gold", PassPriceType.Renewal, 2027);
    private static readonly PassOffer GoldMembership = PassOffer.Membership("Gold", PassPriceType.MonthlyRate);
    private static readonly PassOffer GoldInitiation = PassOffer.Membership("Gold", PassPriceType.InitiationFee);
    private static readonly PassOffer SilverPass = PassOffer.SeasonPass("Silver", PassPriceType.NewPurchase, 2027);
    private static readonly PassOffer SilverMembership = PassOffer.Membership("Silver", PassPriceType.MonthlyRate);

    private static SourceEvidence Evidence(DateTimeOffset at, string sourceName = SourceName) =>
        new(sourceName, new Uri("https://www.sixflags.com/carowinds/memberships-and-season-passes"), at, RetrievalStatus.Verified,
            "Verified 2 Season Passes. Official note: *Plus processing fee of up to $9.99 & applicable taxes.", RetrievalStrategy.Html);

    private static Task SeedAsync(
        InMemoryCatalogProductRepository catalog, CatalogProductKey key, string name, decimal? price, string? display,
        decimal? comparison = null, string? qualifier = null, DateTimeOffset? verifiedAt = null,
        ProductLifecycleState state = ProductLifecycleState.Active, string sourceName = SourceName)
    {
        var at = verifiedAt ?? FixedNow;
        return catalog.UpsertAsync(new CatalogProductUpsert(
            key, name, state, at, at, at,
            ConsecutiveConfirmedAbsenceCount: 0, ArchivedAt: null, ArchivedReason: null,
            CurrentPriceAmount: price, CurrentDisplayPrice: display, ComparisonPriceAmount: comparison, PriceQualifier: qualifier,
            DateSpecific: false, ApplicableDate: null, LastEvidence: Evidence(at, sourceName)));
    }

    private static async Task<InMemoryCatalogProductRepository> PublishedCatalogAsync()
    {
        var catalog = new InMemoryCatalogProductRepository();
        await SeedAsync(catalog, SilverPass.ToKey(), SilverPass.DisplayName, 79m, "$79", 109m);
        await SeedAsync(catalog, GoldPass.ToKey(), GoldPass.DisplayName, 99m, "$99", 115m);
        await SeedAsync(catalog, GoldRenewal.ToKey(), GoldRenewal.DisplayName, 89m, "$89");
        await SeedAsync(catalog, SilverMembership.ToKey(), SilverMembership.DisplayName, null, "Not Available");
        await SeedAsync(catalog, GoldMembership.ToKey(), GoldMembership.DisplayName, 7m, "$7/mo", qualifier: "Auto-renews After 12mo");
        await SeedAsync(catalog, GoldInitiation.ToKey(), GoldInitiation.DisplayName, 20m, "$20", qualifier: "$20 initiation fee due today");
        return catalog;
    }

    private static async Task<AssociateWorkspace> LoadAsync(InMemoryCatalogProductRepository catalog)
    {
        var clock = new FakeClock(FixedNow);
        var service = new AssociatePassdownService(
            new InMemoryAssociateDraftRepository(), new InMemoryDatedResultRepository(), catalog, new FakeRefreshOrchestrator(),
            new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock), clock);
        return await service.LoadAssociateDateAsync(Today);
    }

    private static AssociateProduct Row(AssociateWorkspace workspace, PassOffer offer) =>
        Assert.Single(workspace.Products, p => offer.Equals(p.PassOffer));

    [Fact]
    public async Task SeasonPassRow_CarriesItsOneTimePriceReferenceAndFoldedRenewal()
    {
        var workspace = await LoadAsync(await PublishedCatalogAsync());

        var gold = Row(workspace, GoldPass);
        Assert.Equal(99m, gold.OfficialPriceAmount);
        Assert.Equal(115m, gold.ComparisonPriceAmount);
        Assert.Equal(89m, gold.RenewalPriceAmount);
        Assert.Equal(PassPriceUnit.OneTime, gold.PriceUnit);
        Assert.True(gold.IsPassdownExportable);
        Assert.Null(Row(workspace, SilverPass).RenewalPriceAmount); // none published → unknown, not invented
        Assert.DoesNotContain(workspace.Products, p => GoldRenewal.Equals(p.PassOffer)); // folded, not a second row
    }

    [Fact]
    public async Task MembershipRow_IsMonthlyWithItsInitiationFeeSeparate_AndNotExportable()
    {
        var workspace = await LoadAsync(await PublishedCatalogAsync());

        var membership = Row(workspace, GoldMembership);
        Assert.Equal(7m, membership.OfficialPriceAmount);
        Assert.Equal(PassPriceUnit.PerMonth, membership.PriceUnit);
        Assert.Equal(20m, membership.InitiationFeeAmount);
        Assert.Equal("Auto-renews After 12mo", membership.PriceQualifier);
        Assert.False(membership.IsPassdownExportable);
        Assert.DoesNotContain(workspace.Products, p => GoldInitiation.Equals(p.PassOffer));
    }

    [Fact]
    public async Task UnavailableSilverMembership_IsAnUnpricedOption()
    {
        var workspace = await LoadAsync(await PublishedCatalogAsync());

        var silver = Row(workspace, SilverMembership);
        Assert.True(silver.IsUnavailablePassOption);
        Assert.Null(silver.OfficialPriceAmount);
        Assert.Null(silver.InitiationFeeAmount);
    }

    [Fact]
    public async Task Sections_SeparateSeasonPassesFromMemberships_WithMembershipsDirectlyAfter()
    {
        var workspace = await LoadAsync(await PublishedCatalogAsync());

        var resolved = ProductDisplaySectionResolver.ResolveAll(workspace.Products.Where(p => !p.IsCustom)).ToList();
        Assert.Equal(
            ["Gold Season Pass — 2027", "Silver Season Pass — 2027"],
            resolved.Where(e => e.Section == ProductDisplaySection.SeasonPasses).Select(e => e.Product.DisplayName).Order());
        Assert.Equal(
            ["Gold Membership", "Silver Membership"],
            resolved.Where(e => e.Section == ProductDisplaySection.Memberships).Select(e => e.Product.DisplayName).Order());

        var order = Enum.GetValues<ProductDisplaySection>().OrderBy(ProductDisplaySectionResolver.DisplayOrder).ToList();
        Assert.Equal(order.IndexOf(ProductDisplaySection.SeasonPasses) + 1, order.IndexOf(ProductDisplaySection.Memberships));
    }

    [Fact]
    public async Task AnOfferThePartialCheckCouldNotVerify_IsLabeledStaleWithItsOwnVerifiedTime()
    {
        var catalog = await PublishedCatalogAsync();
        var older = FixedNow.AddDays(-2);
        await SeedAsync(catalog, GoldMembership.ToKey(), GoldMembership.DisplayName, 7m, "$7/mo", qualifier: "Auto-renews After 12mo", verifiedAt: older);

        var workspace = await LoadAsync(catalog);

        var membership = Row(workspace, GoldMembership);
        Assert.Equal(AssociateVerificationState.Stale, membership.Verification.State);
        Assert.Equal(older, membership.Verification.LastVerifiedAt);
        Assert.Equal(TimeSpan.FromDays(2), membership.Verification.StaleAge);
        Assert.Equal(AssociateVerificationState.Verified, Row(workspace, GoldPass).Verification.State);
    }

    [Fact]
    public async Task OtherCatalogCategories_KeepTheirEstablishedVerification()
    {
        var catalog = new InMemoryCatalogProductRepository();
        await catalog.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.AddOns, "a"), "All Season Fast Lane", ProductLifecycleState.Active,
            FixedNow.AddDays(-3), FixedNow.AddDays(-3), FixedNow.AddDays(-3), 0, null, null, 499m, "$499", null, null, false, null,
            Evidence(FixedNow.AddDays(-3), "Carowinds Season Pass Add-Ons")));
        await catalog.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.AddOns, "b"), "Season FunPix", ProductLifecycleState.Active,
            FixedNow, FixedNow, FixedNow, 0, null, null, 99m, "$99", null, null, false, null,
            Evidence(FixedNow, "Carowinds Season Pass Add-Ons")));

        var workspace = await LoadAsync(catalog);

        Assert.All(workspace.Products, p => Assert.Equal(AssociateVerificationState.Verified, p.Verification.State));
        Assert.All(workspace.Products, p => Assert.Null(p.PassOffer));
    }

    [Fact]
    public async Task LegacyKeyedPass_StillResolvesAsTheSeasonPass_AndReceivesItsRenewal()
    {
        var catalog = new InMemoryCatalogProductRepository();
        await SeedAsync(catalog, CatalogProductKey.Parse("SeasonPasses:id:550e3db6-5bad-45f8-8404-5d608c367617"), "Gold Season Pass — 2027", 99m, "$99", 115m);
        await SeedAsync(catalog, GoldRenewal.ToKey(), GoldRenewal.DisplayName, 89m, "$89");

        var workspace = await LoadAsync(catalog);

        var gold = Assert.Single(workspace.Products);
        Assert.Equal(GoldPass, gold.PassOffer);
        Assert.Equal(89m, gold.RenewalPriceAmount);
        Assert.Equal("catalog|SeasonPasses:id:550e3db6-5bad-45f8-8404-5d608c367617", gold.ProductIdentity); // draft overrides keep working
    }

    [Fact]
    public async Task PromotedLegacyNamedCard_IsStillDeduplicatedAgainstTheNativeSeasonPass()
    {
        var catalog = await PublishedCatalogAsync();
        await catalog.UpsertAsync(new CatalogProductUpsert(
            CatalogProductKey.FromOfficialId(SourceCategory.Promotions, "9d031bb4-5b2f-46ea-8da0-08d826e7bd11"), "2027 Gold Pass",
            ProductLifecycleState.Active, FixedNow, FixedNow, FixedNow, 0, null, null, 99m, "$99", 115m, null, false, null,
            Evidence(FixedNow, "Carowinds Special Offers")));

        var workspace = await LoadAsync(catalog);

        var seasonPasses = ProductDisplaySectionResolver.ResolveAll(workspace.Products)
            .Where(e => e.Section == ProductDisplaySection.SeasonPasses)
            .ToList();
        var gold = Assert.Single(seasonPasses, e => e.Product.DisplayName.Contains("Gold", StringComparison.Ordinal));
        Assert.Equal(SourceCategory.SeasonPasses, gold.Product.Category);
    }

    [Fact]
    public void FoldCompanions_KeepsACompanionWhoseParentIsAbsent()
    {
        var renewal = new AssociateProduct(
            "catalog|r", GoldRenewal.DisplayName, SourceCategory.SeasonPasses, false, 89m, "$89", null, null, false, null,
            AssociateVerificationInfo.NotCheckedYet, true, null) { PassOffer = GoldRenewal };
        var products = new List<AssociateProduct> { renewal };

        PassOfferComposition.FoldCompanions(products);

        Assert.Equal([renewal], products);
        Assert.False(products[0].IsPassdownExportable); // still never printed
    }

    [Theory]
    [InlineData("SeasonPasses:pass-offer:carowinds|gold|season-pass|new-purchase|2027", "Gold Season Pass — 2027", PriceHistoryFamily.SeasonPass)]
    [InlineData("SeasonPasses:pass-offer:carowinds|gold|season-pass|renewal|2027", "Gold Season Pass Renewal — 2027", PriceHistoryFamily.SeasonPassRenewal)]
    [InlineData("SeasonPasses:pass-offer:carowinds|gold|membership|monthly", "Gold Membership", PriceHistoryFamily.Membership)]
    [InlineData("SeasonPasses:pass-offer:carowinds|gold|membership|initiation-fee", "Gold Membership Initiation Fee", PriceHistoryFamily.MembershipInitiationFee)]
    [InlineData("SeasonPasses:id:550e3db6-5bad-45f8-8404-5d608c367617", "2027 Gold Pass", PriceHistoryFamily.SeasonPass)]
    public void PriceHistoryFamilies_FollowTheOfferType(string key, string name, PriceHistoryFamily expected)
    {
        Assert.Equal(expected, PriceHistoryTrackingPolicy.Classify(SourceCategory.SeasonPasses, SourceName, name, dateSpecific: false, key));
        Assert.Equal(expected, PriceHistoryTrackingPolicy.ClassifyStoredProduct(SourceCategory.SeasonPasses, SourceName, name, false, hasRecordedHistory: true, key));
    }

    [Fact]
    public void PriceHistoryUnits_AreMonthlyOnlyForMemberships()
    {
        Assert.Equal(PassPriceUnit.PerMonth, PriceHistoryTrackingPolicy.UnitOf(PriceHistoryFamily.Membership));
        Assert.All(
            Enum.GetValues<PriceHistoryFamily>().Where(f => f != PriceHistoryFamily.Membership),
            family => Assert.Equal(PassPriceUnit.OneTime, PriceHistoryTrackingPolicy.UnitOf(family)));
    }

    [Fact]
    public async Task PriceHistoryList_SkipsAnUnpricedUnavailableOption_ButListsEveryPricedOffer()
    {
        var catalog = await PublishedCatalogAsync();
        var service = new SeasonPriceHistoryService(catalog, new InMemorySeasonPriceHistoryRepository());

        var products = await service.ListTrackedProductsAsync();

        Assert.DoesNotContain(products, p => p.ProductKey == SilverMembership.ToKey().ToString());
        Assert.Contains(products, p => p.ProductKey == GoldMembership.ToKey().ToString() && p.Family == PriceHistoryFamily.Membership && p.Unit == PassPriceUnit.PerMonth);
        Assert.Contains(products, p => p.ProductKey == GoldRenewal.ToKey().ToString() && p.Family == PriceHistoryFamily.SeasonPassRenewal);
        Assert.Contains(products, p => p.ProductKey == GoldInitiation.ToKey().ToString() && p.Family == PriceHistoryFamily.MembershipInitiationFee);
    }
}
