using Carowinds.Passdown.Application.DailyFeatureMath;
using Carowinds.Passdown.Application.SpellCheck;

namespace Carowinds.Passdown.Application.Tests.DailyFeatureMath;

/// <summary>Phase 21D: which parts of a Daily Feature are math, and which stay ordinary text.</summary>
public class DailyFeatureMathTextTests
{
    [Theory]
    [InlineData("Question of the Day:\nWhat is your favorite roller coaster?")]
    [InlineData("Fact of the Day:\nCopperhead Strike opened in 2019.")]
    [InlineData("A $5 coupon, C:\\Parks\\file_name, x^2 and a_b stay as typed.")]
    [InlineData("∫₀¹ x² dx = ⅓")]
    [InlineData("")]
    public void OrdinaryText_IsOneTextSegment_ExactlyAsTyped(string text)
    {
        var analysis = DailyFeatureMathText.Analyze(text);

        Assert.False(analysis.ContainsMathDelimiters);
        Assert.False(analysis.HasRenderableMath);
        Assert.Empty(analysis.Issues);
        Assert.Equal(text, string.Concat(analysis.Segments.Select(segment => segment.Source)));
        Assert.All(analysis.Segments, segment => Assert.Equal(MathSegmentKind.Text, segment.Kind));
    }

    [Fact]
    public void InlineMath_IsRecognized_BetweenItsDelimiters()
    {
        var analysis = DailyFeatureMathText.Analyze(@"The derivative is \( \frac{d}{dx}x^2=2x \).");

        Assert.Equal([MathSegmentKind.Text, MathSegmentKind.InlineMath, MathSegmentKind.Text], analysis.Segments.Select(s => s.Kind));
        var math = analysis.Segments[1];
        Assert.Equal(@"\( \frac{d}{dx}x^2=2x \)", math.Source);
        Assert.Equal(@" \frac{d}{dx}x^2=2x ", math.Content);
        Assert.True(math.IsRenderableMath);
        Assert.Equal(".", analysis.Segments[2].Source);
    }

    [Fact]
    public void DisplayMath_MaySpanLines()
    {
        var analysis = DailyFeatureMathText.Analyze("Before\n\\[\n\\int_0^1 x^2\\,dx=\\frac{1}{3}\n\\]\nAfter");

        var display = Assert.Single(analysis.Segments, s => s.IsMath);
        Assert.Equal(MathSegmentKind.DisplayMath, display.Kind);
        Assert.True(display.IsRenderableMath);
    }

    [Fact]
    public void MultipleRegions_MixedWithProse_KeepTheirOrderAndOffsets()
    {
        const string text = @"Since \(a=1\) and \[b=2\], then \(a+b=3\).";
        var analysis = DailyFeatureMathText.Analyze(text);

        Assert.Equal(3, analysis.Segments.Count(s => s.IsMath));
        foreach (var segment in analysis.Segments)
        {
            Assert.Equal(segment.Source, text.Substring(segment.Start, segment.Length));
        }

        Assert.Equal(text, string.Concat(analysis.Segments.Select(s => s.Source)));
    }

    [Fact]
    public void EscapedDelimiter_DoubledBackslash_IsLiteralText()
    {
        var analysis = DailyFeatureMathText.Analyze(@"Type \\( to show a delimiter.");

        Assert.DoesNotContain(analysis.Segments, s => s.IsMath);
        Assert.Empty(analysis.Issues);
    }

    [Theory]
    [InlineData(@"Unclosed \(x+1 here")]
    [InlineData(@"Unclosed \[x+1 here")]
    [InlineData(@"Mismatched \(x+1\] here")]
    [InlineData(@"Nested \(a \(b\)")]
    public void UnclosedOrMismatchedOpener_IsTextWithAnIssue(string text)
    {
        var analysis = DailyFeatureMathText.Analyze(text);

        Assert.True(analysis.ContainsMathDelimiters);
        Assert.Contains(analysis.Issues, issue => issue.Kind == MathIssueKind.UnclosedDelimiter);
        Assert.Equal(text, string.Concat(analysis.Segments.Select(s => s.Source)));
    }

    [Fact]
    public void AFailedRegion_IsKeptAsItsLiteralSource_OthersStillRender()
    {
        var analysis = DailyFeatureMathText.Analyze(@"Bad \(\frac{1}{\) and \(\foo\), good \(a+b\).");

        var math = analysis.Segments.Where(s => s.IsMath).ToList();
        Assert.Equal(3, math.Count);
        Assert.False(math[0].IsRenderableMath);
        Assert.False(math[1].IsRenderableMath);
        Assert.True(math[2].IsRenderableMath);
        Assert.Contains(analysis.Issues, i => i.Kind == MathIssueKind.UnbalancedBraces);
        Assert.Contains(analysis.Issues, i => i.Kind == MathIssueKind.UnsupportedCommand);
        Assert.True(analysis.HasRenderableMath);
    }

    [Fact]
    public void TooLongText_IsNotAnalyzed_AndStaysPlain()
    {
        var text = @"\(x\) " + new string('a', DailyFeatureMathText.MaxAnalyzedLength);
        var analysis = DailyFeatureMathText.Analyze(text);

        Assert.False(analysis.HasRenderableMath);
        Assert.Contains(analysis.Issues, i => i.Kind == MathIssueKind.ResourceLimitExceeded);
        Assert.Equal(text, Assert.Single(analysis.Segments).Source);
    }

    [Fact]
    public void RegionsBeyondTheLimit_AreShownAsTyped()
    {
        var text = string.Concat(Enumerable.Repeat(@"\(x\) ", DailyFeatureMathText.MaxMathRegions + 5));
        var analysis = DailyFeatureMathText.Analyze(text);

        var math = analysis.Segments.Where(s => s.IsMath).ToList();
        Assert.Equal(DailyFeatureMathText.MaxMathRegions, math.Count(s => s.IsRenderableMath));
        Assert.Equal(5, math.Count(s => !s.IsRenderableMath));
        Assert.Contains(analysis.Issues, i => i.Kind == MathIssueKind.ResourceLimitExceeded);
    }

    [Fact]
    public void Mask_BlanksMathOnly_PreservingOffsetsAndLineBreaks()
    {
        const string text = "Teh value \\(\\frac{a}{b}\\) is\n\\[\n\\sqrt{x}\n\\] corect";
        var masked = DailyFeatureMathText.MaskMathForSpellCheck(text);

        Assert.Equal(text.Length, masked.Length);
        Assert.DoesNotContain("frac", masked);
        Assert.DoesNotContain("sqrt", masked);
        Assert.StartsWith("Teh value ", masked);
        Assert.EndsWith(" corect", masked);
        Assert.Equal(text.Count(c => c == '\n'), masked.Count(c => c == '\n'));
    }

    [Theory]
    [InlineData("Plain text only")]
    [InlineData(@"Unclosed \(frac")]
    [InlineData(null)]
    public void Mask_LeavesTextWithoutClosedMath_Unchanged(string? text) =>
        Assert.Equal(text ?? string.Empty, DailyFeatureMathText.MaskMathForSpellCheck(text));

    [Fact]
    public void SpellCheck_OfMaskedText_ChecksProse_NotLatexCommands()
    {
        var service = new SpellCheckService(new Tests.SpellCheck.FakeSpellingBackend("the", "derivative", "is", "and", "value"));
        const string text = @"The derivitive is \( \frac{d}{dx} dist + spd \) and the valeu \[ \sum_{i=1}^{n} rate \]";

        var words = service.Check(DailyFeatureMathText.MaskMathForSpellCheck(text)).Select(issue => issue.Word).ToList();

        Assert.Equal(["derivitive", "valeu"], words);
        var unmasked = service.Check(text).Select(issue => issue.Word).ToList();
        Assert.Contains("dist", unmasked); // what masking prevents: variable names inside math
    }

    [Fact]
    public void SpellCheck_DomainAllowList_StillAppliesAroundMath()
    {
        var service = new SpellCheckService(new Tests.SpellCheck.FakeSpellingBackend("rides", "at"));
        var issues = service.Check(DailyFeatureMathText.MaskMathForSpellCheck(@"Copperhead rides \(v^2\) at Carowinds"));

        Assert.Empty(issues);
    }
}
