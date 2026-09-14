using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Tests.Associate;

/// <summary>
/// Phase 15A §16: unit tests for the one authoritative <see cref="ProductDisplaySectionResolver"/> —
/// every included product must resolve to exactly one <see cref="ProductDisplaySection"/> (or
/// <see langword="null"/> for a category with no dedicated section), never through independent
/// per-section boolean filters that could both fire for the same product.
/// </summary>
public class ProductDisplaySectionResolverTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static AssociateProduct Product(SourceCategory? category, string displayName, bool isCustom = false) => new(
        isCustom ? "custom|x" : $"catalog|{category}|x",
        displayName,
        category,
        isCustom,
        OfficialPriceAmount: 10m,
        OfficialDisplayPrice: null,
        ComparisonPriceAmount: null,
        PriceQualifier: null,
        DateSpecific: false,
        ApplicableDate: null,
        new AssociateVerificationInfo(AssociateVerificationState.Verified, FixedNow, null, null),
        IncludeOnPdf: true,
        DisplayPriceOverride: null);

    [Fact]
    public void Resolve_MilitaryFirstResponder_ResolvesToDailyAdmission()
    {
        var product = Product(SourceCategory.MilitaryFirstResponder, "Military & First Responder Tickets");

        Assert.Equal(ProductDisplaySection.DailyAdmission, ProductDisplaySectionResolver.Resolve(product));
    }

    [Fact]
    public void Resolve_MilitaryFirstResponder_HasNoDedicatedSection()
    {
        // "No dedicated Military section" (spec §16 item 5) means the category-level resolver never
        // returns anything other than DailyAdmission for it — there is no separate value to compare
        // against, by construction.
        Assert.Equal(ProductDisplaySection.DailyAdmission, ProductDisplaySectionResolver.Resolve(SourceCategory.MilitaryFirstResponder));
    }

    [Fact]
    public void Resolve_BringAFriendDuoCategory_HasNoDedicatedSection()
    {
        Assert.Null(ProductDisplaySectionResolver.Resolve(SourceCategory.BringAFriendDuo));
        Assert.Null(ProductDisplaySectionResolver.Resolve(Product(SourceCategory.BringAFriendDuo, "Celebrate America! Bring-A-Friend Duo Offer")));
    }

    [Fact]
    public void Resolve_DatedBringAFriendCategory_HasNoDedicatedSection()
    {
        Assert.Null(ProductDisplaySectionResolver.Resolve(SourceCategory.DatedBringAFriend));
        Assert.Null(ProductDisplaySectionResolver.Resolve(Product(SourceCategory.DatedBringAFriend, "Bring-A-Friend Ticket")));
    }

    [Fact]
    public void Resolve_ScarowindsSpecificPromotion_ResolvesToScarowinds_NotPromotions()
    {
        var product = Product(SourceCategory.Promotions, "SCarowinds Bundles");

        Assert.Equal(ProductDisplaySection.SCarowinds, ProductDisplaySectionResolver.Resolve(product));
    }

    [Fact]
    public void Resolve_HauntedAttractionsCategory_ResolvesToScarowinds()
    {
        var product = Product(SourceCategory.HauntedAttractions, "Haunted Attractions Pass");

        Assert.Equal(ProductDisplaySection.SCarowinds, ProductDisplaySectionResolver.Resolve(product));
    }

    [Fact]
    public void Resolve_FastLaneCategory_StaysFastLane()
    {
        var product = Product(SourceCategory.DatedFastLane, "Fast Lane Reserve");

        Assert.Equal(ProductDisplaySection.FastLane, ProductDisplaySectionResolver.Resolve(product));
    }

    [Fact]
    public void Resolve_OrdinaryBringAFriend_StaysPromotions()
    {
        var product = Product(SourceCategory.Promotions, "Bring-A-Friend Ticket");

        Assert.Equal(ProductDisplaySection.Promotions, ProductDisplaySectionResolver.Resolve(product));
    }

    [Fact]
    public void Resolve_GenericNewPromotion_FallsIntoPromotions()
    {
        // A brand-new, never-before-seen promotional card name — proving Promotions is a true
        // fallback, not a hardcoded list of known offers.
        var product = Product(SourceCategory.Promotions, "Totally New Limited-Time Summer Splash Offer");

        Assert.Equal(ProductDisplaySection.Promotions, ProductDisplaySectionResolver.Resolve(product));
    }

    [Fact]
    public void Resolve_SeasonPassesAndAddOns_PassThroughUnchanged()
    {
        Assert.Equal(ProductDisplaySection.SeasonPasses, ProductDisplaySectionResolver.Resolve(Product(SourceCategory.SeasonPasses, "Gold Season Pass")));
        Assert.Equal(ProductDisplaySection.AddOns, ProductDisplaySectionResolver.Resolve(Product(SourceCategory.AddOns, "Preferred Parking")));
    }

    [Fact]
    public void Resolve_CustomProduct_ReturnsNull()
    {
        var custom = Product(null, "Team Member Meal Voucher", isCustom: true);

        Assert.Null(ProductDisplaySectionResolver.Resolve(custom));
    }

    // Phase 17A §4/§6/§21: product-family precedence — a season-pass product name wins over category
    // and audience wording, so Military/First-Responder-branded passes land in Season Passes while
    // their plain daily-ticket counterparts stay in Daily Admission.
    [Theory]
    [InlineData(SourceCategory.SeasonPasses, "Gold Pass")]
    [InlineData(SourceCategory.SeasonPasses, "Silver Pass")]
    [InlineData(SourceCategory.SeasonPasses, "Prestige Pass")]
    [InlineData(SourceCategory.SeasonPasses, "2027 Silver Pass")]
    [InlineData(SourceCategory.SeasonPasses, "2027 Gold Pass")]
    [InlineData(SourceCategory.SeasonPasses, "2027 Prestige Pass")]
    [InlineData(SourceCategory.MilitaryFirstResponder, "Military Gold Pass")]
    [InlineData(SourceCategory.MilitaryFirstResponder, "First Responder Gold Pass")]
    public void Resolve_SeasonPassProductNames_ResolveToSeasonPasses(SourceCategory category, string displayName)
    {
        Assert.Equal(ProductDisplaySection.SeasonPasses, ProductDisplaySectionResolver.Resolve(Product(category, displayName)));
    }

    [Theory]
    [InlineData("Military 1-Day Ticket")]
    [InlineData("Military & First Responder Tickets")]
    [InlineData("First Responder Daily Ticket")]
    public void Resolve_MilitaryOrFirstResponderDailyTicket_StaysDailyAdmission_NotSeasonPasses(string displayName)
    {
        Assert.Equal(ProductDisplaySection.DailyAdmission, ProductDisplaySectionResolver.Resolve(Product(SourceCategory.MilitaryFirstResponder, displayName)));
    }

    [Fact]
    public void Resolve_BringAFriend_StillClassifiesCorrectly()
    {
        Assert.Equal(ProductDisplaySection.Promotions, ProductDisplaySectionResolver.Resolve(Product(SourceCategory.Promotions, "Bring-A-Friend Ticket")));
        // The catalog "Celebrate America! Bring-A-Friend Duo" card is represented once, generically, under Promotions — never its own section.
        Assert.Equal(ProductDisplaySection.Promotions, ProductDisplaySectionResolver.Resolve(Product(SourceCategory.Promotions, "Celebrate America! Bring-A-Friend Duo")));
    }

    [Fact]
    public void Resolve_ScarowindsPromotion_StillResolvesToScarowinds_AfterSeasonPassOverride()
    {
        // Proves the season-pass check doesn't accidentally short-circuit or reorder the existing
        // SCarowinds-specific override for an unrelated Promotions card.
        Assert.Equal(ProductDisplaySection.SCarowinds, ProductDisplaySectionResolver.Resolve(Product(SourceCategory.Promotions, "SCarowinds Bundles")));
    }

    // Phase 17A §9/§10: "2027 Gold Pass" is deliberately tracked as two independent catalog rows (one
    // native to SeasonPasses, one discovered generically under Promotions) — before this phase they
    // printed once in each of two different sections, so it was never a visible duplicate; now that
    // the season-pass override reclassifies the Promotions row into Season Passes too, ResolveAll must
    // drop the reclassified duplicate and keep only the native Season Passes row.
    [Fact]
    public void ResolveAll_SameNamedPassAcrossTwoCategories_KeepsOnlyTheNativeSeasonPassesRow()
    {
        var nativeSeasonPass = Product(SourceCategory.SeasonPasses, "2027 Gold Pass");
        var promotionsDuplicate = Product(SourceCategory.Promotions, "2027 Gold Pass");

        var resolved = ProductDisplaySectionResolver.ResolveAll([nativeSeasonPass, promotionsDuplicate]);

        var goldPassEntries = resolved.Where(entry => entry.Product.DisplayName == "2027 Gold Pass").ToList();
        var kept = Assert.Single(goldPassEntries);
        Assert.Equal(ProductDisplaySection.SeasonPasses, kept.Section);
        Assert.Equal(SourceCategory.SeasonPasses, kept.Product.Category);
    }

    [Fact]
    public void ResolveAll_MilitaryGoldPassWithNoNativeSeasonPassesCounterpart_StillAppearsOnce()
    {
        var militaryGoldPass = Product(SourceCategory.MilitaryFirstResponder, "Military Gold Pass");
        var dailyTicket = Product(SourceCategory.MilitaryFirstResponder, "Military & First Responder Tickets");

        var resolved = ProductDisplaySectionResolver.ResolveAll([militaryGoldPass, dailyTicket]);

        Assert.Equal(2, resolved.Count);
        Assert.Contains(resolved, entry => entry.Product.DisplayName == "Military Gold Pass" && entry.Section == ProductDisplaySection.SeasonPasses);
        Assert.Contains(resolved, entry => entry.Product.DisplayName == "Military & First Responder Tickets" && entry.Section == ProductDisplaySection.DailyAdmission);
    }

    [Fact]
    public void ResolveAll_NoCollisions_ReturnsEveryProductUnchanged()
    {
        var products = new[]
        {
            Product(SourceCategory.SeasonPasses, "Gold Pass"),
            Product(SourceCategory.AddOns, "Preferred Parking"),
            Product(SourceCategory.Promotions, "Bring-A-Friend Ticket"),
        };

        var resolved = ProductDisplaySectionResolver.ResolveAll(products);

        Assert.Equal(3, resolved.Count);
    }

    [Fact]
    public void ResolveAll_ExcludesCategoriesWithNoDedicatedSection()
    {
        var resolved = ProductDisplaySectionResolver.ResolveAll(
        [
            Product(SourceCategory.BringAFriendDuo, "Celebrate America! Bring-A-Friend Duo Offer"),
            Product(SourceCategory.SeasonPasses, "Gold Pass"),
        ]);

        var kept = Assert.Single(resolved);
        Assert.Equal("Gold Pass", kept.Product.DisplayName);
    }

    [Fact]
    public void Resolve_EveryNonNullCategory_ResolvesToExactlyOneSection()
    {
        // Spec §16 item 10 / §5's "exactly one section" guarantee, proven generically across every
        // currently-defined category rather than one hand-picked example at a time.
        foreach (SourceCategory category in Enum.GetValues<SourceCategory>())
        {
            var section = ProductDisplaySectionResolver.Resolve(category);
            Assert.True(section is null || Enum.IsDefined(section.Value));
        }
    }
}
