using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.Templates.TestSupport;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Phase 18D: the printed passdown (Classic, Compact, Modern) and the finalized snapshot carry the
/// effective North Gate/Avenue times — the override when one is set — with no "manual override"
/// qualifier, and a finalized snapshot keeps the values it froze no matter what changes later.
/// NormalDay opens at 9:00 AM, so the calculated defaults are North Gate 8:30 AM / Avenue 8:15 AM.
/// </summary>
public sealed class GateOverrideDocumentTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    public static IEnumerable<object[]> AllTemplates()
    {
        yield return [AssociatePassdownTemplate.Classic];
        yield return [AssociatePassdownTemplate.Compact];
        yield return [AssociatePassdownTemplate.Modern];
    }

    /// <summary>NormalDay with the gate overrides applied exactly as the production composer applies them (draft + recomputed effective hours).</summary>
    private static AssociateWorkspace NormalDayWith(TimeOnly? northGate, TimeOnly? avenue)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var effective = ParkHoursCalculator.Calculate(
            false, new TimeOnly(9, 0), new TimeOnly(21, 0), false, null, null, false, null, null, northGate, avenue);
        return workspace with
        {
            Draft = workspace.Draft with { NorthGateOpeningOverride = northGate, AvenueOpeningOverride = avenue },
            ParkHours = workspace.ParkHours with { Effective = effective },
        };
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Pdf_ShowsTheOverriddenAvenue_AndTheDefaultNorthGate_WithoutAnOverrideQualifier(AssociatePassdownTemplate template)
    {
        var model = AssociatePassdownDocumentModelBuilder.Build(NormalDayWith(null, new TimeOnly(7, 55)), GeneratedAt);
        Assert.Equal("8:30 AM", model.ParkHours.NorthGateText);
        Assert.Equal("7:55 AM", model.ParkHours.AvenueText);
        Assert.Equal("9:00 AM - 9:00 PM", model.ParkHours.HeadlineText); // park hours untouched by an entrance override.

        var result = Generator.Generate(model, template);
        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        var text = PdfInspector.ExtractAllText(result.PdfBytes!);

        Assert.Contains("7:55 AM", text);
        Assert.Contains("8:30 AM", text);
        Assert.DoesNotContain("8:15 AM", text); // the replaced default never prints.
        Assert.DoesNotContain("override", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Pdf_ShowsBothOverrides(AssociatePassdownTemplate template)
    {
        var model = AssociatePassdownDocumentModelBuilder.Build(NormalDayWith(new TimeOnly(7, 40), new TimeOnly(7, 35)), GeneratedAt);

        var text = PdfInspector.ExtractAllText(Generator.Generate(model, template).PdfBytes!);

        Assert.Contains("7:40 AM", text);
        Assert.Contains("7:35 AM", text);
        Assert.DoesNotContain("8:15 AM", text);
    }

    [Fact]
    public void FinalizedSnapshot_FreezesTheEffectiveValues_AndLaterChangesNeverAlterIt()
    {
        var builder = new FinalizedPassdownSnapshotBuilder();
        var frozen = builder.Build(NormalDayWith(new TimeOnly(7, 40), new TimeOnly(7, 35)), GeneratedAt);
        var storedJson = FinalizedPassdownSnapshotSerializer.Serialize(frozen);

        Assert.Equal("7:40 AM", frozen.ParkHours.NorthGateText);
        Assert.Equal("7:35 AM", frozen.ParkHours.AvenueText);
        Assert.Equal(FinalizedPassdownSnapshot.CurrentSchemaVersion, frozen.SchemaVersion); // no snapshot schema change for 18D

        // Later: overrides reset and official hours move — a fresh build reflects that...
        var later = AssociateWorkspaceFixtures.NormalDay();
        later = later with
        {
            ParkHours = later.ParkHours with
            {
                Effective = ParkHoursCalculator.Calculate(false, new TimeOnly(11, 0), new TimeOnly(21, 0), false, null, null, false, null, null),
            },
        };
        Assert.Equal("10:30 AM", builder.Build(later, GeneratedAt).ParkHours.NorthGateText);

        // ...but the stored History record is read back exactly as frozen, never recalculated.
        var reread = FinalizedPassdownSnapshotSerializer.Deserialize(storedJson);
        Assert.Equal("7:40 AM", reread.ParkHours.NorthGateText);
        Assert.Equal("7:35 AM", reread.ParkHours.AvenueText);
    }
}
