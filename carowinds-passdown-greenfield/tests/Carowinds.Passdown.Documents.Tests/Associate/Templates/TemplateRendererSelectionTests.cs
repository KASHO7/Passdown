using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 17C §29 (revised Phase 17D §7/§18): proves the orchestrating
/// <see cref="AssociatePassdownPdfGenerator"/> actually dispatches to a different renderer per
/// <see cref="AssociatePassdownTemplate"/>. Phase 17D removes every literal app-branding/title string
/// ("CAROWINDS OPERATIONS", "Associate Passdown") that the pre-17D version of this test used to prove
/// template identity by, so "renders distinctly" is now proven by raw PDF byte inequality (the three
/// renderers use different fonts/sizes/colors/spacing even when the visible words are the same) rather
/// than a template-specific literal string.
/// </summary>
public sealed class TemplateRendererSelectionTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    private static AssociatePassdownDocumentModel BuildModel() =>
        AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), GeneratedAt);

    // Phase 17D §7: no template prints app branding or the old "Associate Passdown" document title —
    // the date is the primary page heading instead.
    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void EveryTemplate_NeverPrintsAppBrandingOrDocumentTitle(AssociatePassdownTemplate template)
    {
        var result = Generator.Generate(BuildModel(), template);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        Assert.DoesNotContain("CAROWINDS OPERATIONS", text);
        Assert.DoesNotContain("Associate Passdown", text);

        var model = BuildModel();
        Templates.TestSupport.PdfTextAssert.ContainsOperatingDate(template, model.OperatingDateText, text);
    }

    [Fact]
    public void ModernSelected_RendersDistinctlyFromClassicAndCompact()
    {
        var classicBytes = Generator.Generate(BuildModel(), AssociatePassdownTemplate.Classic).PdfBytes!;
        var compactBytes = Generator.Generate(BuildModel(), AssociatePassdownTemplate.Compact).PdfBytes!;
        var modernBytes = Generator.Generate(BuildModel(), AssociatePassdownTemplate.Modern).PdfBytes!;

        // Proof that three genuinely different renderers ran, not one renderer producing the same
        // output three times — byte inequality rather than a template-specific literal string, since
        // Phase 17D's three templates now share the same master content/order (spec §18).
        Assert.NotEqual(classicBytes, modernBytes);
        Assert.NotEqual(compactBytes, modernBytes);
        Assert.NotEqual(classicBytes, compactBytes);
    }

    [Fact]
    public void NoTemplateArgument_DefaultsToClassic()
    {
        var defaulted = PdfInspector.ExtractAllText(Generator.Generate(BuildModel()).PdfBytes!);
        var explicitClassic = PdfInspector.ExtractAllText(Generator.Generate(BuildModel(), AssociatePassdownTemplate.Classic).PdfBytes!);

        Assert.Equal(explicitClassic, defaulted);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void EveryTemplate_GeneratesSuccessfully(AssociatePassdownTemplate template)
    {
        var result = Generator.Generate(BuildModel(), template);

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.NotNull(result.PdfBytes);
        Assert.NotEmpty(result.PdfBytes!);
        Assert.True(result.PageCount >= 1);
    }
}
