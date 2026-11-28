using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate.Weather;

/// <summary>
/// Phase 37F: finalization never prints weather — the frozen PDF equals the weather-free document for the same
/// workspace, whatever the Include Weather Outlook preference or the cached forecast; stored History bytes are never
/// regenerated.
/// </summary>
public class Phase37FFinalizedPdfTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    public void FinalizedPdf_IsTheWeatherFreeDocument(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();

        var projection = new FinalizedPassdownSnapshotBuilder().BuildProjection(workspace, AsOf, template);
        var weatherFree = new AssociatePassdownPdfGenerator().Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, AsOf), template).PdfBytes!;

        Assert.Equal(PdfInspector.WithoutGenerationTimestamps(weatherFree), PdfInspector.WithoutGenerationTimestamps(projection.PdfBytes));
        Assert.DoesNotContain("NWS", PdfInspector.ExtractAllText(projection.PdfBytes), StringComparison.Ordinal);
    }
}
