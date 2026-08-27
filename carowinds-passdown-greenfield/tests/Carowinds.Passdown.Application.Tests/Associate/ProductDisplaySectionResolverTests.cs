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
