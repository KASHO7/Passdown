using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.Templates.TestSupport;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Pre-RC.15 post-closing pricing retention (docs/pre-rc15-post-closing-pricing-retention.md): previously
/// verified Daily Admission and Fast Lane prices still print in every template through the one shared
/// pipeline, the effective (overridden) price still wins, and the existing footer note says the pricing is
/// not currently verified — no template structure change.
/// </summary>
public sealed class PostClosingPricingRetentionDocumentTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 22, 2, 30, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();
    private const string FooterNote = "Some product pricing has not been recently verified.";

    public static IEnumerable<object[]> AllTemplates()
    {
        yield return [AssociatePassdownTemplate.Classic];
        yield return [AssociatePassdownTemplate.Compact];
        yield return [AssociatePassdownTemplate.Modern];
    }

    [Fact]
    public void Model_KeepsThePreviouslyVerifiedRows_WithEffectivePrices_AndFlagsThem()
    {
        var model = AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.PreviouslyVerifiedDay(), GeneratedAt);

        var rows = model.ProductCategories.SelectMany(c => c.Products).ToDictionary(r => r.DisplayName, r => r.EffectivePriceText);
        Assert.Equal("$64.99", rows["Park Ticket"]);
        Assert.Equal("$49.00", rows["Fast Lane Reserve"]);
        Assert.Equal("$80.00", rows["Fast Lane Ultimate"]); // manual override stays the printed price
        Assert.Equal("$99.00", rows["Gold Pass"]);
        Assert.True(model.HasUnverifiedProductPricing);
    }

    [Fact]
    public void Model_WithOnlyCurrentlyVerifiedPricing_HasNoFooterFlag()
    {
        var model = AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), GeneratedAt);

        Assert.False(model.HasUnverifiedProductPricing);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Pdf_PrintsRetainedPrices_AndTheNotRecentlyVerifiedNote(AssociatePassdownTemplate template)
    {
        var model = AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.PreviouslyVerifiedDay(), GeneratedAt);

        var result = Generator.Generate(model, template);

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);
        Assert.Contains("$64.99", text);
        Assert.Contains("$49.00", text);
        Assert.Contains("$80.00", text);
        Assert.DoesNotContain("$85.00", text); // the overridden official price is not printed
        Assert.Contains(FooterNote, text);
    }

    [Fact]
    public void FinalizedSnapshot_KeepsTheRetainedRows_AndTheFlag()
    {
        var workspace = AssociateWorkspaceFixtures.PreviouslyVerifiedDay();

        var snapshot = new FinalizedPassdownSnapshotBuilder().Build(workspace, GeneratedAt);

        Assert.Equal(4, snapshot.ProductCategories.Sum(c => c.Products.Count));
        Assert.True(snapshot.HasUnverifiedProductPricing);
    }
}
