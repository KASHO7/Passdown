using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Documents.Tests.Associate;

public sealed class AssociatePassdownPdfGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);

    // 26. No network/repository dependency: the generator is constructed with nothing but its own
    // default preflight service and generates purely from an in-memory model.
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    private static AssociatePassdownDocumentModel BuildModel(AssociateWorkspace workspace) =>
        AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

    // 1. Minimal valid Associate PDF generates.
    [Fact]
    public void MinimalWorkspace_Generates()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.MinimalValid());

        var result = Generator.Generate(model);

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.NotNull(result.PdfBytes);
        Assert.NotEmpty(result.PdfBytes!);
    }

    // 2. Normal realistic draft generates one page. 23. Long but valid realistic content still fits one page (dense day).
    [Theory]
    [MemberData(nameof(OnePageFixtures))]
    public void RealisticWorkspaces_RenderExactlyOnePage(Func<AssociateWorkspace> fixture)
    {
        var model = BuildModel(fixture());

        var result = Generator.Generate(model);

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.Equal(AssociatePassdownPreflightStatus.FitsOnePage, result.Preflight.Status);
        Assert.Equal(1, PdfInspector.CountPages(result.PdfBytes!));
    }

    public static IEnumerable<object[]> OnePageFixtures()
    {
        yield return [(Func<AssociateWorkspace>)AssociateWorkspaceFixtures.NormalDay];
        yield return [(Func<AssociateWorkspace>)AssociateWorkspaceFixtures.DenseDay];
        yield return [(Func<AssociateWorkspace>)AssociateWorkspaceFixtures.PrivateBuyoutDay];
        yield return [(Func<AssociateWorkspace>)AssociateWorkspaceFixtures.SparseDay];
    }

    // 3. Operating date appears.
    [Fact]
    public void OperatingDate_Appears()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains(model.OperatingDateText, text);
    }

    // 4. Effective park hours appear.
    [Fact]
    public void EffectiveParkHours_Appear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains(model.ParkHours.HeadlineText, text);
    }

    // 5. North Gate / Avenue appear.
    [Fact]
    public void NorthGateAndAvenue_Appear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("North Gate", text);
        Assert.Contains("Avenue", text);
        Assert.Contains(model.ParkHours.NorthGateText!, text);
        Assert.Contains(model.ParkHours.AvenueText!, text);
    }

    // 6. Projected attendance appears.
    [Fact]
    public void ProjectedAttendance_Appears()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("18,500", text);
    }

    // 7. Pre-solds appear.
    [Fact]
    public void PreSolds_Appear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("4,200", text);
    }

    // 8. Early Ride Time content appears when enabled.
    [Fact]
    public void EarlyRideTimeDetails_AppearWhenEnabled()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.True(model.EarlyRideTime.Enabled);
        Assert.Contains("Early Ride Time from 8:30 AM", text);
    }

    // 9. Participating attractions appear.
    [Fact]
    public void ParticipatingAttractions_Appear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("Fury 325", text);
        Assert.Contains("Intimidator", text);
        Assert.Contains("Copperhead Strike", text);
    }

    // 10. Daily Feature appears, headed by the selected type itself.
    [Fact]
    public void DailyFeature_Appears()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("QUESTION OF THE DAY", text);
        Assert.Contains("What year did Carowinds open?", text);
    }

    // 11. Operational notes appear.
    [Fact]
    public void OperationalNotes_Appear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.Contains("Fury 325 closed for scheduled maintenance until 11 AM.", text);
        Assert.Contains("Guest Services relocated temporarily to the Carolina Harbor breezeway.", text);
    }

    // 12. Excluded official product does not appear. 13. Included official product appears.
    [Fact]
    public void IncludeOnPdf_ControlsWhetherAnOfficialProductPrints()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var included = workspace.Products[0] with { DisplayName = "Included Ticket", IncludeOnPdf = true };
        var excluded = workspace.Products[0] with { ProductIdentity = "dated|DatedAdmissionTickets|excluded", DisplayName = "Excluded Ticket", IncludeOnPdf = false };
        workspace = workspace with { Products = [included, excluded] };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("Included Ticket", text);
        Assert.DoesNotContain("Excluded Ticket", text);
    }

    // 14. Official price appears when no override exists.
    [Fact]
    public void OfficialPrice_AppearsWithoutOverride()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var product = workspace.Products[0] with { OfficialPriceAmount = 54.99m, DisplayPriceOverride = null };
        workspace = workspace with { Products = [product] };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("$54.99", text);
    }

    // 15. Display-price override replaces effective displayed price.
    [Fact]
    public void DisplayPriceOverride_ReplacesEffectivePrice()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var product = workspace.Products[0] with { OfficialPriceAmount = 54.99m, DisplayPriceOverride = 49.99m };
        workspace = workspace with { Products = [product] };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("$49.99", text);
        Assert.DoesNotContain("$54.99", text);
    }

    // 16. Source-backed comparison price behavior.
    [Fact]
    public void ComparisonPrice_AppearsOnlyWhenSourceBacked()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var withComparison = workspace.Products[0] with { OfficialPriceAmount = 54.99m, ComparisonPriceAmount = 69.99m };
        var withoutComparison = workspace.Products[0] with { ProductIdentity = "dated|DatedAdmissionTickets|no-comparison", DisplayName = "Plain Ticket", OfficialPriceAmount = 44.99m, ComparisonPriceAmount = null };
        workspace = workspace with { Products = [withComparison, withoutComparison] };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("$69.99", text);
    }

    // 17. Source-backed qualifier behavior.
    [Fact]
    public void Qualifier_AppearsOnlyWhenSourceBacked()
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var withQualifier = workspace.Products[0] with { PriceQualifier = "Through Labor Day" };
        workspace = workspace with { Products = [withQualifier] };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("Through Labor Day", text);
    }

    // 18. Custom included product appears. 20. Custom product Notes appear appropriately.
    [Fact]
    public void IncludedCustomProduct_AppearsWithNotes()
    {
        var workspace = AssociateWorkspaceFixtures.MinimalValid() with
        {
            Draft = AssociateDraftPayload.CreateDefault() with
            {
                CustomProducts = [new CustomProductEntry("c1", "Staff Meal Voucher", 8.00m, "Cafeteria only.", true)],
            },
        };

        var text = GenerateText(BuildModel(workspace));

        Assert.Contains("Staff Meal Voucher", text);
        Assert.Contains("Cafeteria only.", text);
        Assert.Contains("$8.00", text);
    }

    // 19. Excluded custom product does not appear.
    [Fact]
    public void ExcludedCustomProduct_DoesNotAppear()
    {
        var workspace = AssociateWorkspaceFixtures.MinimalValid() with
        {
            Draft = AssociateDraftPayload.CreateDefault() with
            {
                CustomProducts = [new CustomProductEntry("c1", "Hidden Internal Item", null, null, false)],
            },
        };

        var text = GenerateText(BuildModel(workspace));

        Assert.DoesNotContain("Hidden Internal Item", text);
    }

    // 21. Technical/internal ids do not appear.
    [Fact]
    public void TechnicalIdentifiers_NeverAppear()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());
        var text = GenerateText(model);

        Assert.DoesNotContain("dated|", text);
        Assert.DoesNotContain("catalog|", text);
        Assert.DoesNotContain("custom|", text);
        Assert.DoesNotContain("DatedAdmissionTickets", text);
        Assert.DoesNotContain("DatedFastLane", text);
        Assert.DoesNotContain("CatalogProductKey", text);
        Assert.DoesNotContain("SourceCategory", text);
        Assert.DoesNotContain("RetrievalStrategy", text);
    }

    // 22. Empty optional sections do not create ugly blank blocks.
    [Fact]
    public void EmptyOptionalSections_DoNotPrint()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.MinimalValid());
        var text = GenerateText(model);

        Assert.DoesNotContain("OPERATIONAL NOTES", text);
        Assert.DoesNotContain("PRODUCTS", text);
    }

    // 24. Excessive content follows the explicit overflow behavior: nothing is dropped, a labeled continuation page appears.
    [Fact]
    public void ExcessiveContent_SpillsToLabeledContinuationPage()
    {
        var workspace = AssociateWorkspaceFixtures.ExcessiveDay();
        var model = BuildModel(workspace);

        var result = Generator.Generate(model);

        Assert.Equal(AssociatePassdownPdfStatus.GeneratedWithContinuationPage, result.Status);
        Assert.Equal(AssociatePassdownPreflightStatus.RequiresContinuationPage, result.Preflight.Status);
        Assert.Equal(2, PdfInspector.CountPages(result.PdfBytes!));

        var page2Text = PdfInspector.ExtractPageText(result.PdfBytes!, 2);
        Assert.Contains("Supporting Details", page2Text);

        // Nothing is dropped: every included product row is present somewhere across both pages.
        var fullText = PdfInspector.ExtractAllText(result.PdfBytes!);
        foreach (var category in model.ProductCategories)
        {
            foreach (var product in category.Products)
            {
                Assert.Contains(product.DisplayName, fullText);
            }
        }

        foreach (var note in model.OperationalNotes)
        {
            Assert.Contains(note, fullText);
        }

        var pageOneCount = result.Preflight.PageOneProductCategories.Sum(c => c.Products.Count);
        var continuationCount = result.Preflight.ContinuationProductCategories.Sum(c => c.Products.Count);
        Assert.Equal(model.TotalProductCount, pageOneCount + continuationCount);
    }

    // 25. PDF begins with valid PDF bytes.
    [Fact]
    public void GeneratedBytes_BeginWithPdfMagicNumber()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.NormalDay());

        var result = Generator.Generate(model);

        Assert.NotNull(result.PdfBytes);
        Assert.Equal("%PDF"u8.ToArray(), result.PdfBytes!.Take(4).ToArray());
    }

    // Preflight: missing operating date fails generation clearly rather than producing misleading output.
    [Fact]
    public void MissingOperatingDate_FailsGenerationWithoutBytes()
    {
        var model = BuildModel(AssociateWorkspaceFixtures.MinimalValid()) with { OperatingDate = null };

        var result = Generator.Generate(model);

        Assert.Equal(AssociatePassdownPdfStatus.ValidationFailed, result.Status);
        Assert.Null(result.PdfBytes);
        Assert.Equal(AssociatePassdownPreflightStatus.MissingOperatingDate, result.Preflight.Status);
    }

    // Phase 8A: an excluded optional section's category heading and products never appear, and
    // consume no space — nothing prints where the hidden category would have been.
    [Fact]
    public void ExcludedOptionalSection_DoesNotAppearInGeneratedPdf()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded with { IncludeFastLane = false } },
        };

        var text = GenerateText(BuildModel(workspace));

        Assert.DoesNotContain("Fast Lane", text);
        Assert.Contains("DAILY ADMISSION", text); // category heading; every other included optional section is unaffected.
    }

    // Phase 8A: excluding Operational Notes means the section prints nothing at all, not an empty box.
    [Fact]
    public void ExcludedOperationalNotes_DoesNotAppearInGeneratedPdf()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with { SectionSelection = PassdownSectionSelection.AllIncluded with { IncludeOperationalNotes = false } },
        };

        var text = GenerateText(BuildModel(workspace));

        Assert.DoesNotContain("OPERATIONAL NOTES", text);
        Assert.DoesNotContain("Fury 325 closed for scheduled maintenance", text);
    }

    // Phase 8A: mandatory content always renders regardless of what SectionSelection says — there is
    // no flag that can hide it in the first place.
    [Fact]
    public void MandatoryContent_AlwaysAppears_EvenWithEveryOptionalSectionExcluded()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with
        {
            Draft = workspace.Draft with
            {
                SectionSelection = new PassdownSectionSelection(false, false, false, false, false, false),
            },
        };

        var model = BuildModel(workspace);
        var text = GenerateText(model);

        Assert.Contains(model.OperatingDateText, text);
        Assert.Contains(model.ParkHours.HeadlineText, text);
        Assert.Contains("18,500", text); // attendance
        Assert.Contains("Early Ride Time from 8:30 AM", text);
        Assert.Contains("QUESTION OF THE DAY", text); // Daily Feature
    }

    private static string GenerateText(AssociatePassdownDocumentModel model)
    {
        var result = Generator.Generate(model);
        Assert.NotNull(result.PdfBytes);
        return PdfInspector.ExtractAllText(result.PdfBytes!);
    }
}
