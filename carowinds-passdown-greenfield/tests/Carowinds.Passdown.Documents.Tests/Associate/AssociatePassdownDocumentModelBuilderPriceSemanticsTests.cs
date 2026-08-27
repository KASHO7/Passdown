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

    [Fact]
    public void Build_MilitaryFirstResponderCategory_RendersUnderItsOwnSectionName()
    {
        var model = BuildModelWith(MilitaryProduct());

        Assert.Contains(model.ProductCategories, c => c.CategoryName == "Military & First Responder");
    }

    [Fact]
    public void Build_BringAFriendDuoCategory_RendersUnderItsOwnSectionName()
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

        Assert.Contains(model.ProductCategories, c => c.CategoryName == "Bring-A-Friend Duo");
    }
}
