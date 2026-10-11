using System.Diagnostics;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 18A (docs/phase18-comprehensive-audit.md §29): pathological-but-valid associate input must
/// never crash PDF generation (which would also block scheduled finalization for that date), never
/// hang, and never silently drop the associate's text. Covers the inputs existing density/overflow
/// tests do not: an unbreakable very long token, characters outside the embedded Lato font (emoji,
/// CJK, RTL), control characters, and absurdly large numbers.
/// </summary>
public sealed class PathologicalContentTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    public static TheoryData<AssociatePassdownTemplate, string> Cases()
    {
        var data = new TheoryData<AssociatePassdownTemplate, string>();
        foreach (var template in Enum.GetValues<AssociatePassdownTemplate>())
        {
            data.Add(template, "unbreakable-token");
            data.Add(template, "non-latin-and-emoji");
            data.Add(template, "control-characters");
            data.Add(template, "huge-numbers");
        }

        return data;
    }

    private static AssociateWorkspace Build(string scenario)
    {
        var day = AssociateWorkspaceFixtures.NormalDay();
        var draft = scenario switch
        {
            "unbreakable-token" => day.Draft with
            {
                DailyFeatureText = new string('W', 1500),
                OperationalNotes = [new string('M', 800), "Normal note after the long one."],
                CustomProducts = [new CustomProductEntry("c1", new string('X', 400), 8.00m, new string('Y', 400), true)],
            },
            "non-latin-and-emoji" => day.Draft with
            {
                DailyFeatureText = "Happy Halloween 🎃👻 — Fury 325 ✓ · café · 過山車 · مرحبا · Ω≈ç√",
                OperationalNotes = ["Scarowinds 🦇 opens at 7 PM", "Ticket scanner 🔧 at North Gate"],
            },
            "control-characters" => day.Draft with
            {
                DailyFeatureText = "Line one\r\nLine two\tTabbed\u0000NUL\u0007BEL​ zero-width",
                OperationalNotes = ["Note\u001Bwith escape", "‮reversed‬ text"],
            },
            "huge-numbers" => day.Draft with
            {
                ProjectedAttendance = int.MaxValue,
                PreSolds = int.MaxValue,
                CustomProducts = [new CustomProductEntry("c1", "Absurd price", 79228162514264337593543950335m, null, true)],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        return day with { Draft = draft };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PathologicalInput_GeneratesWithoutCrashing_InBoundedTime(AssociatePassdownTemplate template, string scenario)
    {
        var model = AssociatePassdownDocumentModelBuilder.Build(Build(scenario), GeneratedAt);

        var stopwatch = Stopwatch.StartNew();
        var result = Generator.Generate(model, template);
        stopwatch.Stop();

        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        Assert.NotNull(result.PdfBytes);
        Assert.True(result.PageCount >= 1);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Generation took {stopwatch.Elapsed}.");

        var text = PdfInspector.ExtractAllText(result.PdfBytes);
        if (scenario == "non-latin-and-emoji")
        {
            Assert.Contains("Happy Halloween", text);
            Assert.Contains("café", text);
        }
        else if (scenario == "unbreakable-token")
        {
            Assert.Contains("Normal note after the long one.", text);
        }
    }
}
