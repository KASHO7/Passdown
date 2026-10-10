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

        // Day/date — printed upper-case as the page's primary heading (Phase 17D §7/§11).
        Assert.Contains(model.OperatingDateText, text, StringComparison.OrdinalIgnoreCase);

        // Park Hours.
        Assert.Contains(model.ParkHours.HeadlineText, text);

        // Attendance / Pre-Solds.
        Assert.Contains("18,500", text);
        Assert.Contains("4,200", text);

        // Early Ride Time — Configured/Monthly for NormalDay. Phase 17D §10: rides only, never a time.
        Assert.Equal(EffectiveErtKind.Configured, model.EarlyRideTime.Kind);
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
    public void ErtOverrideDay_ShowsTheOverrideRides_NeverTheStartTime(AssociatePassdownTemplate template)
    {
        // SparseDay (not NormalDay) as the base — NormalDay's own operational notes happen to mention
        // "Fury 325" for an unrelated reason, which would make the sibling NoErt test below ambiguous.
        // 10:15 AM deliberately does not collide with SparseDay's own 10:00 AM park opening / 7:00 PM
        // closing, so an assertion that this exact start time never appears cannot spuriously pass
        // (or fail) because of an unrelated Park Hours value that happens to share the same text.
        var workspace = AssociateWorkspaceFixtures.SparseDay() with
        {
            EarlyRideTime = new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Override, new TimeOnly(10, 15), ["Copperhead Strike"], "Delayed opening"),
        };
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var result = Generator.Generate(model, template);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        Assert.Contains("Copperhead Strike", text);
        // Phase 17D §10: never a start time, even for an override with one.
        Assert.DoesNotContain(model.EarlyRideTime.StartTimeText!, text);
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

    // Phase 17D §8/§9: no template prints a comparison ("was $x") price or a raw ecommerce qualifier
    // ("Online only") — NormalDay's 1-Day Ticket carries a real comparison price, and DenseDay's Gold
    // Season Pass carries a real qualifier, so this proves the omission is real, not merely untested.
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void EveryTemplate_NeverPrintsComparisonPriceOrRawQualifier(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var result = Generator.Generate(model, template);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        Assert.DoesNotContain("$79.99", text); // 1-Day Ticket's comparison price.
        Assert.DoesNotContain("Payment plan available", text); // Gold Season Pass's raw qualifier.
        Assert.DoesNotContain("was ", text, StringComparison.OrdinalIgnoreCase);
    }

    // Phase 17D §19: the footer is "Sep 15 • 1:52 PM" — no "Generated" verb, no app name.
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void EveryTemplate_FooterOmitsGeneratedWordAndAppName(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var result = Generator.Generate(model, template);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        Assert.Contains(model.GeneratedAtText, text);
        Assert.DoesNotContain("Generated", text);
        Assert.DoesNotContain("CAROWINDS OPERATIONS", text, StringComparison.OrdinalIgnoreCase);
    }

    // Phase 17D §16/§17/§25: a genuinely overflowing day may still take a second page, but Operational
    // Notes must never split across the page boundary — every note that appears lands on the same
    // page as the "OPERATIONAL NOTES" heading, never scattered as a tiny orphaned remainder.
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void ExcessiveDay_OperationalNotes_NeverSplitAcrossPages(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.ExcessiveDay();
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var result = Generator.Generate(model, template);
        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);

        var pageCount = PdfInspector.CountPages(result.PdfBytes!);
        var pagesWithAnyNote = new List<int>();

        for (var page = 1; page <= pageCount; page++)
        {
            var pageText = PdfInspector.ExtractPageText(result.PdfBytes!, page);
            if (model.OperationalNotes.Any(note => PdfTextAssert.TextContainsIgnoringWhitespace(pageText, note)))
            {
                pagesWithAnyNote.Add(page);
            }
        }

        var notesPage = Assert.Single(pagesWithAnyNote);
        var notesPageText = PdfInspector.ExtractPageText(result.PdfBytes!, notesPage);
        foreach (var note in model.OperationalNotes)
        {
            PdfTextAssert.ContainsIgnoringWhitespace(note, notesPageText);
        }
    }

    // Phase 17D §18: Modern no longer uses the Phase 17C two-column rail/main body — every template
    // now reads top-to-bottom in the same single-column master order (spec §11): Park Hours, then
    // Daily Feature, then products, then Operational Notes. A rail layout would interleave the "what's
    // happening today" facts and the products in the extracted reading order instead of this strict
    // sequence.
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void EveryTemplate_ReadingOrderIsSingleColumn_ParkHoursThenFeatureThenProductsThenNotes(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt);

        var result = Generator.Generate(model, template);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        var parkHoursIndex = text.IndexOf(model.ParkHours.HeadlineText, StringComparison.Ordinal);
        var featureIndex = text.IndexOf(model.DailyFeature.Text!, StringComparison.Ordinal);
        var firstCategoryIndex = text.IndexOf(model.ProductCategories[0].CategoryName, StringComparison.OrdinalIgnoreCase);
        var notesIndex = text.IndexOf("OPERATIONAL NOTES", StringComparison.OrdinalIgnoreCase);

        Assert.True(parkHoursIndex >= 0, "Park Hours headline not found.");
        Assert.True(featureIndex >= 0, "Daily Feature text not found.");
        Assert.True(firstCategoryIndex >= 0, "First product category not found.");
        Assert.True(notesIndex >= 0, "Operational Notes heading not found.");

        Assert.True(parkHoursIndex < featureIndex, "Park Hours should read before Daily Feature.");
        Assert.True(featureIndex < firstCategoryIndex, "Daily Feature should read before products.");
        Assert.True(firstCategoryIndex < notesIndex, "Products should read before Operational Notes.");
    }
}
