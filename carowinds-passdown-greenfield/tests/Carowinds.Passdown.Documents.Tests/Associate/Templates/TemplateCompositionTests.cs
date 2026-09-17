using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 17E §25/§28's "templates must be structurally, not just stylistically, different" proof —
/// every test here fails if a future change collapses Classic/Compact/Modern back onto one shared
/// composition (the exact Phase 17D failure mode this phase corrects), even though the semantic
/// content assertions in <see cref="TemplateOutputTests"/> would still pass. Two proof techniques:
/// (1) linear extracted-text order/presence (a template-specific literal, like "PARK HOURS" vs
/// "HOURS", either prints or does not), and (2) real glyph Y-positions via
/// <see cref="PdfInspector.FindWordTop"/> (proves two section headings share one physical row —
/// Compact's pairing — versus one strictly above the other — Classic/Modern's single column) — the
/// second is what a page-content assertion alone cannot distinguish, since paired and stacked layouts
/// can print the identical words in the identical linear order.
/// </summary>
public sealed class TemplateCompositionTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    private static AssociatePassdownDocumentModel NormalDayModel() =>
        AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), GeneratedAt);

    // Compact is the one template that pairs Daily Admission and Fast Lane onto the same physical
    // row (spec Part 3/6 — "paired product columns exist"). Same-row headings sit at (near-)identical
    // vertical positions regardless of how many rows either category has beneath it.
    [Fact]
    public void Compact_DailyAdmissionAndFastLane_ShareTheSamePhysicalRow()
    {
        var result = Generator.Generate(NormalDayModel(), AssociatePassdownTemplate.Compact);

        var dailyTop = PdfInspector.FindWordTop(result.PdfBytes!, 1, "DAILY");
        var fastTop = PdfInspector.FindWordTop(result.PdfBytes!, 1, "FAST");

        Assert.NotNull(dailyTop);
        Assert.NotNull(fastTop);
        Assert.True(Math.Abs(dailyTop!.Value - fastTop!.Value) < 3.0,
            $"Expected Compact's Daily Admission/Fast Lane headings on the same row (small Y gap); actual gap was {Math.Abs(dailyTop.Value - fastTop.Value)}pt.");
    }

    // Classic never pairs categories (spec Part 2 — "no two-column product matrix"): Fast Lane's
    // heading sits well below Daily Admission's, separated by the whole Daily Admission section.
    [Fact]
    public void Classic_DailyAdmissionAndFastLane_RenderOnSeparateRows_NoPairing()
    {
        var result = Generator.Generate(NormalDayModel(), AssociatePassdownTemplate.Classic);

        var dailyTop = PdfInspector.FindWordTop(result.PdfBytes!, 1, "DAILY");
        var fastTop = PdfInspector.FindWordTop(result.PdfBytes!, 1, "FAST");

        Assert.NotNull(dailyTop);
        Assert.NotNull(fastTop);
        Assert.True(Math.Abs(dailyTop!.Value - fastTop!.Value) > 20.0,
            $"Expected Classic's Daily Admission/Fast Lane headings on separate rows (large Y gap); actual gap was {Math.Abs(dailyTop.Value - fastTop.Value)}pt.");
    }

    // Modern also renders single-column (spec Part 4 — "mostly single-column product sections... no
    // sidebar rail"), the same non-pairing proof as Classic, not Compact's pairing.
    [Fact]
    public void Modern_DailyAdmissionAndFastLane_RenderOnSeparateRows_NoPairing()
    {
        var result = Generator.Generate(NormalDayModel(), AssociatePassdownTemplate.Modern);

        var dailyTop = PdfInspector.FindWordTop(result.PdfBytes!, 1, "DAILY");
        var fastTop = PdfInspector.FindWordTop(result.PdfBytes!, 1, "FAST");

        Assert.NotNull(dailyTop);
        Assert.NotNull(fastTop);
        Assert.True(Math.Abs(dailyTop!.Value - fastTop!.Value) > 20.0,
            $"Expected Modern's Daily Admission/Fast Lane headings on separate rows (large Y gap); actual gap was {Math.Abs(dailyTop.Value - fastTop.Value)}pt.");
    }

    // Spec Part 4/9: Modern is the only template that numbers its sections, and the numbering is
    // sequential and continues from the product categories into Operational Notes.
    [Fact]
    public void Modern_NumbersSectionsSequentially_ThroughProductsAndIntoOperationalNotes()
    {
        var result = Generator.Generate(NormalDayModel(), AssociatePassdownTemplate.Modern);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        var dailyAdmissionIndex = text.IndexOf("DAILY ADMISSION", StringComparison.Ordinal);
        var fastLaneIndex = text.IndexOf("FAST LANE", StringComparison.Ordinal);
        var notesIndex = text.IndexOf("OPERATIONAL NOTES", StringComparison.Ordinal);

        Assert.True(dailyAdmissionIndex >= 0);
        Assert.True(fastLaneIndex >= 0);
        Assert.True(notesIndex >= 0);

        // "01" appears before Daily Admission's heading, and "05" (four categories precede notes in
        // NormalDay: Daily Admission, Fast Lane, Season Passes, Add-Ons) appears right before Notes.
        Assert.Contains("01", text[..dailyAdmissionIndex]);
        Assert.Contains("05", text.Substring(fastLaneIndex, notesIndex - fastLaneIndex));
    }

    // Neither Classic nor Compact print gutter section numbers at all — the numbered/accented system
    // is exclusively Modern's (spec Part 4's own distinguishing trait).
    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    public void ClassicAndCompact_NeverPrintNumberedSectionGutters(AssociatePassdownTemplate template)
    {
        var result = Generator.Generate(NormalDayModel(), template);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        Assert.DoesNotContain("01", text);
    }

    // Spec Part 8: the top summary is a genuinely different composition per template, not just a
    // restyled copy — proven by a literal each renderer alone prints. Classic's vertical list is the
    // only one captioned "PARK HOURS"; Compact's inline strip is captioned "HOURS" (never the longer
    // "PARK HOURS" phrase); Modern's editorial grid carries no "PARK HOURS"/"HOURS" caption at all —
    // the headline and NORTH GATE/AVENUE/ATTENDANCE/PRE-SOLDS columns stand on their own.
    [Fact]
    public void TopSummaryCaption_DiffersByTemplate()
    {
        var classicText = PdfInspector.ExtractAllText(Generator.Generate(NormalDayModel(), AssociatePassdownTemplate.Classic).PdfBytes!);
        var compactText = PdfInspector.ExtractAllText(Generator.Generate(NormalDayModel(), AssociatePassdownTemplate.Compact).PdfBytes!);
        var modernText = PdfInspector.ExtractAllText(Generator.Generate(NormalDayModel(), AssociatePassdownTemplate.Modern).PdfBytes!);

        Assert.Contains("PARK HOURS", classicText, StringComparison.Ordinal);

        Assert.Contains("HOURS", compactText, StringComparison.Ordinal);
        Assert.DoesNotContain("PARK HOURS", compactText, StringComparison.Ordinal);

        Assert.DoesNotContain("HOURS", modernText, StringComparison.Ordinal);
        Assert.Contains("NORTH GATE", modernText, StringComparison.Ordinal);
    }

    // Spec Part 25's blunt proof-of-life: the three renderers must not converge on one composition —
    // every template pair must disagree on at least one of the structural signals above.
    [Fact]
    public void AllThreeTemplates_AreStructurallyDistinctFromOneAnother()
    {
        var model = NormalDayModel();
        var classicBytes = Generator.Generate(model, AssociatePassdownTemplate.Classic).PdfBytes!;
        var compactBytes = Generator.Generate(model, AssociatePassdownTemplate.Compact).PdfBytes!;
        var modernBytes = Generator.Generate(model, AssociatePassdownTemplate.Modern).PdfBytes!;

        var classicDailyTop = PdfInspector.FindWordTop(classicBytes, 1, "DAILY");
        var classicFastTop = PdfInspector.FindWordTop(classicBytes, 1, "FAST");
        var compactDailyTop = PdfInspector.FindWordTop(compactBytes, 1, "DAILY");
        var compactFastTop = PdfInspector.FindWordTop(compactBytes, 1, "FAST");
        var modernDailyTop = PdfInspector.FindWordTop(modernBytes, 1, "DAILY");
        var modernFastTop = PdfInspector.FindWordTop(modernBytes, 1, "FAST");

        // Compact pairs (small gap); Classic and Modern do not (large gap) — Compact must differ from
        // both, and this alone is not what makes Classic and Modern differ from each other (that is
        // proven separately by the numbered-gutter and top-summary-caption tests above).
        Assert.True(Math.Abs(compactDailyTop!.Value - compactFastTop!.Value) < 3.0);
        Assert.True(Math.Abs(classicDailyTop!.Value - classicFastTop!.Value) > 20.0);
        Assert.True(Math.Abs(modernDailyTop!.Value - modernFastTop!.Value) > 20.0);
    }
}
