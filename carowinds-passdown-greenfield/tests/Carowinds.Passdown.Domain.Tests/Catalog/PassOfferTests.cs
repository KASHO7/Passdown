using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Domain.Tests.Catalog;

/// <summary>Phase 25: pass-offer identity — Season Passes and Memberships never share a pricing identity.</summary>
public class PassOfferTests
{
    private static readonly PassOffer GoldPass = PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027);
    private static readonly PassOffer GoldRenewal = PassOffer.SeasonPass("Gold", PassPriceType.Renewal, 2027);
    private static readonly PassOffer GoldMembership = PassOffer.Membership("Gold", PassPriceType.MonthlyRate);
    private static readonly PassOffer GoldInitiation = PassOffer.Membership("Gold", PassPriceType.InitiationFee);
    private static readonly PassOffer PrestigePass = PassOffer.SeasonPass("Prestige", PassPriceType.NewPurchase, 2027);
    private static readonly PassOffer PrestigeMembership = PassOffer.Membership("Prestige", PassPriceType.MonthlyRate);

    [Fact]
    public void SameTier_SeasonPassAndMembership_HaveDifferentIdentities()
    {
        Assert.NotEqual(GoldPass, GoldMembership);
        Assert.NotEqual(GoldPass.ToKey(), GoldMembership.ToKey());
        Assert.NotEqual(PrestigePass.ToKey(), PrestigeMembership.ToKey());
    }

    [Fact]
    public void EveryPriceTypeOfATier_IsItsOwnIdentity()
    {
        var keys = new[] { GoldPass, GoldRenewal, GoldMembership, GoldInitiation }.Select(o => o.ToKey().ToString()).ToList();

        Assert.Equal(4, keys.Distinct().Count());
    }

    [Fact]
    public void Keys_AreDeterministicAndReadable()
    {
        Assert.Equal("SeasonPasses:pass-offer:carowinds|gold|season-pass|new-purchase|2027", GoldPass.ToKey().ToString());
        Assert.Equal("SeasonPasses:pass-offer:carowinds|gold|season-pass|renewal|2027", GoldRenewal.ToKey().ToString());
        Assert.Equal("SeasonPasses:pass-offer:carowinds|gold|membership|monthly", GoldMembership.ToKey().ToString());
        Assert.Equal("SeasonPasses:pass-offer:carowinds|gold|membership|initiation-fee", GoldInitiation.ToKey().ToString());
        Assert.Equal(SourceCategory.SeasonPasses, GoldPass.ToKey().Category);
    }

    [Fact]
    public void Keys_RoundTripThroughStorage()
    {
        foreach (var offer in new[] { GoldPass, GoldRenewal, GoldMembership, GoldInitiation, PassOffer.SeasonPass("Silver", PassPriceType.NewPurchase, null) })
        {
            var stored = CatalogProductKey.Parse(offer.ToKey().ToString()).ToString();
            Assert.Equal(offer, PassOffer.FromKey(stored));
        }
    }

    [Fact]
    public void TierComparison_IgnoresCase()
    {
        Assert.Equal(GoldPass, PassOffer.SeasonPass("GOLD", PassPriceType.NewPurchase, 2027));
        Assert.Equal(GoldPass.GetHashCode(), PassOffer.SeasonPass("gold", PassPriceType.NewPurchase, 2027).GetHashCode());
    }

    [Fact]
    public void SeasonYear_DistinguishesPasses()
    {
        Assert.NotEqual(GoldPass, PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2026));
        Assert.NotEqual(GoldPass, PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, null));
    }

    [Fact]
    public void Units_MonthlyOnlyForTheMembershipRate()
    {
        Assert.Equal(PassPriceUnit.OneTime, GoldPass.Unit);
        Assert.Equal(PassPriceUnit.OneTime, GoldRenewal.Unit);
        Assert.Equal(PassPriceUnit.PerMonth, GoldMembership.Unit);
        Assert.Equal(PassPriceUnit.OneTime, GoldInitiation.Unit);
    }

    [Fact]
    public void OnlyTheNewPurchasePrice_IsPartOfThePassdownExport()
    {
        Assert.True(GoldPass.IsPassdownExportable);
        Assert.False(GoldRenewal.IsPassdownExportable);
        Assert.False(GoldMembership.IsPassdownExportable);
        Assert.False(GoldInitiation.IsPassdownExportable);
    }

    [Fact]
    public void Parents_LinkCompanionPricesToTheirProduct()
    {
        Assert.Equal(GoldPass, GoldRenewal.Parent);
        Assert.Equal(GoldMembership, GoldInitiation.Parent);
        Assert.Null(GoldPass.Parent);
        Assert.Null(GoldMembership.Parent);
    }

    [Fact]
    public void DisplayNames_SayWhatTheProductIs()
    {
        Assert.Equal("Gold Season Pass — 2027", GoldPass.DisplayName);
        Assert.Equal("Gold Season Pass Renewal — 2027", GoldRenewal.DisplayName);
        Assert.Equal("Gold Membership", GoldMembership.DisplayName);
        Assert.Equal("Gold Membership Initiation Fee", GoldInitiation.DisplayName);
    }

    [Fact]
    public void InvalidCombinations_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => PassOffer.SeasonPass("Gold", PassPriceType.MonthlyRate, 2027));
        Assert.Throws<ArgumentException>(() => PassOffer.Membership("Gold", PassPriceType.Renewal));
        Assert.Throws<ArgumentException>(() => PassOffer.SeasonPass("Go|ld", PassPriceType.NewPurchase, 2027));
        Assert.Throws<ArgumentOutOfRangeException>(() => PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 1999));
    }

    [Theory]
    [InlineData("SeasonPasses:id:550e3db6-5bad-45f8-8404-5d608c367617")]
    [InlineData("Promotions:pass-offer:carowinds|gold|season-pass|new-purchase|2027")]
    [InlineData("SeasonPasses:pass-offer:other-park|gold|season-pass|new-purchase|2027")]
    [InlineData("SeasonPasses:pass-offer:carowinds|gold|membership|monthly|2027")]
    [InlineData("SeasonPasses:pass-offer:carowinds|gold|season-pass|monthly")]
    [InlineData("SeasonPasses:pass-offer:carowinds|gold")]
    [InlineData("")]
    public void FromKey_RejectsAnythingThatIsNotAPassOfferKey(string key) => Assert.Null(PassOffer.FromKey(key));

    [Theory]
    [InlineData("2027 Gold Pass", "Gold", 2027)]
    [InlineData("2027 Prestige Pass", "Prestige", 2027)]
    [InlineData("2026 silver pass", "Silver", 2026)]
    [InlineData("Gold Season Pass — 2027", "Gold", 2027)]
    [InlineData("Gold Season Pass", "Gold", null)]
    public void FromLegacyName_RecognizesOldAndNewPassNames(string name, string tier, int? year)
    {
        Assert.Equal(PassOffer.SeasonPass(tier, PassPriceType.NewPurchase, year), PassOffer.FromLegacyName(name));
    }

    [Theory]
    [InlineData("Military Gold Pass")]
    [InlineData("First Responder Gold Pass")]
    [InlineData("Gold Membership")]
    [InlineData("Gold Season Pass Renewal — 2027")]
    [InlineData("2027 Gold Pass Add-On")]
    [InlineData("All Season Fast Lane")]
    [InlineData("2026 Gold Pass — 2027")]
    [InlineData("Gold Passport")]
    public void FromLegacyName_NeverMatchesOtherProducts(string name) => Assert.Null(PassOffer.FromLegacyName(name));

    [Fact]
    public void Resolve_OnlyAppliesToTheSeasonPassesCategory()
    {
        Assert.Equal(GoldPass, PassOffer.Resolve(SourceCategory.SeasonPasses, "SeasonPasses:id:legacy", "2027 Gold Pass"));
        Assert.Equal(GoldMembership, PassOffer.Resolve(SourceCategory.SeasonPasses, GoldMembership.ToKey().ToString(), "anything"));
        Assert.Null(PassOffer.Resolve(SourceCategory.Promotions, "Promotions:id:x", "2027 Gold Pass"));
        Assert.Null(PassOffer.Resolve(null, null, "2027 Gold Pass"));
    }

    [Fact]
    public void AdvertisedPriceText_LabelsTheMonthlyUnit()
    {
        Assert.Equal("$7.00/month", AdvertisedPriceText.Format(7m, PriceKind.StandardCatalog, PassPriceUnit.PerMonth));
        Assert.Equal("$99.00", AdvertisedPriceText.Format(99m, PriceKind.Sale, PassPriceUnit.OneTime));
        Assert.Null(AdvertisedPriceText.FormatOrNull(null, PriceKind.StandardCatalog, PassPriceUnit.PerMonth));
    }
}
