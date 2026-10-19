using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 17C §21/§31: <see cref="FinalizedPassdownSnapshotBuilder.BuildProjection"/> must build the
/// exact same <see cref="Carowinds.Passdown.Application.Finalization.FinalizedPassdownSnapshot"/> for
/// a given workspace regardless of which template rendered the frozen PDF — the snapshot is the
/// template-neutral document model's data, never the renderer's presentation. Only the PDF bytes
/// (and, downstream, the printed page count) may differ by template.
/// </summary>
public class FinalizedPassdownSnapshotBuilderTemplateTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly FinalizedPassdownSnapshotBuilder Builder = new();

    [Fact]
    public void BuildProjection_NoTemplateArgument_DefaultsToClassic()
    {
        var withDefault = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf);
        var withExplicitClassic = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf, AssociatePassdownTemplate.Classic);

        Assert.Equal(PdfInspector.WithoutGenerationTimestamps(withExplicitClassic.PdfBytes), PdfInspector.WithoutGenerationTimestamps(withDefault.PdfBytes));
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void BuildProjection_EveryTemplate_ProducesTheIdenticalSnapshot(AssociatePassdownTemplate template)
    {
        var classic = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf, AssociatePassdownTemplate.Classic);
        var underTest = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf, template);

        // Serialized comparison, not record Assert.Equal: FinalizedPassdownSnapshot's List<T>-typed
        // fields compare by reference under the compiler-synthesized record equality, so two
        // independently-built (but content-identical) snapshots would spuriously report "different"
        // — the same reason FinalizedPassdownSnapshotSerializer.ComputeContentHash exists.
        Assert.Equal(FinalizedPassdownSnapshotSerializer.Serialize(classic.Snapshot), FinalizedPassdownSnapshotSerializer.Serialize(underTest.Snapshot));
    }

    [Fact]
    public void BuildProjection_DifferentTemplates_ProduceDifferentPdfBytes()
    {
        var classic = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf, AssociatePassdownTemplate.Classic);
        var compact = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf, AssociatePassdownTemplate.Compact);
        var modern = Builder.BuildProjection(AssociateWorkspaceFixtures.NormalDay(), AsOf, AssociatePassdownTemplate.Modern);

        Assert.NotEqual(classic.PdfBytes, compact.PdfBytes);
        Assert.NotEqual(classic.PdfBytes, modern.PdfBytes);
        Assert.NotEqual(compact.PdfBytes, modern.PdfBytes);
    }

    // Spec §31's core scenario at the Documents layer: a PDF frozen under one template is never
    // silently regenerated/altered by building again under a different template later — each
    // BuildProjection call is an independent, deterministic render from the model it was given.
    [Fact]
    public void BuildProjection_UnderCompact_ThenAgainUnderModern_FirstResultIsUnaffected()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();

        var compactProjection = Builder.BuildProjection(workspace, AsOf, AssociatePassdownTemplate.Compact);
        var compactBytesSnapshot = (byte[])compactProjection.PdfBytes.Clone();

        _ = Builder.BuildProjection(workspace, AsOf, AssociatePassdownTemplate.Modern);

        Assert.Equal(compactBytesSnapshot, compactProjection.PdfBytes);
    }
}
