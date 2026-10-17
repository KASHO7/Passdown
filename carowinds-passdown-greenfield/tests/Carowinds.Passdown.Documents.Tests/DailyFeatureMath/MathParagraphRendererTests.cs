using System.Diagnostics;
using Carowinds.Passdown.Application.DailyFeatureMath;
using Carowinds.Passdown.Documents.DailyFeatureMath;

namespace Carowinds.Passdown.Documents.Tests.DailyFeatureMath;

/// <summary>Phase 21D: mixed prose + math paragraphs — wrapping, fitting, fallback, caching and the SVG output.</summary>
public class MathParagraphRendererTests
{
    private static readonly MathParagraphStyle Style = new(10, 1.2, ItalicProse: true);

    private static MathParagraphRenderer Renderer() =>
        new(MathFontSet.Shared ?? throw new InvalidOperationException("Bundled math fonts failed to load."));

    private static (double MinX, double MaxX, double MinY, double MaxY) Extent(MathRenderedLine line)
    {
        var points = line.Fill.Where(c => c.Verb != PathVerb.Close).Select(c => (c.X, c.Y))
            .Concat(line.Strokes.SelectMany(s => s.Points)).ToList();
        return points.Count == 0 ? (0, 0, 0, 0) : (points.Min(p => p.X), points.Max(p => p.X), points.Min(p => p.Y), points.Max(p => p.Y));
    }

    [Fact]
    public void Shared_IsAvailable_OfflineFromEmbeddedFonts()
    {
        Assert.NotNull(MathFontSet.Shared);
        Assert.NotNull(MathParagraphRenderer.Shared);
        Assert.DoesNotContain(typeof(MathParagraphRenderer).Assembly.GetReferencedAssemblies(), a => a.Name is "System.Net.Http" or "Microsoft.Playwright");
    }

    [Theory]
    [InlineData(@"The derivative of \(x^2\) is \(2x\).", 1)]
    [InlineData("Text before\n\\[\n\\int_0^1 x^2\\,dx=\\frac13\n\\]\nText after", 3)]
    public void MixedProseAndMath_LaysOutTheExpectedLines(string text, int lines)
    {
        var paragraph = Renderer().Render(text, 540, Style)!;
        Assert.Equal(lines, paragraph.Lines.Count);
        Assert.All(paragraph.Lines, line => Assert.NotEmpty(line.Fill));
    }

    [Fact]
    public void InlineMath_SharesTheProseBaseline_WithoutBecomingADisplayEquation()
    {
        var paragraph = Renderer().Render(@"The derivative of \(x^2\) is \(2x\).", 540, Style)!;
        var line = Assert.Single(paragraph.Lines);
        var (minX, _, _, _) = Extent(line);
        Assert.InRange(minX, 0, 2); // starts at the left margin, not centered
        Assert.InRange(line.Height, 10, 16); // an ordinary line, not a tall display block
    }

    [Fact]
    public void DisplayMath_IsCentered_WithBalancedSpace()
    {
        var paragraph = Renderer().Render("Above\n\\[a+b=c\\]\nBelow", 400, Style)!;
        var display = paragraph.Lines[1];
        var (minX, maxX, _, _) = Extent(display);
        Assert.InRange((minX + maxX) / 2, 190, 210);
        Assert.InRange(display.Height, 12, 22);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(160)]
    [InlineData(540)]
    public void NothingIsEverWiderThanTheLine(double width)
    {
        var text = @"A long one: \(a_1+a_2+a_3+a_4+a_5+a_6+a_7+a_8+a_9+a_{10}+a_{11}+a_{12}+a_{13}+a_{14}+a_{15}+a_{16}+a_{17}+a_{18}\) and "
            + @"\[\int_0^{\infty} e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2} = \sum_{n=0}^{\infty} \frac{(-1)^n}{n!(2n+1)} \cdot \frac{1}{1+\frac{1}{1+\frac{1}{x}}}\]"
            + " Supercalifragilisticexpialidociousextraordinarilylongword.";
        var paragraph = Renderer().Render(text, width, Style)!;

        foreach (var line in paragraph.Lines)
        {
            var (minX, maxX, minY, maxY) = Extent(line);
            Assert.True(minX >= -0.5 && maxX <= width + 0.5, $"line spans {minX:0.0}..{maxX:0.0} in {width}");
            Assert.True(minY >= -0.5 && maxY <= line.Height + 0.5, $"line ink {minY:0.0}..{maxY:0.0} outside height {line.Height:0.0}");
        }
    }

    [Fact]
    public void MalformedMath_IsShownAsTyped_AndReported()
    {
        var paragraph = Renderer().Render(@"Bad \(\frac{1}{\) and \(\foo\) but \(a+b\) works.", 540, Style)!;
        Assert.Contains(paragraph.Issues, i => i.Kind == MathIssueKind.UnbalancedBraces);
        Assert.Contains(paragraph.Issues, i => i.Kind == MathIssueKind.UnsupportedCommand);
        Assert.NotEmpty(paragraph.Lines);
    }

    [Fact]
    public void Cache_IsBounded_AndReusesIdenticalLayouts()
    {
        var renderer = Renderer();
        var first = renderer.Render(@"\(x\)", 300, Style);
        Assert.Same(first, renderer.Render(@"\(x\)", 300, Style));
        Assert.NotSame(first, renderer.Render(@"\(x\)", 250, Style));

        for (var i = 0; i < MathParagraphRenderer.CacheCapacity + 40; i++)
        {
            renderer.Render($@"\(x^{{{i}}}\)", 300, Style);
        }

        Assert.Equal(MathParagraphRenderer.CacheCapacity, renderer.CachedCount);
        renderer.ClearCache();
        Assert.Equal(0, renderer.CachedCount);
    }

    [Fact]
    public void Svg_IsPathsOnly_NoTextImagesScriptsOrReferences()
    {
        var paragraph = Renderer().Render(@"Root \(\sqrt{x^2+1}\) and \(\frac{a}{b}\)", 540, Style)!;
        var svg = MathParagraphRenderer.ToSvg(Assert.Single(paragraph.Lines), "#111111");

        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg);
        foreach (var forbidden in new[] { "<text", "<image", "<script", "href", "<use", "url(", "<foreign" })
        {
            Assert.DoesNotContain(forbidden, svg, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("stroke-linecap=\"round\"", svg); // the radical sign
        Assert.Contains("fill=\"#111111\"", svg);
    }

    [Fact]
    public void PathologicalInput_RendersQuickly_WithinLimits()
    {
        var renderer = Renderer();
        var watch = Stopwatch.StartNew();
        var huge = string.Concat(Enumerable.Repeat(@"\(\frac{\sqrt{x^2+y^2}}{\sum_{i=1}^{n} a_i}\) ", DailyFeatureMathText.MaxMathRegions + 10));
        var deep = @"\(" + string.Concat(Enumerable.Repeat(@"\frac{1}{1+", 11)) + "x" + new string('}', 22) + @"\)";
        var tall = @"\[\begin{matrix}" + string.Join(@" \\ ", Enumerable.Repeat(@"\frac{a}{b} & \frac{a}{b}", 10)) + @"\end{matrix}\]";

        foreach (var text in new[] { huge, deep, tall })
        {
            var paragraph = renderer.Render(text, 300, Style);
            Assert.NotNull(paragraph);
            Assert.All(paragraph.Lines, line => Assert.True(line.Height <= Style.FontSize * 26, $"line height {line.Height}"));
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public void UnicodeSymbols_InProse_AreDrawnFromTheBundledMathFont()
    {
        var paragraph = Renderer().Render(@"∫₀¹ x² dx = ⅓ and ∇·E with \(x\)", 540, Style)!;
        Assert.Single(paragraph.Lines);
        Assert.NotEmpty(paragraph.Lines[0].Fill);
    }
}
