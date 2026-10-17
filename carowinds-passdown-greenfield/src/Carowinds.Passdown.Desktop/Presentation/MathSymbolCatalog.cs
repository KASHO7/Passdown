namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>One insertable symbol: the Unicode text and an accessible name.</summary>
public sealed record MathSymbolEntry(string Symbol, string Name);

/// <summary>A small, named group of symbols in the picker.</summary>
public sealed record MathSymbolCategory(string Name, IReadOnlyList<MathSymbolEntry> Symbols);

/// <summary>
/// An equation template: plain editable source text. After insertion the editor selects
/// <see cref="SelectLength"/> characters starting <see cref="SelectStart"/> characters into the inserted
/// text (the first placeholder), so typing replaces it.
/// </summary>
public sealed record MathTemplate(string Label, string Text, int SelectStart, int SelectLength);

/// <summary>
/// Phase 21D: the Daily Feature math picker's contents — a deliberately short, categorized set of
/// common symbols (not a Unicode browser) plus a handful of LaTeX-style templates. Symbols insert as
/// plain Unicode text and work without any delimiters; templates insert <c>\( … \)</c> source that the
/// preview and PDFs format.
/// </summary>
public static class MathSymbolCatalog
{
    public static IReadOnlyList<MathSymbolCategory> Categories { get; } =
    [
        new("Calculus",
        [
            new("∫", "Integral"), new("∬", "Double integral"), new("∮", "Contour integral"), new("∂", "Partial"),
            new("∇", "Nabla"), new("∞", "Infinity"), new("Δ", "Delta"), new("′", "Prime"),
        ]),
        new("Greek",
        [
            new("α", "Alpha"), new("β", "Beta"), new("γ", "Gamma"), new("δ", "Delta (lowercase)"), new("ε", "Epsilon"),
            new("θ", "Theta"), new("λ", "Lambda"), new("μ", "Mu"), new("π", "Pi"), new("σ", "Sigma (lowercase)"),
            new("φ", "Phi"), new("ω", "Omega (lowercase)"), new("Σ", "Sigma"), new("Π", "Pi (capital)"), new("Ω", "Omega"),
        ]),
        new("Operators",
        [
            new("±", "Plus or minus"), new("×", "Times"), new("÷", "Divided by"), new("·", "Dot"), new("−", "Minus"),
            new("≈", "Approximately equal"), new("≠", "Not equal"), new("≤", "Less than or equal"), new("≥", "Greater than or equal"),
            new("≡", "Identical to"), new("∝", "Proportional to"), new("∑", "Summation"), new("∏", "Product"), new("√", "Square root"),
        ]),
        new("Sets and logic",
        [
            new("∈", "Element of"), new("∉", "Not an element of"), new("⊂", "Subset"), new("⊆", "Subset or equal"),
            new("∪", "Union"), new("∩", "Intersection"), new("∅", "Empty set"), new("∀", "For all"), new("∃", "There exists"),
            new("¬", "Not"), new("⇒", "Implies"), new("⇔", "If and only if"),
        ]),
        new("Other",
        [
            new("°", "Degree"), new("→", "Right arrow"), new("←", "Left arrow"), new("↔", "Left-right arrow"),
            new("∴", "Therefore"), new("∵", "Because"), new("²", "Superscript two"), new("³", "Superscript three"),
            new("ⁿ", "Superscript n"), new("₀", "Subscript zero"), new("₁", "Subscript one"), new("½", "One half"),
            new("⅓", "One third"), new("ℝ", "Real numbers"),
        ]),
    ];

    public static IReadOnlyList<MathTemplate> Templates { get; } =
    [
        Template("Inline math", @"\(⟦x⟧\)"),
        Template("Display equation", @"\[⟦x⟧\]"),
        Template("Fraction", @"\(\frac{⟦a⟧}{b}\)"),
        Template("Derivative", @"\(\frac{d}{dx}⟦f(x)⟧\)"),
        Template("Partial derivative", @"\(\frac{\partial ⟦f⟧}{\partial x}\)"),
        Template("Integral", @"\(\int_a^b ⟦f(x)⟧\,dx\)"),
        Template("Summation", @"\(\sum_{i=1}^{⟦n⟧} i\)"),
        Template("Square root", @"\(\sqrt{⟦x⟧}\)"),
        Template("nth root", @"\(\sqrt[⟦n⟧]{x}\)"),
        Template("Limit", @"\(\lim_{x\to 0}⟦f(x)⟧\)"),
        Template("Power", @"\(x^{⟦2⟧}\)"),
        Template("Subscript", @"\(x_{⟦i⟧}\)"),
    ];

    /// <summary>Builds a template from source whose placeholder is marked ⟦like this⟧ (markers removed; the placeholder becomes the selection).</summary>
    private static MathTemplate Template(string label, string marked)
    {
        var start = marked.IndexOf('⟦', StringComparison.Ordinal);
        var end = marked.IndexOf('⟧', StringComparison.Ordinal);
        var text = marked.Remove(end, 1).Remove(start, 1);
        return new MathTemplate(label, text, start, end - start - 1);
    }
}
