using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 13C Part X/AF: PDF product-row price-semantics preservation — a source-described bundle's
/// per-person breakdown survives to <see cref="PdfProductRow.Notes"/> as an explicit, derived note
/// (never silently replacing the bundle total or a real comparison/"was" price), and the raw
/// qualifier text always survives verbatim. No <see cref="PdfProductRow"/> schema change was needed.
/// </summary>
public sealed class AssociatePassdownDocumentModelBuilderPriceSemanticsTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);

    private static AssociateProduct BundleProduct() => new(
        "catalog|promotions|baf-duo",
        "Celebrate America! Bring-A-Friend Duo",
        SourceCategory.Promotions,
        IsCustom: false,
        OfficialPriceAmount: 50.00m,
        OfficialDisplayPrice: "$50",
        ComparisonPriceAmount: null,
        PriceQualifier: "Two Friends Just",
        DateSpecific: false,
        ApplicableDate: null,
        Verification: AssociateVerificationInfo.NotCheckedYet,
        IncludeOnPdf: true,
        DisplayPriceOverride: null,
        PriceKind: PriceKind.Bundle,
        BundleTotalPriceAmount: 50.00m,
        BundleQuantity: 2,
        PerPersonPriceAmount: 25.00m);

    private static AssociateProduct MilitaryProduct() => new(
        "catalog|military-first-responder|ticket",
        "Military & First Responder Tickets",
        SourceCategory.MilitaryFirstResponder,
        IsCustom: false,
        OfficialPriceAmount: 37.00m,
        OfficialDisplayPrice: "$37",
        ComparisonPriceAmount: 80.00m,
        PriceQualifier: "Starting From",
        DateSpecific: false,
        ApplicableDate: null,
        Verification: AssociateVerificationInfo.NotCheckedYet,
        IncludeOnPdf: true,
        DisplayPriceOverride: null,
        PriceKind: PriceKind.StartingFrom);

    private static AssociatePassdownDocumentModel BuildModelWith(params AssociateProduct[] products)
    {
        var workspace = AssociateWorkspaceFixtures.MinimalValid() with { Products = products };
        return AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);
    }

    [Fact]
    public void Build_BundleProduct_PreservesTotalPriceAndRawQualifier_AndAddsDerivedPerPersonNote()
    {
        var model = BuildModelWith(BundleProduct());

        var row = model.ProductCategories.SelectMany(c => c.Products).Single(r => r.DisplayName == "Celebrate America! Bring-A-Friend Duo");

        Assert.Equal("$50.00", row.EffectivePriceText); // the bundle total — never collapsed to per-person
        Assert.Equal("Two Friends Just", row.Qualifier); // raw source qualifier, never stripped
        Assert.Null(row.ComparisonPriceText); // no fabricated "was" price
        Assert.Equal("2 for $50.00 — $25.00 per person", row.Notes);
    }

    /// <summary>Phase 15A §13: once ordinary Bring-A-Friend has an Exact Dated price (post-merge — see AssociatePassdownServicePriceSemanticsTests), the PDF must never print "Starting From" alongside it, while the "$80 was" comparison still renders.</summary>
    [Fact]
    public void Build_ExactDatedBringAFriendProduct_NeverPrintsStartingFrom_ButKeepsComparisonPrice()
    {
        var exactDated = new AssociateProduct(
            "catalog|promotions|baf",
            "Bring-A-Friend Ticket",
            SourceCategory.Promotions,
            IsCustom: false,
            OfficialPriceAmount: 39.00m,
            OfficialDisplayPrice: null,
            ComparisonPriceAmount: 80.00m,
            PriceQualifier: null,
            DateSpecific: true,
            ApplicableDate: new DateOnly(2026, 8, 29),
            Verification: AssociateVerificationInfo.NotCheckedYet,
            IncludeOnPdf: true,
            DisplayPriceOverride: null,
            PriceKind: PriceKind.ExactDated);

        var model = BuildModelWith(exactDated);

        var row = model.ProductCategories.SelectMany(c => c.Products).Single(r => r.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal("$39.00", row.EffectivePriceText);
        Assert.Equal("$80.00", row.ComparisonPriceText);
        Assert.Null(row.Qualifier); // never "Starting From" for an Exact Dated price
    }

    /// <summary>Phase 15A §13 (fallback side): with only the generic catalog value, "Starting From" still prints exactly as before.</summary>
    [Fact]
    public void Build_StartingFromBringAFriendProduct_StillPrintsStartingFromQualifier()
    {
        var startingFrom = new AssociateProduct(
            "catalog|promotions|baf",
            "Bring-A-Friend Ticket",
            SourceCategory.Promotions,
            IsCustom: false,
            OfficialPriceAmount: 35.00m,
            OfficialDisplayPrice: null,
            ComparisonPriceAmount: 80.00m,
            PriceQualifier: "Starting From",
            DateSpecific: false,
            ApplicableDate: null,
            Verification: AssociateVerificationInfo.NotCheckedYet,
            IncludeOnPdf: true,
            DisplayPriceOverride: null,
            PriceKind: PriceKind.StartingFrom);

        var model = BuildModelWith(startingFrom);

        var row = model.ProductCategories.SelectMany(c => c.Products).Single(r => r.DisplayName == "Bring-A-Friend Ticket");
        Assert.Equal("$35.00", row.EffectivePriceText);
        Assert.Equal("$80.00", row.ComparisonPriceText);
        Assert.Equal("Starting From", row.Qualifier);
    }

    [Fact]
    public void Build_MilitaryFirstResponderProduct_PreservesStartingFromQualifierAndGatePriceComparison()
    {
        var model = BuildModelWith(MilitaryProduct());

        var row = model.ProductCategories.SelectMany(c => c.Products).Single(r => r.DisplayName == "Military & First Responder Tickets");

        Assert.Equal("$37.00", row.EffectivePriceText);
        Assert.Equal("$80.00", row.ComparisonPriceText);
        Assert.Equal("Starting From", row.Qualifier);
        Assert.Null(row.Notes); // no bundle fields set — no fabricated note
    }

    /// <summary>Phase 15A: Military/First Responder no longer gets a dedicated PDF section — it folds into Daily Admission (docs/phase13-hardening.md Phase 15A section).</summary>
    [Fact]
    public void Build_MilitaryFirstResponderCategory_RendersUnderDailyAdmission_NotItsOwnSection()
    {
        var model = BuildModelWith(MilitaryProduct());

        Assert.Contains(model.ProductCategories, c => c.CategoryName == "Daily Admission");
        Assert.DoesNotContain(model.ProductCategories, c => c.CategoryName == "Military & First Responder");

        var dailyAdmission = model.ProductCategories.Single(c => c.CategoryName == "Daily Admission");
        Assert.Contains(dailyAdmission.Products, r => r.DisplayName == "Military & First Responder Tickets");
    }

    /// <summary>
    /// Phase 15A: Bring-A-Friend Duo's own dated category (<see cref="SourceCategory.BringAFriendDuo"/>)
    /// never gets a section of its own — its real-world offer is represented by its Promotions catalog
    /// counterpart instead (docs/phase13-hardening.md Phase 15A section). With no such catalog row
    /// present in this composition, the dated row renders nowhere at all — it is never given a
    /// fallback section.
    /// </summary>
    [Fact]
    public void Build_BringAFriendDuoCategory_NeverRendersItsOwnSection()
    {
        var datedDuo = new AssociateProduct(
            "dated|BringAFriendDuo|10097207",
            "Celebrate America! Bring-A-Friend Duo Offer",
            SourceCategory.BringAFriendDuo,
            IsCustom: false,
            OfficialPriceAmount: 25.00m,
            OfficialDisplayPrice: null,
            ComparisonPriceAmount: null,
            PriceQualifier: null,
            DateSpecific: true,
            ApplicableDate: new DateOnly(2026, 8, 29),
            Verification: AssociateVerificationInfo.NotCheckedYet,
            IncludeOnPdf: true,
            DisplayPriceOverride: null,
            PriceKind: PriceKind.ExactDated);

        var model = BuildModelWith(datedDuo);

        Assert.DoesNotContain(model.ProductCategories, c => c.CategoryName == "Bring-A-Friend Duo");
        Assert.DoesNotContain(model.ProductCategories.SelectMany(c => c.Products), r => r.DisplayName == "Celebrate America! Bring-A-Friend Duo Offer");
    }

    /// <summary>Phase 15A: the same real Promotions catalog card ("Celebrate America! Bring-A-Friend Duo") appears exactly once, under Promotions — never duplicated by its dated sibling category.</summary>
    [Fact]
    public void Build_BringAFriendDuoCatalogCard_RendersExactlyOnceUnderPromotions()
    {
        var catalogDuo = new AssociateProduct(
            "catalog|promotions|baf-duo",
            "Celebrate America! Bring-A-Friend Duo",
            SourceCategory.Promotions,
            IsCustom: false,
            OfficialPriceAmount: 50.00m,
            OfficialDisplayPrice: "$50",
            ComparisonPriceAmount: null,
            PriceQualifier: "Two Friends Just",
            DateSpecific: false,
            ApplicableDate: null,
            Verification: AssociateVerificationInfo.NotCheckedYet,
            IncludeOnPdf: true,
            DisplayPriceOverride: null,
            PriceKind: PriceKind.Bundle,
            BundleTotalPriceAmount: 50.00m,
            BundleQuantity: 2,
            PerPersonPriceAmount: 25.00m);

        var datedDuo = new AssociateProduct(
            "dated|BringAFriendDuo|10097207",
            "Celebrate America! Bring-A-Friend Duo Offer",
            SourceCategory.BringAFriendDuo,
            IsCustom: false,
            OfficialPriceAmount: 25.00m,
            OfficialDisplayPrice: null,
            ComparisonPriceAmount: null,
            PriceQualifier: null,
            DateSpecific: true,
            ApplicableDate: new DateOnly(2026, 8, 29),
            Verification: AssociateVerificationInfo.NotCheckedYet,
            IncludeOnPdf: true,
            DisplayPriceOverride: null,
            PriceKind: PriceKind.ExactDated);

        var model = BuildModelWith(catalogDuo, datedDuo);

        var matches = model.ProductCategories.SelectMany(c => c.Products).Where(r => r.DisplayName.StartsWith("Celebrate America!")).ToList();
        var single = Assert.Single(matches);
        Assert.Equal("Celebrate America! Bring-A-Friend Duo", single.DisplayName);
        Assert.Contains(model.ProductCategories, c => c.CategoryName == "Promotions" && c.Products.Contains(single));
    }
}
