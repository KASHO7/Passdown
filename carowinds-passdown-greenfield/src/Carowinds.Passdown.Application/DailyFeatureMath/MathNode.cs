namespace Carowinds.Passdown.Application.DailyFeatureMath;

/// <summary>
/// Phase 21D: TeX's atom classes, which decide the spacing between neighbouring pieces of a formula
/// (a relation such as "=" gets thick space, a binary operator such as "+" medium space, and so on).
/// </summary>
public enum MathAtomClass
{
    Ord,
    Op,
    Bin,
    Rel,
    Open,
    Close,
    Punct,
    Inner,
}

/// <summary>How a symbol's letters are drawn: math italic for variables, upright for digits, names and operators.</summary>
public enum MathGlyphStyle
{
    Italic,
    Upright,
    Bold,
    DoubleStruck,
}

/// <summary>Accent kinds supported over (or under) a sub-formula.</summary>
public enum MathAccentKind
{
    Hat,
    Bar,
    Vec,
    Dot,
    DoubleDot,
    Tilde,
    Overline,
    Underline,
}

/// <summary>
/// Phase 21D: the parsed form of one math region of a Daily Feature — the single interpretation
/// shared by the Associate preview and every PDF template (no second parser anywhere). Plain data,
/// produced only by <see cref="MathParser"/> from the controlled subset it accepts.
/// </summary>
public abstract record MathNode;

/// <summary>A horizontal sequence of atoms.</summary>
public sealed record MathRow(IReadOnlyList<MathNode> Items) : MathNode
{
    public static MathRow Empty { get; } = new([]);
}

/// <summary>One symbol: a letter, digit, operator, relation, delimiter or punctuation mark (one Unicode scalar, or a short run for digits).</summary>
public sealed record MathSymbol(string Text, MathAtomClass Class, MathGlyphStyle Style) : MathNode;

/// <summary>A large operator: Σ, Π, ∫ and friends. <paramref name="Limits"/> null = the operator's default placement.</summary>
public sealed record MathLargeOperator(string Symbol, bool IsIntegral, bool? Limits) : MathNode;

/// <summary>An upright operator name such as "sin" or "lim". Limit-style names stack their scripts in display math.</summary>
public sealed record MathFunctionName(string Name, bool TakesLimits, bool? Limits) : MathNode;

/// <summary>A base with an optional subscript and/or superscript.</summary>
public sealed record MathScripts(MathNode Base, MathNode? Subscript, MathNode? Superscript) : MathNode;

/// <summary>A fraction (or, without a rule, a binomial's stack). <paramref name="ForceDisplay"/>: \dfrac (true) / \tfrac (false) / \frac (null).</summary>
public sealed record MathFraction(MathNode Numerator, MathNode Denominator, bool HasRule, bool? ForceDisplay) : MathNode;

/// <summary>A square root or, with <paramref name="Index"/>, an nth root.</summary>
public sealed record MathRadical(MathNode Body, MathNode? Index) : MathNode;

/// <summary>
/// A body between delimiters that grow with it (\left( … \right), binomials, matrices). A null
/// delimiter is the invisible "\left." / "\right.".
/// </summary>
public sealed record MathDelimited(string? Left, MathNode Body, string? Right) : MathNode;

/// <summary>Explicit horizontal space in em (negative = \!).</summary>
public sealed record MathSpace(double Em) : MathNode;

/// <summary>Upright running text inside math (\text{…}).</summary>
public sealed record MathText(string Text) : MathNode;

/// <summary>An accent over (or a rule under) a sub-formula.</summary>
public sealed record MathAccent(MathNode Body, MathAccentKind Kind) : MathNode;

/// <summary>A small grid of cells (matrix environments), optionally between growing delimiters.</summary>
public sealed record MathMatrix(IReadOnlyList<IReadOnlyList<MathNode>> Rows, string? Left, string? Right) : MathNode;
