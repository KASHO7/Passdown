namespace Carowinds.Passdown.Application.DailyFeatureMath;

/// <summary>
/// Phase 21D: the closed vocabulary of the supported math subset. Every command the parser accepts is
/// listed here (or handled structurally in <see cref="MathParser"/>); anything else is an
/// "unsupported command" and its region falls back to the literal source. There are no user macros,
/// packages, file/URL commands or anything that could reach outside the formula.
/// </summary>
public static class MathSymbols
{
    /// <summary>Commands that are a single symbol: Unicode text, atom class, glyph style.</summary>
    public static IReadOnlyDictionary<string, (string Text, MathAtomClass Class, MathGlyphStyle Style)> SymbolCommands { get; } = BuildSymbols();

    /// <summary>Large operators: symbol and whether it is an integral (integrals keep their scripts at the side).</summary>
    public static IReadOnlyDictionary<string, (string Symbol, bool IsIntegral)> LargeOperators { get; } = new Dictionary<string, (string, bool)>(StringComparer.Ordinal)
    {
        ["sum"] = ("∑", false),
        ["prod"] = ("∏", false),
        ["coprod"] = ("∐", false),
        ["int"] = ("∫", true),
        ["iint"] = ("∬", true),
        ["iiint"] = ("∭", true),
        ["oint"] = ("∮", true),
        ["bigcup"] = ("⋃", false),
        ["bigcap"] = ("⋂", false),
    };

    /// <summary>Operator names drawn upright; the flag marks names whose scripts stack in display math (\lim, \max…).</summary>
    public static IReadOnlyDictionary<string, bool> FunctionNames { get; } = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["sin"] = false, ["cos"] = false, ["tan"] = false, ["sec"] = false, ["csc"] = false, ["cot"] = false,
        ["arcsin"] = false, ["arccos"] = false, ["arctan"] = false,
        ["sinh"] = false, ["cosh"] = false, ["tanh"] = false,
        ["log"] = false, ["ln"] = false, ["exp"] = false, ["deg"] = false, ["arg"] = false, ["dim"] = false, ["ker"] = false,
        ["lim"] = true, ["max"] = true, ["min"] = true, ["sup"] = true, ["inf"] = true, ["det"] = true, ["gcd"] = true,
    };

    /// <summary>Spacing commands, in em.</summary>
    public static IReadOnlyDictionary<string, double> Spaces { get; } = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        [","] = 3.0 / 18, [":"] = 4.0 / 18, [">"] = 4.0 / 18, [";"] = 5.0 / 18, ["!"] = -3.0 / 18, [" "] = 0.25,
        ["quad"] = 1.0, ["qquad"] = 2.0,
    };

    /// <summary>Accent commands.</summary>
    public static IReadOnlyDictionary<string, MathAccentKind> Accents { get; } = new Dictionary<string, MathAccentKind>(StringComparer.Ordinal)
    {
        ["hat"] = MathAccentKind.Hat,
        ["bar"] = MathAccentKind.Bar,
        ["vec"] = MathAccentKind.Vec,
        ["dot"] = MathAccentKind.Dot,
        ["ddot"] = MathAccentKind.DoubleDot,
        ["tilde"] = MathAccentKind.Tilde,
        ["overline"] = MathAccentKind.Overline,
        ["underline"] = MathAccentKind.Underline,
    };

    /// <summary>Delimiters accepted after \left / \right and in matrix environments ("." = none).</summary>
    public static IReadOnlyDictionary<string, string> DelimiterCommands { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["{"] = "{", ["}"] = "}", ["|"] = "‖", ["lbrace"] = "{", ["rbrace"] = "}",
        ["langle"] = "⟨", ["rangle"] = "⟩", ["lfloor"] = "⌊", ["rfloor"] = "⌋", ["lceil"] = "⌈", ["rceil"] = "⌉",
        ["vert"] = "|", ["Vert"] = "‖", ["lvert"] = "|", ["rvert"] = "|", ["lVert"] = "‖", ["rVert"] = "‖",
    };

    /// <summary>Single characters accepted as delimiters after \left / \right.</summary>
    public static bool IsDelimiterChar(string text) => text is "(" or ")" or "[" or "]" or "|" or "/" or "⟨" or "⟩" or "‖";

    /// <summary>Matrix environments: left and right delimiter.</summary>
    public static IReadOnlyDictionary<string, (string? Left, string? Right)> MatrixEnvironments { get; } = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal)
    {
        ["matrix"] = (null, null),
        ["pmatrix"] = ("(", ")"),
        ["bmatrix"] = ("[", "]"),
        ["vmatrix"] = ("|", "|"),
        ["Bmatrix"] = ("{", "}"),
    };

    /// <summary>Classifies a literal character typed inside math.</summary>
    public static (MathAtomClass Class, MathGlyphStyle Style, string Text) ClassifyChar(string text)
    {
        if (text.Length == 1)
        {
            var c = text[0];
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'))
            {
                return (MathAtomClass.Ord, MathGlyphStyle.Italic, text);
            }

            if (char.IsDigit(c))
            {
                return (MathAtomClass.Ord, MathGlyphStyle.Upright, text);
            }

            switch (c)
            {
                case '+': return (MathAtomClass.Bin, MathGlyphStyle.Upright, "+");
                case '-': return (MathAtomClass.Bin, MathGlyphStyle.Upright, "−");
                case '*': return (MathAtomClass.Bin, MathGlyphStyle.Upright, "∗");
                case '=': return (MathAtomClass.Rel, MathGlyphStyle.Upright, "=");
                case '<': return (MathAtomClass.Rel, MathGlyphStyle.Upright, "<");
                case '>': return (MathAtomClass.Rel, MathGlyphStyle.Upright, ">");
                case ':': return (MathAtomClass.Rel, MathGlyphStyle.Upright, ":");
                case '(': return (MathAtomClass.Open, MathGlyphStyle.Upright, "(");
                case '[': return (MathAtomClass.Open, MathGlyphStyle.Upright, "[");
                case ')': return (MathAtomClass.Close, MathGlyphStyle.Upright, ")");
                case ']': return (MathAtomClass.Close, MathGlyphStyle.Upright, "]");
                case '!': return (MathAtomClass.Close, MathGlyphStyle.Upright, "!");
                case '?': return (MathAtomClass.Close, MathGlyphStyle.Upright, "?");
                case ',': return (MathAtomClass.Punct, MathGlyphStyle.Upright, ",");
                case ';': return (MathAtomClass.Punct, MathGlyphStyle.Upright, ";");
            }

            // Greek letters typed directly: lowercase italic (TeX convention), uppercase upright.
            if (c is >= 'α' and <= 'ω')
            {
                return (MathAtomClass.Ord, MathGlyphStyle.Italic, text);
            }
        }

        return CharClasses.TryGetValue(text, out var known)
            ? (known, MathGlyphStyle.Upright, text)
            : (MathAtomClass.Ord, MathGlyphStyle.Upright, text);
    }

    /// <summary>Atom class of a Unicode symbol typed directly (e.g. "≤", "×"), from the command table.</summary>
    private static IReadOnlyDictionary<string, MathAtomClass> CharClasses { get; } = SymbolCommands.Values
        .GroupBy(entry => entry.Text, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().Class, StringComparer.Ordinal);

    private static Dictionary<string, (string, MathAtomClass, MathGlyphStyle)> BuildSymbols()
    {
        var symbols = new Dictionary<string, (string, MathAtomClass, MathGlyphStyle)>(StringComparer.Ordinal);

        void Add(MathAtomClass cls, MathGlyphStyle style, params (string Name, string Text)[] entries)
        {
            foreach (var (name, text) in entries)
            {
                symbols[name] = (text, cls, style);
            }
        }

        // Greek: lowercase italic, uppercase upright.
        Add(MathAtomClass.Ord, MathGlyphStyle.Italic,
            ("alpha", "α"), ("beta", "β"), ("gamma", "γ"), ("delta", "δ"), ("epsilon", "ϵ"), ("varepsilon", "ε"),
            ("zeta", "ζ"), ("eta", "η"), ("theta", "θ"), ("vartheta", "ϑ"), ("iota", "ι"), ("kappa", "κ"),
            ("lambda", "λ"), ("mu", "μ"), ("nu", "ν"), ("xi", "ξ"), ("pi", "π"), ("varpi", "ϖ"), ("rho", "ρ"),
            ("varrho", "ϱ"), ("sigma", "σ"), ("varsigma", "ς"), ("tau", "τ"), ("upsilon", "υ"), ("phi", "ϕ"),
            ("varphi", "φ"), ("chi", "χ"), ("psi", "ψ"), ("omega", "ω"));
        Add(MathAtomClass.Ord, MathGlyphStyle.Upright,
            ("Gamma", "Γ"), ("Delta", "Δ"), ("Theta", "Θ"), ("Lambda", "Λ"), ("Xi", "Ξ"), ("Pi", "Π"),
            ("Sigma", "Σ"), ("Upsilon", "Υ"), ("Phi", "Φ"), ("Psi", "Ψ"), ("Omega", "Ω"));

        // Ordinary symbols.
        Add(MathAtomClass.Ord, MathGlyphStyle.Upright,
            ("infty", "∞"), ("partial", "∂"), ("nabla", "∇"), ("prime", "′"), ("emptyset", "∅"), ("varnothing", "∅"),
            ("forall", "∀"), ("exists", "∃"), ("nexists", "∄"), ("neg", "¬"), ("lnot", "¬"), ("angle", "∠"),
            ("triangle", "△"), ("hbar", "ℏ"), ("ell", "ℓ"), ("Re", "ℜ"), ("Im", "ℑ"), ("aleph", "ℵ"),
            ("degree", "°"), ("circ", "∘"), ("ldots", "…"), ("dots", "…"), ("cdots", "⋯"), ("vdots", "⋮"),
            ("ddots", "⋱"), ("therefore", "∴"), ("because", "∵"), ("top", "⊤"), ("bot", "⊥"),
            ("{", "{"), ("}", "}"), ("%", "%"), ("$", "$"), ("&", "&"), ("#", "#"), ("_", "_"),
            ("|", "‖"), ("vert", "|"), ("Vert", "‖"));
        symbols["circ"] = ("∘", MathAtomClass.Bin, MathGlyphStyle.Upright);
        symbols["{"] = ("{", MathAtomClass.Open, MathGlyphStyle.Upright);
        symbols["}"] = ("}", MathAtomClass.Close, MathGlyphStyle.Upright);
        symbols["lbrace"] = ("{", MathAtomClass.Open, MathGlyphStyle.Upright);
        symbols["rbrace"] = ("}", MathAtomClass.Close, MathGlyphStyle.Upright);
        symbols["langle"] = ("⟨", MathAtomClass.Open, MathGlyphStyle.Upright);
        symbols["rangle"] = ("⟩", MathAtomClass.Close, MathGlyphStyle.Upright);
        symbols["lfloor"] = ("⌊", MathAtomClass.Open, MathGlyphStyle.Upright);
        symbols["rfloor"] = ("⌋", MathAtomClass.Close, MathGlyphStyle.Upright);
        symbols["lceil"] = ("⌈", MathAtomClass.Open, MathGlyphStyle.Upright);
        symbols["rceil"] = ("⌉", MathAtomClass.Close, MathGlyphStyle.Upright);

        // Binary operators.
        Add(MathAtomClass.Bin, MathGlyphStyle.Upright,
            ("pm", "±"), ("mp", "∓"), ("times", "×"), ("div", "÷"), ("cdot", "⋅"), ("ast", "∗"), ("star", "⋆"),
            ("bullet", "∙"), ("cup", "∪"), ("cap", "∩"), ("setminus", "∖"), ("land", "∧"), ("wedge", "∧"),
            ("lor", "∨"), ("vee", "∨"), ("oplus", "⊕"), ("otimes", "⊗"), ("odot", "⊙"));

        // Relations and arrows.
        Add(MathAtomClass.Rel, MathGlyphStyle.Upright,
            ("le", "≤"), ("leq", "≤"), ("ge", "≥"), ("geq", "≥"), ("ne", "≠"), ("neq", "≠"), ("approx", "≈"),
            ("equiv", "≡"), ("sim", "∼"), ("simeq", "≃"), ("cong", "≅"), ("propto", "∝"), ("ll", "≪"), ("gg", "≫"),
            ("in", "∈"), ("notin", "∉"), ("ni", "∋"), ("subset", "⊂"), ("subseteq", "⊆"), ("supset", "⊃"),
            ("supseteq", "⊇"), ("perp", "⊥"), ("parallel", "∥"), ("mid", "∣"), ("to", "→"), ("rightarrow", "→"),
            ("leftarrow", "←"), ("gets", "←"), ("leftrightarrow", "↔"), ("Rightarrow", "⇒"), ("Leftarrow", "⇐"),
            ("Leftrightarrow", "⇔"), ("implies", "⟹"), ("impliedby", "⟸"), ("iff", "⟺"), ("mapsto", "↦"),
            ("longrightarrow", "⟶"), ("longleftarrow", "⟵"), ("uparrow", "↑"), ("downarrow", "↓"),
            ("doteq", "≐"), ("models", "⊨"), ("vdash", "⊢"));

        return symbols;
    }
}
