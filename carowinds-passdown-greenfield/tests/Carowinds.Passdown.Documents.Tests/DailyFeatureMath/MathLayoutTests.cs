using Carowinds.Passdown.Application.DailyFeatureMath;
using Carowinds.Passdown.Documents.DailyFeatureMath;

namespace Carowinds.Passdown.Documents.Tests.DailyFeatureMath;

/// <summary>
/// Phase 21D: structural checks of the shared math layout — where things actually land (box
/// geometry, y downward, baseline 0), not just that something rendered.
/// </summary>
public class MathLayoutTests
{
    private static readonly MathFontSet Fonts = MathFontSet.Shared ?? throw new InvalidOperationException("Bundled math fonts failed to load.");
    private static readonly MathLayoutEngine Engine = new(Fonts);

    private static MathBox Layout(string source, MathStyle style = MathStyle.Text)
    {
        var parse = MathParser.Parse(source);
        Assert.True(parse.Succeeded, parse.Issue?.Message);
        return Engine.Layout(parse.Root!, style);
    }

    private static ushort Glyph(int codePoint)
    {
        Assert.True(Fonts.Math.TryGetGlyph(codePoint, out var glyph), $"U+{codePoint:X} missing");
        return glyph;
    }

    private static MathGlyphItem Find(MathBox box, int codePoint) =>
        Assert.Single(box.Items.OfType<MathGlyphItem>(), g => g.Font == Fonts.Math && g.Glyph == Glyph(codePoint));

    private const int ItalicA = 0x1D44E;
    private const int ItalicB = 0x1D44F;
    private const int ItalicX = 0x1D465;

    [Fact]
    public void BundledFonts_Load_AndCoverMathSymbols()
    {
        foreach (var codePoint in new[] { '∫', '∑', '∏', '∂', '∇', '∞', '≤', '≠', '√', 'α', 'Ω', '⇒', '∈', ItalicX, 0x210E, 0x211D })
        {
            Glyph(codePoint);
        }

        Assert.True(Fonts.ProseItalic.TryGetGlyph('a', out _));
        Assert.NotEmpty(Fonts.Math.GetOutline(Glyph('∫')));
        Assert.InRange(Fonts.Math.UnitsPerEm, 500, 4096);
    }

    [Fact]
    public void Variables_UseMathItalic_DigitsUpright()
    {
        var box = Layout("x2");
        Find(box, ItalicX);
        Find(box, '2');
    }

    [Fact]
    public void Fraction_PutsNumeratorAboveTheRule_AndDenominatorBelow()
    {
        var box = Layout(@"\frac{a}{b}");
        var numerator = Find(box, ItalicA);
        var denominator = Find(box, ItalicB);
        var rule = Assert.Single(box.Items.OfType<MathRectItem>());

        Assert.True(numerator.Y < rule.Y, "numerator baseline must be above the rule");
        Assert.True(denominator.Y > rule.Y + rule.Height, "denominator must be below the rule");
        Assert.InRange(-(rule.Y + (rule.Height / 2)), 0.2, 0.32); // centered on the math axis
        Assert.True(box.Height > 0.5 && box.Depth > 0.3);
    }

    [Fact]
    public void NestedFraction_IsTallerThanASimpleOne()
    {
        var simple = Layout(@"\frac{a}{b}");
        var nested = Layout(@"\frac{1}{1+\frac{1}{x}}");
        Assert.True(nested.Depth > simple.Depth);
        Assert.Equal(2, nested.Items.OfType<MathRectItem>().Count());
    }

    [Fact]
    public void Superscript_IsRaisedAndSmaller_SubscriptLowered()
    {
        var box = Layout("x_a^b");
        var x = Find(box, ItalicX);
        var sub = Find(box, ItalicA);
        var sup = Find(box, ItalicB);

        Assert.True(sup.Y < -0.25, "superscript raised");
        Assert.True(sub.Y > 0.15, "subscript lowered");
        Assert.True(sup.Size < x.Size && sub.Size < x.Size);
        Assert.True(sup.X > x.X && sub.X > x.X);
    }

    [Fact]
    public void DisplaySum_StacksItsLimitsAboveAndBelow()
    {
        var box = Layout(@"\sum_{a}^{b}", MathStyle.Display);
        var sigma = Find(box, '∑');
        var below = Find(box, ItalicA);
        var above = Find(box, ItalicB);
        var sigmaMetrics = Fonts.Math.GetMetrics(sigma.Glyph);
        var sigmaTop = sigma.Y - (sigmaMetrics.YMax * sigma.Size);
        var sigmaBottom = sigma.Y - (sigmaMetrics.YMin * sigma.Size);

        Assert.True(above.Y < sigmaTop, "upper limit above Σ");
        Assert.True(below.Y > sigmaBottom, "lower limit below Σ");
        var sigmaCenter = sigma.X + (sigmaMetrics.Advance * sigma.Size / 2);
        Assert.InRange(above.X, sigmaCenter - 0.5, sigmaCenter);
        Assert.True(sigma.Size > 1.2, "display Σ is enlarged");
    }

    [Fact]
    public void InlineSum_KeepsItsScriptsAtTheSide()
    {
        var box = Layout(@"\sum_{a}^{b}", MathStyle.Text);
        var sigma = Find(box, '∑');
        var sigmaRight = sigma.X + (Fonts.Math.GetMetrics(sigma.Glyph).Advance * sigma.Size);
        Assert.True(Find(box, ItalicB).X >= sigmaRight - 0.01);
    }

    [Fact]
    public void DisplayIntegral_KeepsLimitsAtTheSide_AndIsEnlarged()
    {
        var box = Layout(@"\int_a^b", MathStyle.Display);
        var integral = Find(box, '∫');
        Assert.True(integral.Size > 1.5);
        Assert.True(Find(box, ItalicB).X > integral.X);
        Assert.True(Find(box, ItalicB).Y < 0 && Find(box, ItalicA).Y > 0);
    }

    [Fact]
    public void Radical_BarSitsAboveTheBody()
    {
        var box = Layout(@"\sqrt{x}");
        var body = Find(box, ItalicX);
        var bar = Assert.Single(box.Items.OfType<MathRectItem>());
        var bodyTop = body.Y - (Fonts.Math.GetMetrics(body.Glyph).YMax * body.Size);

        Assert.True(bar.Y + bar.Height <= bodyTop, "bar clears the body");
        Assert.Equal(3, box.Items.OfType<MathStrokeItem>().Count());
        Assert.True(bar.X + bar.Width >= body.X + (Fonts.Math.GetMetrics(body.Glyph).Advance * body.Size));
    }

    [Fact]
    public void NthRoot_PlacesTheIndexUpperLeft_InScriptScriptSize()
    {
        var box = Layout(@"\sqrt[3]{x}");
        var index = Find(box, '3');
        var body = Find(box, ItalicX);
        Assert.True(index.X < body.X && index.Y < 0);
        Assert.Equal(0.5, index.Size, 3);
    }

    [Fact]
    public void Relations_GetMoreSpaceThanAdjacentLetters()
    {
        var tight = Layout("ab");
        var spaced = Layout("a=b");
        var equalsAdvance = Fonts.Math.GetMetrics(Glyph('=')).Advance;
        Assert.True(spaced.Width > tight.Width + equalsAdvance + 0.4, "thick space on both sides of '='");
    }

    [Fact]
    public void LeadingMinus_IsUnary_NoBinarySpacing()
    {
        var unary = Layout("-1");
        var minusAdvance = Fonts.Math.GetMetrics(Glyph('−')).Advance;
        var oneAdvance = Fonts.Math.GetMetrics(Glyph('1')).Advance;
        Assert.Equal(minusAdvance + oneAdvance, unary.Width, 3);
    }

    [Fact]
    public void GrowingDelimiters_CoverATallFraction()
    {
        var box = Layout(@"\left(\frac{a}{b}\right)", MathStyle.Display);
        var paren = box.Items.OfType<MathGlyphItem>().First(g => g.Glyph == Glyph('('));
        Assert.True(paren.ScaleY > 1.2);
    }

    [Fact]
    public void Matrix_LaysOutRowsAndColumns()
    {
        var box = Layout(@"\begin{pmatrix} a & b \\ x & 2 \end{pmatrix}");
        var a = Find(box, ItalicA);
        var b = Find(box, ItalicB);
        var x = Find(box, ItalicX);
        Assert.Equal(a.Y, b.Y, 6);
        Assert.True(x.Y > a.Y);
        Assert.True(b.X > a.X);
    }

    [Fact]
    public void BreakablePieces_JoinToTheSameWidthAsTheWholeRow()
    {
        var parse = MathParser.Parse("a+b+x=a-b");
        var whole = Engine.Layout(parse.Root!, MathStyle.Text);
        var pieces = Engine.LayoutBreakablePieces(parse.Root!, MathStyle.Text);

        Assert.Equal(5, pieces.Count);
        Assert.Equal(whole.Width, pieces.Sum(p => p.Width), 6);
    }
}
