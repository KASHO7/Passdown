using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Associate.Templates;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Documents.Tests.Associate.Templates.TestSupport;
using UglyToad.PdfPig;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 22D: projected attendance and pre-solds print with en-US thousands separators in all three
/// templates and in Teams Copy, through the shared <see cref="Carowinds.Passdown.Domain.OperationalCount"/>
/// rule. Grouping was already the PDF/Teams behavior (Phase 18B/18F, "N0"); these tests pin it, prove
/// it is the only count-related difference between two renders, that large values do not clip, that
/// prices and Daily Feature math are untouched, and that finalized snapshots keep the plain integer.
/// </summary>
public sealed class Phase22DCountFormattingTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();
    private const double LetterWidth = 612;

    private static AssociateWorkspace WithCounts(int? attendance, int? preSolds, string? feature = null)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        return workspace with
        {
            Draft = workspace.Draft with
            {
                ProjectedAttendance = attendance,
                PreSolds = preSolds,
                DailyFeatureText = feature ?? workspace.Draft.DailyFeatureText,
            },
        };
    }

    private static byte[] Render(AssociateWorkspace workspace, AssociatePassdownTemplate template)
    {
        var result = Generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt), template);
        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        return result.PdfBytes!;
    }

    private static List<string> AllWords(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().SelectMany(p => p.GetWords().Select(w => w.Text)).ToList();
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void Pdf_PrintsGroupedAttendanceAndPreSolds(AssociatePassdownTemplate template)
    {
        var text = PdfInspector.ExtractAllText(Render(WithCounts(38500, 21240), template));

        PdfTextAssert.ContainsIgnoringWhitespace("38,500", text);
        PdfTextAssert.ContainsIgnoringWhitespace("21,240", text);
        Assert.DoesNotContain("38500", text.Replace(" ", string.Empty));
        Assert.DoesNotContain("21240", text.Replace(" ", string.Empty));
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void Pdf_SmallAndZeroCounts_PrintWithoutSeparators(AssociatePassdownTemplate template)
    {
        var words = AllWords(Render(WithCounts(250, 0), template));

        Assert.Contains("250", words);
        Assert.Contains("0", words);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void Pdf_MissingCounts_PrintADash_NeverZero(AssociatePassdownTemplate template)
    {
        var words = AllWords(Render(WithCounts(null, null), template));

        Assert.Contains("—", words);
        Assert.DoesNotContain("0", words);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void Pdf_OnlyTheCountTokensDifferBetweenTwoRenders(AssociatePassdownTemplate template)
    {
        var baseline = AllWords(Render(WithCounts(18500, 4200), template));
        var busy = AllWords(Render(WithCounts(38500, 21240), template))
            .Select(w => w switch { "38,500" => "18,500", "21,240" => "4,200", _ => w })
            .ToList();

        Assert.Equal(baseline, busy);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void Pdf_LargeCounts_StayOnThePage_AndDoNotOverlap(AssociatePassdownTemplate template)
    {
        var pdf = Render(WithCounts(1000000, 250000), template);

        var attendance = PdfInspector.FindWordBox(pdf, 1, "1,000,000");
        var preSolds = PdfInspector.FindWordBox(pdf, 1, "250,000");
        Assert.NotNull(attendance);
        Assert.NotNull(preSolds);
        Assert.InRange(attendance!.Value.Left, 0, LetterWidth);
        Assert.InRange(attendance.Value.Right, 0, LetterWidth);
        Assert.InRange(preSolds!.Value.Right, 0, LetterWidth);

        var sameLine = Math.Abs(attendance.Value.Top - preSolds.Value.Top) < 3;
        if (sameLine)
        {
            Assert.True(attendance.Value.Right <= preSolds.Value.Left || preSolds.Value.Right <= attendance.Value.Left,
                "attendance and pre-sold values overlap on the same line.");
        }

        Assert.Equal(PdfInspector.CountPages(Render(WithCounts(18500, 4200), template)), PdfInspector.CountPages(pdf));
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void Pdf_PricesAndDailyFeatureNumbers_AreUnchangedByCountFormatting(AssociatePassdownTemplate template)
    {
        const string feature = "Carowinds opened in 1973 and the walk is 10000 steps";
        var low = AllWords(Render(WithCounts(18500, 4200, feature), template));
        var high = AllWords(Render(WithCounts(38500, 21240, feature), template));

        // Every dollar amount is identical (no grouping was added to prices).
        Assert.Equal(low.Where(w => w.Contains('$')), high.Where(w => w.Contains('$')));
        Assert.Contains("1973", high);
        Assert.Contains("10000", high);
        Assert.DoesNotContain("10,000", high);
        Assert.DoesNotContain("1,973", high);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public void Pdf_DailyFeatureMath_RendersIdentically_WhateverTheCounts(AssociatePassdownTemplate template)
    {
        // Math is drawn as vector paths (no extractable glyphs), so compare the non-count words and the
        // page count of two renders that differ only in the counts.
        const string feature = @"Solve \(x^2 = 1600\) before close";
        var lowPdf = Render(WithCounts(18500, 4200, feature), template);
        var highPdf = Render(WithCounts(38500, 21240, feature), template);

        var high = AllWords(highPdf).Select(w => w switch { "38,500" => "18,500", "21,240" => "4,200", _ => w }).ToList();
        Assert.Equal(AllWords(lowPdf), high);
        Assert.Equal(PdfInspector.CountPages(lowPdf), PdfInspector.CountPages(highPdf));
    }

    [Fact]
    public void PdfFormatting_UsesTheSharedCountRule()
    {
        Assert.Equal("38,500", PdfFormatting.FormatCount(38500));
        Assert.Equal("21,240", PdfFormatting.FormatCount(21240));
        Assert.Equal("0", PdfFormatting.FormatCount(0));
        Assert.Equal("—", PdfFormatting.FormatCount(null));
    }

    [Fact]
    public void TeamsCopy_GroupsTheExistingCountLines()
    {
        var summary = AssociateTeamsTextFormatter.FormatOperationsSummary(AssociatePassdownDocumentModelBuilder.Build(WithCounts(38500, 21240), GeneratedAt));

        Assert.Contains("Projected Attendance: 38,500", summary);
        Assert.Contains("Pre-solds: 21,240", summary);
    }

    [Fact]
    public void TeamsCopy_OnlyTheCountTextDiffers_AndNoFieldIsAdded()
    {
        var low = AssociateTeamsTextFormatter.FormatOperationsSummary(AssociatePassdownDocumentModelBuilder.Build(WithCounts(18500, 4200), GeneratedAt));
        var high = AssociateTeamsTextFormatter.FormatOperationsSummary(AssociatePassdownDocumentModelBuilder.Build(WithCounts(38500, 21240), GeneratedAt));

        Assert.Equal(low, high.Replace("38,500", "18,500").Replace("21,240", "4,200"));

        var missing = AssociateTeamsTextFormatter.FormatOperationsSummary(AssociatePassdownDocumentModelBuilder.Build(WithCounts(null, null), GeneratedAt));
        Assert.DoesNotContain("Projected Attendance", missing);
        Assert.DoesNotContain("Pre-solds", missing);
    }

    [Fact]
    public void TeamsCopy_Pricing_IsIdenticalWhateverTheCounts()
    {
        var low = AssociateTeamsTextFormatter.FormatPricing(AssociatePassdownDocumentModelBuilder.Build(WithCounts(18500, 4200), GeneratedAt));
        var high = AssociateTeamsTextFormatter.FormatPricing(AssociatePassdownDocumentModelBuilder.Build(WithCounts(38500, 21240), GeneratedAt));

        Assert.Equal(low, high);
    }

    [Fact]
    public void FinalizedSnapshot_StoresThePlainInteger_NotTheDisplayText()
    {
        var snapshot = new FinalizedPassdownSnapshotBuilder().Build(WithCounts(38500, 21240), GeneratedAt);

        Assert.Equal(38500, snapshot.Attendance.ProjectedAttendance);
        Assert.Equal(21240, snapshot.Attendance.PreSolds);

        var json = FinalizedPassdownSnapshotSerializer.Serialize(snapshot);
        Assert.Contains("38500", json);
        Assert.DoesNotContain("38,500", json);
    }
}
