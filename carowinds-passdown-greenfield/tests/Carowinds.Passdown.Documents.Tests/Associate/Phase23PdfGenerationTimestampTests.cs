using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 23: root cause of the intermittent PDF byte-comparison failure. QuestPDF writes the
/// document-info /CreationDate and /ModDate from the wall clock at one-second resolution, so two
/// renders of one model on either side of a second boundary differ in exactly those two values and
/// nothing else. These tests force that boundary, so the difference is reproduced every run rather
/// than by chance, and pin down that <see cref="PdfInspector.WithoutGenerationTimestamps"/> masks only it.
/// </summary>
public sealed class Phase23PdfGenerationTimestampTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);

    private static byte[] Render(AssociatePassdownTemplate template) =>
        new AssociatePassdownPdfGenerator()
            .Generate(AssociatePassdownDocumentModelBuilder.Build(AssociateWorkspaceFixtures.NormalDay(), GeneratedAt), template)
            .PdfBytes!;

    private static void WaitForTheNextWallClockSecond()
    {
        var second = DateTimeOffset.Now.ToUnixTimeSeconds();
        while (DateTimeOffset.Now.ToUnixTimeSeconds() == second)
        {
            Thread.Sleep(5);
        }
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void RendersInDifferentWallClockSeconds_DifferOnlyInTheGenerationTimestamps(AssociatePassdownTemplate template)
    {
        var first = Render(template);
        WaitForTheNextWallClockSecond();
        var second = Render(template);

        Assert.Equal(first.Length, second.Length);
        var differing = Enumerable.Range(0, first.Length).Where(i => first[i] != second[i]).ToList();
        Assert.NotEmpty(differing); // the wall-clock stamp really is in the bytes
        Assert.Equal(PdfInspector.WithoutGenerationTimestamps(first), PdfInspector.WithoutGenerationTimestamps(second));
    }

    [Fact]
    public void Normalizing_KeepsTheLength_AndChangesNothingOutsideTheTwoTimestampValues()
    {
        var pdf = Render(AssociatePassdownTemplate.Classic);
        var normalized = PdfInspector.WithoutGenerationTimestamps(pdf);

        Assert.Equal(pdf.Length, normalized.Length);
        var changed = Enumerable.Range(0, pdf.Length).Where(i => pdf[i] != normalized[i]).ToList();
        Assert.InRange(changed.Count, 1, 28); // at most the 14 digits of each of the two stamps
        Assert.All(changed, i => Assert.InRange(pdf[i], (byte)'0', (byte)'9'));
        Assert.Equal(PdfInspector.ExtractAllText(pdf), PdfInspector.ExtractAllText(normalized));
    }

    [Fact]
    public void Normalizing_StillDetectsARealContentDifference()
    {
        var classic = PdfInspector.WithoutGenerationTimestamps(Render(AssociatePassdownTemplate.Classic));
        var compact = PdfInspector.WithoutGenerationTimestamps(Render(AssociatePassdownTemplate.Compact));

        Assert.NotEqual(classic, compact);
    }

    [Fact]
    public void Normalizing_RefusesBytesWithoutTheTimestampFields()
    {
        Assert.Throws<InvalidOperationException>(() => PdfInspector.WithoutGenerationTimestamps("%PDF-1.7 no info dictionary"u8.ToArray()));
    }
}
