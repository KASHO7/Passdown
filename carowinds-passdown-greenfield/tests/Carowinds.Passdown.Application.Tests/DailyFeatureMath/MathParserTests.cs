using Carowinds.Passdown.Application.DailyFeatureMath;

namespace Carowinds.Passdown.Application.Tests.DailyFeatureMath;

/// <summary>Phase 21D: the supported LaTeX-style subset, and safe failure for everything else.</summary>
public class MathParserTests
{
    private static MathRow Parse(string source)
    {
        var result = MathParser.Parse(source);
        Assert.True(result.Succeeded, result.Issue?.Message);
        return Assert.IsType<MathRow>(result.Root);
    }

    private static MathIssue Fail(string source)
    {
        var result = MathParser.Parse(source);
        Assert.False(result.Succeeded);
        return result.Issue!;
    }

    [Theory]
    [InlineData("x^2")]
    [InlineData("x_i")]
    [InlineData(@"\frac{a}{b}")]
    [InlineData(@"\frac13")]
    [InlineData(@"\sqrt{x}")]
    [InlineData(@"\sqrt[3]{8}")]
    [InlineData(@"\int_0^1 x^2\,dx")]
    [InlineData(@"\frac{d}{dx}x^2")]
    [InlineData(@"\frac{\partial f}{\partial x}")]
    [InlineData(@"\sum_{i=1}^{n} i")]
    [InlineData(@"\prod_{k=1}^{n} k")]
    [InlineData(@"\lim_{x\to 0}\frac{\sin x}{x}")]
    [InlineData(@"\alpha+\beta\le\Gamma \cdot \Omega")]
    [InlineData(@"\left( \frac{a}{b} \right)^2")]
    [InlineData(@"\left\{ x \right.")]
    [InlineData(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}")]
    [InlineData(@"\begin{bmatrix} 1 & 0 \\ 0 & 1 \\ \end{bmatrix}")]
    [InlineData(@"f'(x) = \text{rate of change}")]
    [InlineData(@"x \in \mathbb{R}, \forall \epsilon > 0 \; \exists \delta")]
    [InlineData(@"a \Rightarrow b \iff c \to \infty")]
    [InlineData(@"\hat{x} + \bar{y} + \vec{v} + \overline{AB}")]
    [InlineData(@"\binom{n}{k}")]
    [InlineData(@"\sum\limits_{i} x_i \quad \int\nolimits_a^b")]
    [InlineData(@"\operatorname{sgn} x")]
    [InlineData("∫₀¹ x² dx = ⅓")]
    [InlineData("")]
    public void SupportedSyntax_Parses(string source) => Parse(source);

    [Fact]
    public void Fraction_HasNumeratorAndDenominator()
    {
        var fraction = Assert.IsType<MathFraction>(Assert.Single(Parse(@"\frac{a}{b}").Items));
        Assert.True(fraction.HasRule);
        Assert.Equal("a", Assert.IsType<MathSymbol>(Assert.Single(Assert.IsType<MathRow>(fraction.Numerator).Items)).Text);
        Assert.Equal("b", Assert.IsType<MathSymbol>(Assert.Single(Assert.IsType<MathRow>(fraction.Denominator).Items)).Text);
    }

    [Fact]
    public void Scripts_AttachToTheirBase()
    {
        var scripts = Assert.IsType<MathScripts>(Assert.Single(Parse("x_i^2").Items));
        Assert.Equal("x", Assert.IsType<MathSymbol>(scripts.Base).Text);
        Assert.NotNull(scripts.Subscript);
        Assert.NotNull(scripts.Superscript);
    }

    [Fact]
    public void Integral_IsALargeOperator_WithSideScripts()
    {
        var scripts = Assert.IsType<MathScripts>(Parse(@"\int_0^1 x").Items[0]);
        var op = Assert.IsType<MathLargeOperator>(scripts.Base);
        Assert.True(op.IsIntegral);
        Assert.Equal("∫", op.Symbol);
    }

    [Fact]
    public void Letters_AreItalicVariables_DigitsAndFunctionNamesUpright()
    {
        var row = Parse(@"x 2 \sin");
        Assert.Equal(MathGlyphStyle.Italic, Assert.IsType<MathSymbol>(row.Items[0]).Style);
        Assert.Equal(MathGlyphStyle.Upright, Assert.IsType<MathSymbol>(row.Items[1]).Style);
        Assert.Equal("sin", Assert.IsType<MathFunctionName>(row.Items[2]).Name);
    }

    [Fact]
    public void Minus_IsTheTrueMinusSign_AndABinaryOperator()
    {
        var minus = Assert.IsType<MathSymbol>(Parse("a-b").Items[1]);
        Assert.Equal("−", minus.Text);
        Assert.Equal(MathAtomClass.Bin, minus.Class);
    }

    [Fact]
    public void Matrix_KeepsRowsAndColumns_AndDropsATrailingRowBreak()
    {
        var matrix = Assert.IsType<MathMatrix>(Assert.Single(Parse(@"\begin{bmatrix} 1 & 0 \\ 0 & 1 \\ \end{bmatrix}").Items));
        Assert.Equal(2, matrix.Rows.Count);
        Assert.All(matrix.Rows, row => Assert.Equal(2, row.Count));
        Assert.Equal("[", matrix.Left);
    }

    [Theory]
    [InlineData(@"\foo{x}", MathIssueKind.UnsupportedCommand)]
    [InlineData(@"\input{secrets}", MathIssueKind.UnsupportedCommand)]
    [InlineData(@"\def\x{1}", MathIssueKind.UnsupportedCommand)]
    [InlineData(@"\includegraphics{http://example.com/a.png}", MathIssueKind.UnsupportedCommand)]
    [InlineData(@"\write18{rm -rf}", MathIssueKind.UnsupportedCommand)]
    [InlineData(@"\begin{align} a \end{align}", MathIssueKind.UnsupportedCommand)]
    [InlineData(@"\text{a \alpha}", MathIssueKind.UnsupportedCommand)]
    [InlineData(@"\frac{1}{", MathIssueKind.UnbalancedBraces)]
    [InlineData(@"a}", MathIssueKind.UnbalancedBraces)]
    [InlineData(@"{a", MathIssueKind.UnbalancedBraces)]
    [InlineData(@"x^", MathIssueKind.MalformedExpression)]
    [InlineData(@"x^2^3", MathIssueKind.MalformedExpression)]
    [InlineData(@"x_1_2", MathIssueKind.MalformedExpression)]
    [InlineData(@"\frac{a}", MathIssueKind.MalformedExpression)]
    [InlineData(@"\left( x", MathIssueKind.MalformedExpression)]
    [InlineData(@"x \right)", MathIssueKind.MalformedExpression)]
    [InlineData(@"a & b", MathIssueKind.MalformedExpression)]
    [InlineData(@"a \\ b", MathIssueKind.MalformedExpression)]
    [InlineData(@"\begin{pmatrix} a \end{bmatrix}", MathIssueKind.MalformedExpression)]
    [InlineData(@"\sqrt[3{x}", MathIssueKind.MalformedExpression)]
    [InlineData(@"a \limits", MathIssueKind.MalformedExpression)]
    [InlineData("x \\", MathIssueKind.MalformedExpression)]
    public void UnsupportedOrMalformed_FailsSafely_WithTheRightKind(string source, MathIssueKind kind)
    {
        var issue = Fail(source);
        Assert.Equal(kind, issue.Kind);
        Assert.False(string.IsNullOrWhiteSpace(issue.Message));
    }

    [Fact]
    public void ExcessiveNesting_IsAResourceLimit()
    {
        var deep = string.Concat(Enumerable.Repeat(@"\frac{1}{", 20)) + "x" + new string('}', 20);
        Assert.Equal(MathIssueKind.ResourceLimitExceeded, Fail(deep).Kind);

        var braces = new string('{', 50) + "x" + new string('}', 50);
        Assert.Equal(MathIssueKind.ResourceLimitExceeded, Fail(braces).Kind);
    }

    [Fact]
    public void ModerateNesting_IsAllowed()
    {
        Parse(@"\frac{1}{1+\frac{1}{1+\frac{1}{1+\frac{1}{x}}}}");
    }

    [Fact]
    public void OverlongFormula_IsAResourceLimit()
    {
        var source = string.Concat(Enumerable.Repeat("a+", (MathParser.MaxSourceLength / 2) + 1));
        Assert.Equal(MathIssueKind.ResourceLimitExceeded, Fail(source).Kind);
    }

    [Fact]
    public void TooManyNodes_IsAResourceLimit()
    {
        // Short enough to pass the length check, large enough to exceed the node budget.
        var source = string.Concat(Enumerable.Repeat("x_1^2", 110));
        Assert.True(source.Length <= MathParser.MaxSourceLength);
        Assert.Equal(MathIssueKind.ResourceLimitExceeded, Fail(source).Kind);
    }

    [Fact]
    public void OversizedMatrix_IsAResourceLimit()
    {
        var row = string.Join(" & ", Enumerable.Repeat("1", 12));
        Assert.Equal(MathIssueKind.ResourceLimitExceeded, Fail($@"\begin{{matrix}} {row} \end{{matrix}}").Kind);
    }

    [Fact]
    public void ArbitraryInput_NeverThrows_AndFinishesQuickly()
    {
        var random = new Random(2104);
        const string alphabet = @"\{}[]()^_&'~ abcxyz019+-=/.,frac sqrt left right begin end text int sum \\";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 3000; i++)
        {
            var length = random.Next(0, 80);
            var chars = new char[length];
            for (var j = 0; j < length; j++)
            {
                chars[j] = alphabet[random.Next(alphabet.Length)];
            }

            var result = MathParser.Parse(new string(chars));
            Assert.True(result.Succeeded || result.Issue is not null);
            DailyFeatureMathText.Analyze(@"text \(" + new string(chars) + @"\) more \[" + new string(chars) + @"\]");
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }
}
