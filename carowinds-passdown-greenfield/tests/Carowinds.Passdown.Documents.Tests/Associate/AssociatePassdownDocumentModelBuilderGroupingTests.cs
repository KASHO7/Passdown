using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 15A §16: PDF-composition-level grouping tests — every included product must render in
/// exactly one <see cref="PdfProductCategory"/>, resolved through <see cref="ProductDisplaySectionResolver"/>
/// rather than the raw retrieval <see cref="SourceCategory"/> (docs/phase13-hardening.md Phase 15A
/// section). Price-semantics preservation (qualifier/comparison/bundle notes) is covered separately
/// in <see cref="AssociatePassdownDocumentModelBuilderPriceSemanticsTests"/>.
/// </summary>
public sealed class AssociatePassdownDocumentModelBuilderGroupingTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 27, 6, 8, 0, TimeSpan.Zero);

    private static AssociatePassdownDocumentModel BuildModelWith(params AssociateProduct[] products)
    {
        var workspace = AssociateWorkspaceFixtures.MinimalValid() with { Products = products };
        return AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);
    }

    private static AssociateProduct CatalogProduct(SourceCategory category, string identity, string displayName, decimal price = 10m, string? qualifier = null, decimal? comparison = null) => new(
        identity,
        displayName,
        category,
        IsCustom: false,
        price,
        OfficialDisplayPrice: null,
        comparison,
        qualifier,
        DateSpecific: false,
        ApplicableDate: null,
        Verification: AssociateVerificationInfo.NotCheckedYet,
        IncludeOnPdf: true,
        DisplayPriceOverride: null,
        PriceKind: qualifier == "Starting From" ? PriceKind.StartingFrom : PriceKind.StandardCatalog);

    /// <summary>An SCarowinds-named Promotions card renders under SCarowinds, never under Promotions too.</summary>
    [Fact]
    public void ScarowindsSpecificPromotion_RendersUnderScarowinds_NotAlsoUnderPromotions()
    {
        var product = CatalogProduct(SourceCategory.Promotions, "catalog|promotions|scarowinds-bundles", "SCarowinds Bundles", qualifier: "Starting From");

        var model = BuildModelWith(product);

        Assert.Contains(model.ProductCategories, c => c.CategoryName == "SCarowinds" && c.Products.Any(r => r.DisplayName == "SCarowinds Bundles"));
        Assert.DoesNotContain(model.ProductCategories, c => c.CategoryName == "Promotions" && c.Products.Any(r => r.DisplayName == "SCarowinds Bundles"));
    }

    /// <summary>Fast Lane products keep their own section, unaffected by the grouping fix.</summary>
    [Fact]
    public void FastLaneProduct_StaysUnderFastLane()
    {
        var product = CatalogProduct(SourceCategory.DatedFastLane, "dated|DatedFastLane|reserve", "Fast Lane Reserve");

        var model = BuildModelWith(product);

        Assert.Contains(model.ProductCategories, c => c.CategoryName == "Fast Lane" && c.Products.Any(r => r.DisplayName == "Fast Lane Reserve"));
    }

    /// <summary>Ordinary Bring-A-Friend, with no exact dated data merged in, stays under Promotions with its generic qualifier.</summary>
    [Fact]
    public void OrdinaryBringAFriend_StaysUnderPromotions()
    {
        var product = CatalogProduct(SourceCategory.Promotions, "catalog|promotions|baf", "Bring-A-Friend Ticket", price: 35m, qualifier: "Starting From", comparison: 80m);

        var model = BuildModelWith(product);

        Assert.Contains(model.ProductCategories, c => c.CategoryName == "Promotions" && c.Products.Any(r => r.DisplayName == "Bring-A-Friend Ticket"));
    }

    /// <summary>A brand-new, never-before-seen promotional card still falls into Promotions — a true fallback, not a hardcoded list.</summary>
    [Fact]
    public void GenericNewPromotion_FallsIntoPromotions()
    {
        var product = CatalogProduct(SourceCategory.Promotions, "catalog|promotions|new-offer", "Totally New Limited-Time Summer Splash Offer");

        var model = BuildModelWith(product);

        Assert.Contains(model.ProductCategories, c => c.CategoryName == "Promotions" && c.Products.Any(r => r.DisplayName == "Totally New Limited-Time Summer Splash Offer"));
    }

    /// <summary>Phase 17A §6/§14: "Military Gold Pass" renders under Season Passes, not Daily Admission, even though it's scraped under SourceCategory.MilitaryFirstResponder.</summary>
    [Fact]
    public void MilitaryGoldPass_RendersUnderSeasonPasses_NotDailyAdmission()
    {
        var product = CatalogProduct(SourceCategory.MilitaryFirstResponder, "catalog|military|gold-pass", "Military Gold Pass");

        var model = BuildModelWith(product);

        Assert.Contains(model.ProductCategories, c => c.CategoryName == "Season Passes" && c.Products.Any(r => r.DisplayName == "Military Gold Pass"));
        Assert.DoesNotContain(model.ProductCategories, c => c.CategoryName == "Daily Admission" && c.Products.Any(r => r.DisplayName == "Military Gold Pass"));
    }

    /// <summary>Phase 17A §6: an ordinary Military daily ticket (no season-pass wording) is unaffected and still renders under Daily Admission.</summary>
    [Fact]
    public void MilitaryDailyTicket_StaysUnderDailyAdmission()
    {
        var product = CatalogProduct(SourceCategory.MilitaryFirstResponder, "catalog|military|ticket", "Military & First Responder Tickets", comparison: 80m, qualifier: "Starting From");

        var model = BuildModelWith(product);

        Assert.Contains(model.ProductCategories, c => c.CategoryName == "Daily Admission" && c.Products.Any(r => r.DisplayName == "Military & First Responder Tickets"));
    }

    /// <summary>
    /// Phase 17A §9/§10: "2027 Gold Pass" is tracked as two independent catalog rows (native
    /// SeasonPasses, and a Promotions-category duplicate discovered on the same promoted-offer page).
    /// Once both reclassify into Season Passes, the PDF must print it once, not twice.
    /// </summary>
    [Fact]
    public void SameNamedPassAcrossTwoCategories_PrintsOnceUnderSeasonPasses()
    {
        var nativeSeasonPass = CatalogProduct(SourceCategory.SeasonPasses, "catalog|season|2027-gold", "2027 Gold Pass");
        var promotionsDuplicate = CatalogProduct(SourceCategory.Promotions, "catalog|promotions|2027-gold", "2027 Gold Pass", qualifier: "Starting From");

        var model = BuildModelWith(nativeSeasonPass, promotionsDuplicate);

        var seasonPasses = Assert.Single(model.ProductCategories, c => c.CategoryName == "Season Passes");
        Assert.Single(seasonPasses.Products, r => r.DisplayName == "2027 Gold Pass");
        Assert.DoesNotContain(model.ProductCategories, c => c.CategoryName == "Promotions");
    }

    /// <summary>Every rendered product appears in exactly one section — proven across one composition spanning every category, not one example at a time.</summary>
    [Fact]
    public void EveryRenderedProduct_AppearsInExactlyOneSection()
    {
        var products = new[]
        {
            CatalogProduct(SourceCategory.DatedAdmissionTickets, "dated|DatedAdmissionTickets|1", "1-Day Ticket"),
            CatalogProduct(SourceCategory.MilitaryFirstResponder, "catalog|military|ticket", "Military & First Responder Tickets", comparison: 80m, qualifier: "Starting From"),
            CatalogProduct(SourceCategory.DatedFastLane, "dated|DatedFastLane|1", "Fast Lane Reserve"),
            CatalogProduct(SourceCategory.HauntedAttractions, "dated|HauntedAttractions|1", "Haunted Attractions Pass"),
            CatalogProduct(SourceCategory.Promotions, "catalog|promotions|scarowinds", "SCarowinds Bundles", qualifier: "Starting From"),
            CatalogProduct(SourceCategory.Promotions, "catalog|promotions|baf", "Bring-A-Friend Ticket", qualifier: "Starting From", comparison: 80m),
            CatalogProduct(SourceCategory.SeasonPasses, "catalog|season|gold", "Gold Season Pass"),
            CatalogProduct(SourceCategory.AddOns, "catalog|addon|parking", "Preferred Parking"),
        };

        var model = BuildModelWith(products);

        var seen = new HashSet<string>();
        foreach (var category in model.ProductCategories)
        {
            foreach (var row in category.Products)
            {
                Assert.True(seen.Add(row.DisplayName), $"'{row.DisplayName}' appeared in more than one section.");
            }
        }

        Assert.Equal(products.Select(p => p.DisplayName).ToHashSet(), seen);
    }
}
