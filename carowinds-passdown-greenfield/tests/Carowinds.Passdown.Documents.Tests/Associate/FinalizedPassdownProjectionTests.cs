using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Covers <see cref="FinalizedPassdownSnapshotBuilder.BuildProjection"/> — the Phase 9C seam that
/// derives the frozen snapshot and the frozen PDF from a single canonical document-model build (spec
/// Phase 9C Part E). <see cref="FinalizedPassdownSnapshotBuilderTests"/> already covers the
/// snapshot-only mapping in isolation; this file focuses on what is new for 9C: the paired PDF output,
/// PDF-generation failure behavior, and cross-checking that the snapshot and the stored PDF text agree
/// on the same frozen values (hours, attendance, Daily Feature, product prices, custom products,
/// operational notes).
/// </summary>
public class FinalizedPassdownProjectionTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly FinalizedPassdownSnapshotBuilder Builder = new();
    private static readonly IAssociatePassdownPdfGenerator DirectGenerator = new AssociatePassdownPdfGenerator();

    // Part E: the projection's PDF bytes are exactly what generating directly from the same
    // (workspace, asOf) document model would produce — proving BuildProjection does not derive the
    // PDF from some second, independently recomposed model.
    [Fact]
    public void BuildProjection_NormalDay_PdfBytesMatchDirectGenerationFromSameModel()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var expectedModel = AssociatePassdownDocumentModelBuilder.Build(workspace, AsOf);
        var expectedPdf = DirectGenerator.Generate(expectedModel);

        var projection = Builder.BuildProjection(workspace, AsOf);

        Assert.Equal(AssociatePassdownPdfStatus.Generated, expectedPdf.Status);
        Assert.Equal(expectedPdf.PdfBytes, projection.PdfBytes);
    }

    [Fact]
    public void BuildProjection_NormalDay_ProducesExactlyOneRealPdfPage()
    {
        var projection = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        Assert.Equal(1, PdfInspector.CountPages(projection.PdfBytes));
    }

    // Part N/O/P: the snapshot and the rendered PDF text must agree on the same frozen values — hours,
    // attendance, Daily Feature, a representative product price/comparison price, and a custom product.
    [Fact]
    public void BuildProjection_NormalDay_SnapshotAndPdfTextAgreeOnFrozenValues()
    {
        var projection = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf);
        var text = PdfInspector.ExtractAllText(projection.PdfBytes);

        Assert.Equal("9:00 AM - 9:00 PM", projection.Snapshot.ParkHours.HeadlineText);
        Assert.Contains(projection.Snapshot.ParkHours.HeadlineText, text);

        Assert.Contains("18,500", text); // projected attendance
        Assert.Contains("4,200", text); // pre-solds

        Assert.Equal("What year did Carowinds open?", projection.Snapshot.DailyFeature.Text);
        Assert.Contains(projection.Snapshot.DailyFeature.Text!, text);

        Assert.Contains("Fury 325 closed for scheduled maintenance until 11 AM.", text);
    }

    // Part P: a representative product's frozen display name/effective price/comparison price.
    [Fact]
    public void BuildProjection_NormalDay_ProductPriceAndComparisonPriceFrozenInSnapshotAndPdf()
    {
        var projection = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf);
        var text = PdfInspector.ExtractAllText(projection.PdfBytes);

        var admission = projection.Snapshot.ProductCategories.Single(c => c.CategoryName == "Daily Admission");
        var oneDay = admission.Products.Single(p => p.DisplayName == "1-Day Ticket");
        Assert.Equal("$64.99", oneDay.EffectivePriceText);
        Assert.Equal("$79.99", oneDay.ComparisonPriceText);

        Assert.Contains("1-Day Ticket", text);
        Assert.Contains("$64.99", text);
        Assert.Contains("$79.99", text);
    }

    // Part P: a custom/internal product is frozen exactly like an official one.
    [Fact]
    public void BuildProjection_NormalDay_CustomProductFrozenInSnapshotAndPdf()
    {
        var projection = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf);
        var text = PdfInspector.ExtractAllText(projection.PdfBytes);

        var custom = projection.Snapshot.ProductCategories.Single(c => c.CategoryName == "Custom / Internal Items");
        var voucher = custom.Products.Single();
        Assert.Equal("Team Member Meal Voucher", voucher.DisplayName);
        Assert.Equal("$8.00", voucher.EffectivePriceText);

        Assert.Contains("Team Member Meal Voucher", text);
        Assert.Contains("$8.00", text);
    }

    // Part O: manual/private-buyout effective hours (not the official closed status) are what freeze
    // into both the snapshot and the rendered PDF.
    [Fact]
    public void BuildProjection_PrivateBuyoutDay_ManualEffectiveHoursFrozenInSnapshotAndPdf()
    {
        var projection = Builder.BuildProjection(AssociateWorkspaceFixtures.PrivateBuyoutDay(), AsOf);
        var text = PdfInspector.ExtractAllText(projection.PdfBytes);

        Assert.Equal(FinalizedParkHoursStatus.Open, projection.Snapshot.ParkHours.Status);
        Assert.Equal("Private Buyout", projection.Snapshot.ParkHours.BadgeText);
        Assert.Contains("Acme Corp", projection.Snapshot.ParkHours.PrivateBuyoutDetails);

        Assert.Contains("6:00 PM", text);
        Assert.Contains("11:00 PM", text);
        Assert.Contains("Private Buyout", text);
        Assert.Contains("Acme Corp", text);
    }

    // Phase 13D Part AA: content that overflows one page (AssociateWorkspaceFixtures.ExcessiveDay, the
    // same fixture AssociatePassdownPdfGeneratorTests uses to exercise real multi-page generation) no
    // longer blocks finalization outright — an unattended scheduled finalization must not silently
    // lose an entire day's frozen record merely because content ran long. The frozen PDF simply spans
    // more than one physical page.
    [Fact]
    public void BuildProjection_ContentExceedsOnePage_StillFinalizes_WithMultiPageFrozenPdf()
    {
        var projection = Builder.BuildProjection(AssociateWorkspaceFixtures.ExcessiveDay(), AsOf);

        Assert.NotNull(projection.PdfBytes);
        Assert.True(PdfInspector.CountPages(projection.PdfBytes) > 1);
    }

    // Part G/U: no network/filesystem/user-interaction dependency — BuildProjection is constructed
    // with only the default in-memory PDF generator and never touches anything beyond the workspace
    // handed to it.
    [Fact]
    public void BuildProjection_NeverRequiresAnyExternalDependencyToConstruct()
    {
        var builder = new FinalizedPassdownSnapshotBuilder();

        var projection = builder.BuildProjection(AssociateWorkspaceFixtures.MinimalValid(), AsOf);

        Assert.NotNull(projection.PdfBytes);
        Assert.NotEmpty(projection.PdfBytes);
    }
}
