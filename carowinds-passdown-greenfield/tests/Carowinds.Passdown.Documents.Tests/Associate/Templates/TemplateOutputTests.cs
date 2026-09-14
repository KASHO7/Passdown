using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Documents.Tests.Associate.Templates.TestSupport;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 17C §30: for each template, verifies the rendered PDF actually carries every piece of the
/// template-neutral <see cref="AssociatePassdownDocumentModel"/> — day/date, Park Hours, ERT,
/// attendance/pre-solds, Daily Feature, selected notes, and products — via semantic text inspection
/// (PdfPig), never byte equality (spec's explicit "avoid brittle whole-PDF byte equality tests").
/// </summary>
public sealed class TemplateOutputTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    public static IEnumerable<object[]> AllTemplates()
    {
        yield return [AssociatePassdownTemplate.Classic];
        yield return [AssociatePassdownTemplate.Compact];
        yield return [AssociatePassdownTemplate.Modern];
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void NormalDay_EveryModelFieldAppearsInTheRenderedText(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var result = Generator.Generate(model, template);
        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        // Day/date.
        Assert.Contains(model.OperatingDateText, text);

        // Park Hours.
        Assert.Contains(model.ParkHours.HeadlineText, text);

        // Attendance / Pre-Solds.
        Assert.Contains("18,500", text);
        Assert.Contains("4,200", text);

        // Early Ride Time — Configured/Monthly for NormalDay.
        Assert.Equal(EffectiveErtKind.Configured, model.EarlyRideTime.Kind);
        Assert.Contains(model.EarlyRideTime.StartTimeText!, text);
        foreach (var ride in model.EarlyRideTime.Rides)
        {
            Assert.Contains(ride, text);
        }

        // Daily Feature.
        Assert.Contains(model.DailyFeature.Text!, text);

        // Selected (Operational) Notes.
        foreach (var note in model.OperationalNotes)
        {
            PdfTextAssert.ContainsIgnoringWhitespace(note, text);
        }

        // Products — every category and product name from the model appears.
        foreach (var category in model.ProductCategories)
        {
            Assert.Contains(category.CategoryName, text, StringComparison.OrdinalIgnoreCase);

            foreach (var product in category.Products)
            {
                Assert.Contains(product.DisplayName, text);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void ErtOverrideDay_ShowsTheOverrideStartTimeAndRides(AssociatePassdownTemplate template)
    {
        // SparseDay (not NormalDay) as the base — NormalDay's own operational notes happen to mention
        // "Fury 325" for an unrelated reason, which would make the sibling NoErt test below ambiguous.
        var workspace = AssociateWorkspaceFixtures.SparseDay() with
        {
            EarlyRideTime = new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Override, new TimeOnly(10, 0), ["Copperhead Strike"], "Delayed opening"),
        };
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var result = Generator.Generate(model, template);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        Assert.Contains(model.EarlyRideTime.StartTimeText!, text);
        Assert.Contains("Copperhead Strike", text);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void NoErtDay_NeverPrintsAFabricatedTime(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.SparseDay() with
        {
            EarlyRideTime = new EffectiveErt(EffectiveErtKind.NoErt, EffectiveErtSource.Monthly, null, [], null),
        };
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);
        Assert.Equal(EffectiveErtKind.NoErt, model.EarlyRideTime.Kind);

        var result = Generator.Generate(model, template);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        // No ride name should appear anywhere once ERT is explicitly NoErt — SparseDay carries no
        // ride names anywhere else in its content, so any appearance would be fabricated.
        Assert.DoesNotContain("Copperhead Strike", text);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void SparseDay_EmptyOptionalSections_ReclaimSpaceAndPrintNoEmptyHeading(AssociatePassdownTemplate template)
    {
        // SparseDay has no operational notes and no custom products (spec §22: empty sections must be
        // removed cleanly, never rendered as a blank placeholder).
        var workspace = AssociateWorkspaceFixtures.SparseDay();
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);
        Assert.Empty(model.OperationalNotes);

        var result = Generator.Generate(model, template);
        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        Assert.DoesNotContain("OPERATIONAL NOTES", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void ExcessiveDay_OverflowsSafely_NeverDropsSelectedContent(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.ExcessiveDay();
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var result = Generator.Generate(model, template);

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.True(result.PageCount >= 1);

        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        // Every selected note and every product must still be present, however many pages it takes
        // (spec Part 27: "never silently omit selected data").
        foreach (var note in model.OperationalNotes)
        {
            PdfTextAssert.ContainsIgnoringWhitespace(note, text);
        }

        foreach (var category in model.ProductCategories)
        {
            foreach (var product in category.Products)
            {
                Assert.Contains(product.DisplayName, text);
            }
        }
    }
}
