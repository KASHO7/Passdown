using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using static Carowinds.Passdown.Application.Tests.PersonalSync.Phase26OfficialPriceObservationTests;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>
/// RC.9 defect: one Season Pass known under a legacy CMS-id key on one computer and under its
/// pass-offer key on another. The read-time fold joins them only when unambiguous.
/// </summary>
public class ImportedPassIdentityFoldingTests
{
    private const string LegacyGold = "SeasonPasses:id:550e3db6-5bad-45f8-8404-5d608c367617";
    private const string OtherLegacyGold = "SeasonPasses:id:00000000-1111-2222-3333-444444444444";
    private const string YearlessLegacyGold = "SeasonPasses:name:carowinds season passes|gold pass";
    private const string LegacyPrestige = "SeasonPasses:id:5de73366-26f8-4c9d-8983-407dc10132ea";

    private static readonly Guid DeviceA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Aug22 = new(2026, 8, 22, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Oct22 = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

    private static CatalogProductRecord Record(string key, string name, SourceCategory category = SourceCategory.SeasonPasses) => new(
        key, category, name, ProductLifecycleState.Active, Aug22, Oct22, Oct22, 0, null, null, 99m, "$99", null, null, false, null,
        new SourceEvidence(SourceName, new Uri("https://www.sixflags.com/carowinds/memberships-and-season-passes"), Oct22, RetrievalStatus.Verified, "Checked.", RetrievalStrategy.Html));

    private static OfficialPriceObservation Observed(string key, int sequence, decimal amount, string name) =>
        OfficialPriceObservation.FromLocalInterval(Interval(key, sequence, amount, Aug22.AddDays(sequence), Aug22.AddDays(sequence + 1), PriceKind.Sale, name), DeviceA)!;

    private static Dictionary<string, List<OfficialPriceObservation>> Imported(params OfficialPriceObservation[] observations) =>
        observations.GroupBy(o => o.ProductKey).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    [Fact]
    public void LegacyImport_FoldsIntoThePassOfferCatalogProduct()
    {
        var folded = ImportedPassIdentityFolding.Resolve(
            [Record(GoldPass, "Gold Season Pass — 2027")],
            Imported(Observed(LegacyGold, 1, 89m, "2027 Gold Pass"), Observed(LegacyGold, 2, 99m, "Gold Season Pass — 2027")));

        Assert.Equal(GoldPass, Assert.Single(folded, pair => pair.Key == LegacyGold).Value);
    }

    [Fact]
    public void PassOfferImport_FoldsIntoTheLegacyCatalogProduct()
    {
        var folded = ImportedPassIdentityFolding.Resolve(
            [Record(LegacyGold, "Gold Season Pass — 2027")],
            Imported(Observed(GoldPass, 1, 99m, "Gold Season Pass — 2027")));

        Assert.Equal(LegacyGold, folded[GoldPass]);
    }

    [Fact]
    public void SameKeyOnBothComputers_NeedsNoFold()
    {
        var folded = ImportedPassIdentityFolding.Resolve(
            [Record(GoldPass, "Gold Season Pass — 2027")], Imported(Observed(GoldPass, 1, 99m, "Gold Season Pass — 2027")));

        Assert.Empty(folded);
    }

    [Fact]
    public void YearlessLegacyGoldPass_IsNeverFolded()
    {
        var folded = ImportedPassIdentityFolding.Resolve(
            [Record(GoldPass, "Gold Season Pass — 2027")], Imported(Observed(YearlessLegacyGold, 1, 89m, "Gold Pass")));

        Assert.Empty(folded);
    }

    [Fact]
    public void LegacyImportNamingAnotherTierOrYear_IsNeverFolded()
    {
        var folded = ImportedPassIdentityFolding.Resolve(
            [Record(GoldPass, "Gold Season Pass — 2027")],
            Imported(Observed(LegacyPrestige, 1, 229m, "2027 Prestige Pass"), Observed(LegacyGold, 1, 89m, "2026 Gold Pass")));

        Assert.Empty(folded);
    }

    [Fact]
    public void LegacyImportWithDisagreeingNames_IsNeverFolded()
    {
        var folded = ImportedPassIdentityFolding.Resolve(
            [Record(GoldPass, "Gold Season Pass — 2027")],
            Imported(Observed(LegacyGold, 1, 89m, "2027 Gold Pass"), Observed(LegacyGold, 2, 99m, "2028 Gold Pass")));

        Assert.Empty(folded);
    }

    [Fact]
    public void TwoLegacyKeysForOneOffer_AreAmbiguous_AndNothingIsFolded()
    {
        var folded = ImportedPassIdentityFolding.Resolve(
            [Record(GoldPass, "Gold Season Pass — 2027")],
            Imported(Observed(LegacyGold, 1, 89m, "2027 Gold Pass"), Observed(OtherLegacyGold, 1, 89m, "2027 Gold Pass")));

        Assert.Empty(folded);
    }

    [Fact]
    public void TwoCatalogProductsForOneOffer_AreAmbiguous_AndNothingIsFolded()
    {
        var folded = ImportedPassIdentityFolding.Resolve(
            [Record(GoldPass, "Gold Season Pass — 2027"), Record(LegacyGold, "2027 Gold Pass")],
            Imported(Observed(OtherLegacyGold, 1, 89m, "2027 Gold Pass")));

        Assert.Empty(folded);
    }

    [Theory]
    [InlineData(GoldRenewal, "Gold Season Pass Renewal — 2027")]
    [InlineData(GoldMembership, "Gold Membership")]
    [InlineData(GoldInitiationFee, "Gold Membership Initiation Fee")]
    public void RenewalMembershipAndInitiationFee_NeverAdoptTheLegacyPass(string catalogKey, string catalogName)
    {
        var folded = ImportedPassIdentityFolding.Resolve(
            [Record(catalogKey, catalogName)], Imported(Observed(LegacyGold, 1, 89m, "2027 Gold Pass")));

        Assert.Empty(folded);
    }

    [Fact]
    public void NoCatalogProductForTheOffer_LeavesTheImportAsItsOwnProduct()
    {
        var folded = ImportedPassIdentityFolding.Resolve([], Imported(Observed(LegacyGold, 1, 89m, "2027 Gold Pass")));

        Assert.Empty(folded);
    }
}
