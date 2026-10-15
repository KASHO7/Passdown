using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

namespace Carowinds.Passdown.Documents.Tests.Associate.Templates;

/// <summary>
/// Phase 18F: with no real Daily Feature text, every template renders nothing for the section — no
/// feature-type heading, no "Daily Feature" fallback heading, no "Not entered." placeholder, no rule,
/// and no reserved space (the following product section moves up). A selected type alone is not
/// content. With text, the section still renders under its type heading.
/// </summary>
public sealed class DailyFeatureOmissionTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 6, 8, 0, TimeSpan.Zero);
    private static readonly IAssociatePassdownPdfGenerator Generator = new AssociatePassdownPdfGenerator();

    /// <summary>The first word of the first product section heading in NormalDay ("DAILY ADMISSION") — upper-case in all three templates.</summary>
    private const string FollowingSectionWord = "ADMISSION";

    public static TheoryData<AssociatePassdownTemplate, string?> EmptyTextCases()
    {
        var data = new TheoryData<AssociatePassdownTemplate, string?>();
        foreach (var template in Enum.GetValues<AssociatePassdownTemplate>())
        {
            data.Add(template, null);
            data.Add(template, "");
            data.Add(template, "   \r\n\t ");
        }

        return data;
    }

    public static TheoryData<AssociatePassdownTemplate> AllTemplates() =>
        new(Enum.GetValues<AssociatePassdownTemplate>());

    private static AssociateWorkspace NormalDayWith(DailyFeatureType? type, string? text)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        return workspace with { Draft = workspace.Draft with { DailyFeatureType = type, DailyFeatureText = text } };
    }

    private static byte[] Render(AssociatePassdownDocumentModel model, AssociatePassdownTemplate template)
    {
        var result = Generator.Generate(model, template);
        Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
        return result.PdfBytes!;
    }

    private static byte[] Render(AssociateWorkspace workspace, AssociatePassdownTemplate template) =>
        Render(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt), template);

    [Theory]
    [MemberData(nameof(EmptyTextCases))]
    public void TypeSelected_NoText_SectionIsOmittedEntirely(AssociatePassdownTemplate template, string? text)
    {
        var bytes = Render(NormalDayWith(DailyFeatureType.QuestionOfTheDay, text), template);
        var pdfText = PdfInspector.ExtractAllText(bytes);

        Assert.DoesNotContain("QUESTION", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Daily Feature", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Not entered", pdfText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void NoTypeAndNoText_SectionIsOmittedEntirely(AssociatePassdownTemplate template)
    {
        var pdfText = PdfInspector.ExtractAllText(Render(NormalDayWith(null, null), template));

        Assert.DoesNotContain("Daily Feature", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Not entered", pdfText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void EveryFeatureType_WithoutText_IsOmitted(AssociatePassdownTemplate template)
    {
        foreach (var (type, word) in new[]
                 {
                     (DailyFeatureType.QuoteOfTheDay, "QUOTE"),
                     (DailyFeatureType.QuestionOfTheDay, "QUESTION"),
                     (DailyFeatureType.UselessFactOfTheDay, "USELESS"),
                 })
        {
            var pdfText = PdfInspector.ExtractAllText(Render(NormalDayWith(type, " "), template));
            Assert.DoesNotContain(word, pdfText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void WithText_SectionStillRendersUnderItsTypeHeading(AssociatePassdownTemplate template)
    {
        var pdfText = PdfInspector.ExtractAllText(Render(NormalDayWith(DailyFeatureType.UselessFactOfTheDay, "Fury 325 is 325 feet tall."), template));

        Assert.Contains("USELESS FACT OF THE DAY", pdfText.ToUpperInvariant());
        Assert.Contains("Fury 325 is 325 feet tall.", pdfText);
    }

    /// <summary>The following section moves up when the feature is omitted.</summary>
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Omitted_FollowingSectionMovesUp(AssociatePassdownTemplate template)
    {
        var populated = Render(NormalDayWith(DailyFeatureType.QuestionOfTheDay, "What year did Carowinds open?"), template);
        var empty = Render(NormalDayWith(DailyFeatureType.QuestionOfTheDay, null), template);

        var populatedTop = PdfInspector.FindWordTop(populated, 1, FollowingSectionWord);
        var emptyTop = PdfInspector.FindWordTop(empty, 1, FollowingSectionWord);

        Assert.NotNull(populatedTop);
        Assert.NotNull(emptyTop);

        // PDF space: a larger Top is higher on the page.
        Assert.True(emptyTop > populatedTop + 10, $"Expected the next section to move up: populated {populatedTop}, empty {emptyTop}.");
        Assert.True(PdfInspector.CountPages(empty) <= PdfInspector.CountPages(populated));
    }

    /// <summary>
    /// No empty divider/spacing remains: a selected type with no text lays out at exactly the same
    /// position as a model with no Daily Feature at all — the type label reserves nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Omitted_LeavesNoReservedSpace_SameLayoutAsNoFeatureAtAll(AssociatePassdownTemplate template)
    {
        var typeOnly = AssociatePassdownDocumentModelBuilder.Build(NormalDayWith(DailyFeatureType.QuoteOfTheDay, "  "), GeneratedAt);
        var none = typeOnly with { DailyFeature = new PdfDailyFeature(null, null) };

        Assert.Equal(
            PdfInspector.FindWordTop(Render(none, template), 1, FollowingSectionWord),
            PdfInspector.FindWordTop(Render(typeOnly, template), 1, FollowingSectionWord));
        Assert.Equal(
            PdfInspector.Words(Render(none, template), 1),
            PdfInspector.Words(Render(typeOnly, template), 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \t")]
    public void Model_HasContent_RequiresNonWhitespaceText(string? text)
    {
        Assert.False(new PdfDailyFeature("Quote of the Day", text).HasContent);
        Assert.False(new PdfDailyFeature(null, text).HasContent);
    }

    [Fact]
    public void Model_HasContent_TrueWithText()
    {
        Assert.True(new PdfDailyFeature("Quote of the Day", "Smile.").HasContent);
        Assert.True(new PdfDailyFeature(null, "Smile.").HasContent);
    }

    [Fact]
    public void Preflight_OmittedFeature_StillFitsOnePage()
    {
        var empty = AssociatePassdownDocumentModelBuilder.Build(NormalDayWith(DailyFeatureType.QuoteOfTheDay, null), GeneratedAt);

        Assert.Equal(AssociatePassdownPreflightStatus.FitsOnePage, new AssociatePassdownPreflightService().Evaluate(empty).Status);
    }
}
