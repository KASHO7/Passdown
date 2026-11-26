using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Documents.Tests.Associate.Templates.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 31D: the PDF is still driven only by the draft. A new draft started from the defaults prints
/// Operational Notes; the Shows switch (now under Include in Passdown) still decides whether the selected
/// shows print, independently of which shows are selected — in both Classic and Modern.
/// </summary>
public sealed class Phase31DPassdownDefaultsPdfTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();
    private static readonly ScheduledShow Revue = new("SHOW-031-00001", "Midway Revue", "Midway Stage", [new TimeOnly(13), new TimeOnly(16, 30)]);

    private static AssociateWorkspace Workspace(PassdownDefaultSelections defaults, bool? includeShowsOverride = null)
    {
        var baseline = AssociateWorkspaceFixtures.NormalDay();
        var draft = AssociateDraftPayload.CreateDefault(defaults) with { OperationalNotes = ["Gate 3 closes at 6 PM"] };
        var includeShows = includeShowsOverride ?? defaults.Shows;
        draft = draft with
        {
            Shows = new AssociateShowsSelection(includeShows, [new AssociateShowSelection(Revue.ShowId, Revue.Name, Revue.Venue, Revue.Times, GeneratedAt)]),
        };
        var schedule = new ShowScheduleView(baseline.OperatingDate, ShowScheduleState.Scheduled, [Revue], false, GeneratedAt, GeneratedAt);
        return baseline with { Draft = draft, ShowSchedule = schedule };
    }

    private static string Render(AssociateWorkspace workspace, AssociatePassdownTemplate template)
    {
        var result = Generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt), template);
        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        return PdfInspector.ExtractAllText(result.PdfBytes!);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void NewDraftFromBuiltInDefaults_PrintsOperationalNotes(AssociatePassdownTemplate template)
    {
        var text = Render(Workspace(PassdownDefaultSelections.BuiltIn), template);

        PdfTextAssert.ContainsIgnoringWhitespace("Gate 3 closes at 6 PM", text);
        Assert.False(PdfTextAssert.TextContainsIgnoringWhitespace(text, "Midway Revue")); // Shows default is off
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void ShowsSwitchOn_PrintsTheSelectedShows(AssociatePassdownTemplate template)
    {
        var text = Render(Workspace(PassdownDefaultSelections.BuiltIn with { Shows = true }), template);

        PdfTextAssert.ContainsIgnoringWhitespace("Midway Revue", text);
        PdfTextAssert.ContainsIgnoringWhitespace("4:30 PM", text);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void ShowsSwitchOff_KeepsTheSelection_ButPrintsNoShows(AssociatePassdownTemplate template)
    {
        var workspace = Workspace(PassdownDefaultSelections.BuiltIn, includeShowsOverride: false);

        var text = Render(workspace, template);

        Assert.Single(workspace.Draft.Shows!.SelectedShows);
        Assert.False(PdfTextAssert.TextContainsIgnoringWhitespace(text, "Midway Revue"));
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void OperationalNotesDefaultOff_PrintsNoNotes(AssociatePassdownTemplate template)
    {
        var text = Render(Workspace(PassdownDefaultSelections.BuiltIn with { OperationalNotes = false }), template);

        Assert.False(PdfTextAssert.TextContainsIgnoringWhitespace(text, "Gate 3 closes at 6 PM"));
    }
}
