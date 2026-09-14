using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 17C §29: proves the orchestrating <see cref="AssociatePassdownPdfGenerator"/> actually
/// dispatches to a different renderer per <see cref="AssociatePassdownTemplate"/> — each template
/// renders a distinct, identifiable string ("Classic"/"Compact"/"Modern" appears literally in each
/// renderer's header) so this is a real behavioral proof, not just "three enum values exist".
/// </summary>
public sealed class TemplateRendererSelectionTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    private static AssociatePassdownDocumentModel BuildModel() =>
        AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), GeneratedAt);

    [Fact]
    public void ClassicSelected_RendersTheClassicHeader()
    {
        var result = Generator.Generate(BuildModel(), AssociatePassdownTemplate.Classic);

        var text = PdfInspector.ExtractAllText(result.PdfBytes!);
        Assert.Contains("Associate Passdown", text);
        Assert.DoesNotContain("Associate Passdown — Compact", text);
    }

    [Fact]
    public void CompactSelected_RendersTheCompactHeader()
    {
        var result = Generator.Generate(BuildModel(), AssociatePassdownTemplate.Compact);

        var text = PdfInspector.ExtractAllText(result.PdfBytes!);
        Assert.Contains("Compact", text);
    }

    [Fact]
    public void ModernSelected_RendersDistinctlyFromClassicAndCompact()
    {
        var classic = PdfInspector.ExtractAllText(Generator.Generate(BuildModel(), AssociatePassdownTemplate.Classic).PdfBytes!);
        var compact = PdfInspector.ExtractAllText(Generator.Generate(BuildModel(), AssociatePassdownTemplate.Compact).PdfBytes!);
        var modern = PdfInspector.ExtractAllText(Generator.Generate(BuildModel(), AssociatePassdownTemplate.Modern).PdfBytes!);

        // Not a byte-equality check (spec §30 explicitly avoids that) — proof that three genuinely
        // different renderers ran, not one renderer producing the same text three times.
        Assert.NotEqual(classic, modern);
        Assert.NotEqual(compact, modern);
        Assert.NotEqual(classic, compact);
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
