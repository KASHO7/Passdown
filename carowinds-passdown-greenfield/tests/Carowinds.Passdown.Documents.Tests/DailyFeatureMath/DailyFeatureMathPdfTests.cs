using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.DailyFeatureMath;

/// <summary>
/// Phase 21D: Daily Feature math in the Classic, Compact and Modern PDFs — formulas drawn (no raw
/// LaTeX in the PDF text), ordinary text unchanged, the empty section still omitted, safe fallback,
/// sensible pagination next to Phase 21C shows, deterministic output, and frozen History consistency.
/// </summary>
public sealed class DailyFeatureMathPdfTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    /// <summary>The first word of the first product heading after the Daily Feature in NormalDay.</summary>
    private const string FollowingSectionWord = "ADMISSION";

    public static TheoryData<AssociatePassdownTemplate> AllTemplates() => new(Enum.GetValues<AssociatePassdownTemplate>());

    private static AssociateWorkspace NormalDayWith(string? text, AssociateWorkspace? baseline = null)
    {
        var workspace = baseline ?? AssociateWorkspaceFixtures.NormalDay();
        return workspace with { Draft = workspace.Draft with { DailyFeatureType = DailyFeatureType.UselessFactOfTheDay, DailyFeatureText = text } };
    }

    private static byte[] Render(AssociateWorkspace workspace, AssociatePassdownTemplate template)
    {
        var result = Generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt), template);
        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        return result.PdfBytes!;
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void InlineMath_IsDrawn_NotPrintedAsLatex_AndTheSectionStaysCompact(AssociatePassdownTemplate template)
    {
        var math = Render(NormalDayWith(@"The derivative of \(x^2\) is \(2x\)."), template);
        var plain = Render(NormalDayWith("The derivative of x squared is two x."), template);
        var text = PdfInspector.ExtractAllText(math);

        Assert.Contains("USELESS", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\(", text);
        Assert.DoesNotContain("x^2", text);
        Assert.Equal(1, PdfInspector.CountPages(math));

        // The section occupies about one ordinary line either way: the next heading moves by only a few points.
        var mathTop = PdfInspector.FindWordTop(math, 1, FollowingSectionWord)!.Value;
        var plainTop = PdfInspector.FindWordTop(plain, 1, FollowingSectionWord)!.Value;
        Assert.InRange(Math.Abs(mathTop - plainTop), 0, 8);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void DisplayIntegral_Derivatives_SumsAndMultipleEquations_Render(AssociatePassdownTemplate template)
    {
        const string text = "Fact:\n\\[\n\\int_0^1 x^2\\,dx=\\frac{1}{3}\n\\]\n"
            + @"Partial \(\frac{\partial f}{\partial x}\), derivative \(\frac{d}{dx}\sin x = \cos x\), "
            + @"sum \(\sum_{i=1}^{n} i = \frac{n(n+1)}{2}\), nested \(\frac{1}{1+\frac{1}{x}}\), and \(\sqrt[3]{8}=2\).";
        var bytes = Render(NormalDayWith(text), template);

        Assert.Equal(1, PdfInspector.CountPages(bytes));
        var pdfText = PdfInspector.ExtractAllText(bytes);
        Assert.DoesNotContain("frac", pdfText);
        Assert.DoesNotContain("partial", pdfText);
        Assert.Contains("USELESS", pdfText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void PlainText_IsStillRealText_ExactlyAsBefore(AssociatePassdownTemplate template)
    {
        var pdfText = PdfInspector.ExtractAllText(Render(NormalDayWith("Copperhead Strike opened in 2019. A $5 tip, x^2 and C:\\temp stay as typed."), template));

        Assert.Contains("Copperhead Strike opened in 2019.", pdfText);
        Assert.Contains("x^2", pdfText);
        Assert.Contains("$5", pdfText);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void PlainUnicodeSymbols_GenerateWithoutMissingGlyphFailures(AssociatePassdownTemplate template)
    {
        var bytes = Render(NormalDayWith("∫₀¹ x² dx = ⅓ · ∑ ∏ ∂ ∇ ∞ ≈ ≠ ≤ ≥ ∈ ⊆ ∀ ∃ ⇒ α β γ Δ θ λ μ π Σ Ω ° ∝ → ↔ ∴"), template);
        Assert.Equal(1, PdfInspector.CountPages(bytes));
        Assert.Contains("USELESS", PdfInspector.ExtractAllText(bytes), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void MalformedAndUnsupportedMath_NeverBreakGeneration(AssociatePassdownTemplate template)
    {
        foreach (var text in new[] { @"\(\frac{1}{\)", @"Unclosed \(x+1", @"\(\foo{x}\) and \(\input{x}\)", @"\[\]", @"\(\)", @"\(\begin{align}a\end{align}\)" })
        {
            var bytes = Render(NormalDayWith(text), template);
            Assert.Equal(1, PdfInspector.CountPages(bytes));
        }
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void UnclosedDelimiter_PrintsTheSourceAsText(AssociatePassdownTemplate template)
    {
        // No formula parsed → the ordinary text path: the source is visible, never silently dropped.
        var pdfText = PdfInspector.ExtractAllText(Render(NormalDayWith(@"Unclosed \(x+1 stays visible"), template));
        Assert.Contains(@"\(x+1", pdfText);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void VeryLongEquation_AndLongDailyFeature_GenerateWithoutClippingFailures(AssociatePassdownTemplate template)
    {
        var longEquation = @"\(" + string.Join("+", Enumerable.Range(1, 80).Select(i => $"a_{{{i}}}")) + @"\)";
        Assert.Equal(1, PdfInspector.CountPages(Render(NormalDayWith("Long: " + longEquation), template)));

        var longFeature = string.Join("\n", Enumerable.Range(1, 30).Select(i => $@"Line {i}: \(\frac{{{i}}}{{x+{i}}}\) and \[\sum_{{k=1}}^{{{i}}} k^2\]"));
        var bytes = Render(NormalDayWith(longFeature), template);
        Assert.InRange(PdfInspector.CountPages(bytes), 1, 6);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void BusyPassdownWithShows_AndMath_StaysOnOnePage(AssociatePassdownTemplate template)
    {
        var workspace = NormalDayWith(@"Fact: \(\int_0^1 x^2\,dx=\frac13\) and \(e^{i\pi}+1=0\).");
        var show = new ScheduledShow("SHOW-030-00122", "Wake the Dead", "Carowinds Theatre", [new TimeOnly(19, 0), new TimeOnly(20, 0)]);
        var view = new ShowScheduleView(workspace.OperatingDate, ShowScheduleState.Scheduled, [show], false, GeneratedAt, GeneratedAt);
        var selection = new AssociateShowsSelection(true, [new AssociateShowSelection(show.ShowId, show.Name, show.Venue, show.Times, GeneratedAt)]);
        workspace = workspace with { Draft = workspace.Draft with { Shows = selection }, ShowSchedule = view };

        var bytes = Render(workspace, template);
        Assert.Equal(1, PdfInspector.CountPages(bytes));
        Assert.Contains("Wake the Dead", PdfInspector.ExtractAllText(bytes));
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void WhitespaceOnlyFeature_IsStillOmittedEntirely(AssociatePassdownTemplate template)
    {
        var pdfText = PdfInspector.ExtractAllText(Render(NormalDayWith("  \n "), template));
        Assert.DoesNotContain("USELESS", pdfText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void MathOutput_IsDeterministic(AssociatePassdownTemplate template)
    {
        var workspace = NormalDayWith(@"\(\sqrt{x^2+y^2}\) and \[\lim_{x\to 0}\frac{\sin x}{x}=1\]");
        Assert.Equal(Render(workspace, template), Render(workspace, template));
    }

    [Fact]
    public void FinalizedSnapshot_KeepsTheSourceText_AndItsPdfMatchesTheLiveRender()
    {
        const string source = @"Today: \(\frac{d}{dx}x^2=2x\) and ∫ x dx.";
        var workspace = NormalDayWith(source);
        var builder = new FinalizedPassdownSnapshotBuilder();

        var snapshot = builder.Build(workspace, GeneratedAt);
        Assert.Equal(source, snapshot.DailyFeature.Text);

        var projection = builder.BuildProjection(workspace, GeneratedAt);
        var direct = Generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt));
        Assert.Equal(direct.PdfBytes, projection.PdfBytes);
    }

    [Fact]
    public void TeamsCopy_NeverIncludesTheDailyFeature_OrLatex()
    {
        var model = AssociatePassdownDocumentModelBuilder.Build(NormalDayWith(@"Fact \(\frac{a}{b}\) ∑ unique-marker-21d"), GeneratedAt);
        foreach (var kind in Enum.GetValues<AssociateTeamsCopyKind>())
        {
            var copy = AssociateTeamsTextFormatter.Format(model, kind) ?? string.Empty;
            Assert.DoesNotContain("unique-marker-21d", copy);
            Assert.DoesNotContain(@"\frac", copy);
            Assert.DoesNotContain(@"\(", copy);
        }

        var plain = AssociatePassdownDocumentModelBuilder.Build(NormalDayWith("Fact a over b"), GeneratedAt);
        foreach (var kind in Enum.GetValues<AssociateTeamsCopyKind>())
        {
            Assert.Equal(AssociateTeamsTextFormatter.Format(plain, kind), AssociateTeamsTextFormatter.Format(model, kind));
        }
    }
}
