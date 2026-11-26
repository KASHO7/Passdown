using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 31: a current-catalog event product whose official season has ended is never printed, even
/// when its saved "include on PDF" choice is on; every other product prints exactly as before.
/// </summary>
public sealed class Phase31PastSeasonPdfTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 11, 21, 11, 0, 0, TimeSpan.Zero);

    private static AssociateProduct Promotion(string name, bool pastSeason) =>
        new($"catalog|Promotions:id:{name}", name, SourceCategory.Promotions, IsCustom: false, 55m, "$55", null, "Starting From", false, null,
            new AssociateVerificationInfo(AssociateVerificationState.Verified, GeneratedAt, null, "Verified."), IncludeOnPdf: true, DisplayPriceOverride: null)
        {
            PastSeasonNote = pastSeason ? "SCarowinds 2026 ended November 7, 2026." : null,
        };

    [Fact]
    public void PastSeasonProduct_IsNotPrinted_AndCurrentPromotionsStillAre()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded },
            Products = [Promotion("SCarowinds Bundles", pastSeason: true), Promotion("Bring-A-Friend Ticket", pastSeason: false)],
        };

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var printed = model.ProductCategories.SelectMany(c => c.Products).Select(p => p.DisplayName).ToList();
        Assert.Contains("Bring-A-Friend Ticket", printed);
        Assert.DoesNotContain("SCarowinds Bundles", printed);
        Assert.DoesNotContain(model.ProductCategories, c => c.Section == ProductDisplaySection.SCarowinds);
    }

    [Fact]
    public void SameProduct_NotPastSeason_PrintsInItsSectionAsBefore()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded },
            Products = [Promotion("SCarowinds Bundles", pastSeason: false)],
        };

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        Assert.Contains(model.ProductCategories, c => c.Section == ProductDisplaySection.SCarowinds && c.Products.Any(p => p.DisplayName == "SCarowinds Bundles"));
    }
}
