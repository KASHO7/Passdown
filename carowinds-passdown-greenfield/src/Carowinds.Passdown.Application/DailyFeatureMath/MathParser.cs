using System.Text;

namespace Carowinds.Passdown.Application.DailyFeatureMath;

/// <summary>Why a math region could not be formatted. Never carries the user's formula (safe to log).</summary>
public enum MathIssueKind
{
    UnclosedDelimiter,
    UnsupportedCommand,
    UnbalancedBraces,
    MalformedExpression,
    ResourceLimitExceeded,
    RendererUnavailable,
}

/// <summary>
/// A formatting problem for one region. <see cref="Message"/> is a short, user-facing explanation for
/// the Associate preview (it may name the unsupported command the user typed, which is shown only
/// on their own screen); diagnostics log <see cref="Kind"/> only.
/// </summary>
public sealed record MathIssue(MathIssueKind Kind, string Message);

/// <summary>The outcome of parsing one region: a tree, or the issue that made it fall back to literal source.</summary>
public sealed record MathParseResult(MathNode? Root, MathIssue? Issue)
{
    public bool Succeeded => Root is not null;
}

/// <summary>
/// Phase 21D: parser for the intentional LaTeX-style subset documented in
/// <c>docs/phase21d-daily-feature-math.md</c>. Input is untrusted text: the parser only builds a
/// <see cref="MathNode"/> tree from the closed vocabulary in <see cref="MathSymbols"/> — no macros, no
/// packages, no file/URL/shell access — and gives up (returning a <see cref="MathIssue"/>, never
/// throwing) on anything unknown, malformed, too long, too deep or too large. A failed region is shown
/// as its literal source by every renderer; it is never "repaired" into a different formula.
/// </summary>
public static class MathParser
{
    /// <summary>Longest formula (between its delimiters) that is parsed at all.</summary>
    public const int MaxSourceLength = 600;

    /// <summary>Deepest group/fraction/script nesting accepted.</summary>
    public const int MaxDepth = 12;

    /// <summary>Most nodes one formula may produce.</summary>
    public const int MaxNodes = 400;

    public const int MaxMatrixRows = 10;
    public const int MaxMatrixColumns = 10;

    public static MathParseResult Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Length > MaxSourceLength)
        {
            return new(null, new MathIssue(MathIssueKind.ResourceLimitExceeded, "This formula is too long to format."));
        }

        try
        {
            var parser = new Parser(source);
            var row = parser.ParseTopLevel();
            return new(row, null);
        }
        catch (MathSyntaxException ex)
        {
            return new(null, ex.Issue);
        }
    }

    private sealed class MathSyntaxException(MathIssue issue) : Exception(issue.Message)
    {
        public MathIssue Issue { get; } = issue;
    }

    private enum TokenKind
    {
        End,
        Command,
        Char,
        BeginGroup,
        EndGroup,
        Superscript,
        Subscript,
        Align,
        Prime,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Start, int End);

    private enum Stop
    {
        End,
        Group,
        Bracket,
        Right,
        Cell,
    }

    private sealed class Parser(string source)
    {
        private readonly string _s = source;
        private int _pos;
        private int _depth;
        private int _nodes;

        public MathRow ParseTopLevel()
        {
            var row = ParseRow(Stop.End);
            return row;
        }

        private static MathSyntaxException Error(MathIssueKind kind, string message) => new(new MathIssue(kind, message));

        private T Count<T>(T node)
            where T : MathNode
        {
            if (++_nodes > MaxNodes)
            {
                throw Error(MathIssueKind.ResourceLimitExceeded, "This formula is too large to format.");
            }

            return node;
        }

        private void SkipWhitespace()
        {
            while (_pos < _s.Length && (char.IsWhiteSpace(_s[_pos]) || char.IsControl(_s[_pos])))
            {
                _pos++;
            }
        }

        private Token Peek()
        {
            SkipWhitespace();
            if (_pos >= _s.Length)
            {
                return new Token(TokenKind.End, string.Empty, _pos, _pos);
            }

            var c = _s[_pos];
            switch (c)
            {
                case '{': return new Token(TokenKind.BeginGroup, "{", _pos, _pos + 1);
                case '}': return new Token(TokenKind.EndGroup, "}", _pos, _pos + 1);
                case '^': return new Token(TokenKind.Superscript, "^", _pos, _pos + 1);
                case '_': return new Token(TokenKind.Subscript, "_", _pos, _pos + 1);
                case '&': return new Token(TokenKind.Align, "&", _pos, _pos + 1);
                case '\'': return new Token(TokenKind.Prime, "'", _pos, _pos + 1);
                case '~': return new Token(TokenKind.Command, " ", _pos, _pos + 1);
                case '\\':
                    if (_pos + 1 >= _s.Length)
                    {
                        throw Error(MathIssueKind.MalformedExpression, "A backslash at the end of the formula is incomplete.");
                    }

                    if (char.IsAsciiLetter(_s[_pos + 1]))
                    {
                        var end = _pos + 1;
                        while (end < _s.Length && char.IsAsciiLetter(_s[end]))
                        {
                            end++;
                        }

                        return new Token(TokenKind.Command, _s[(_pos + 1)..end], _pos, end);
                    }

                    var length = char.IsHighSurrogate(_s[_pos + 1]) && _pos + 2 < _s.Length ? 2 : 1;
                    return new Token(TokenKind.Command, _s.Substring(_pos + 1, length), _pos, _pos + 1 + length);
                default:
                    var charLength = char.IsHighSurrogate(c) && _pos + 1 < _s.Length && char.IsLowSurrogate(_s[_pos + 1]) ? 2 : 1;
                    return new Token(TokenKind.Char, _s.Substring(_pos, charLength), _pos, _pos + charLength);
            }
        }

        private Token Next()
        {
            var token = Peek();
            _pos = token.End;
            return token;
        }

        private MathRow ParseRow(Stop stop)
        {
            if (++_depth > MaxDepth)
            {
                throw Error(MathIssueKind.ResourceLimitExceeded, "This formula is nested too deeply to format.");
            }

            var items = new List<MathNode>();
            while (true)
            {
                var token = Peek();
                switch (token.Kind)
                {
                    case TokenKind.End:
                        if (stop != Stop.End)
                        {
                            throw stop == Stop.Group
                                ? Error(MathIssueKind.UnbalancedBraces, "A “{” is missing its closing “}”.")
                                : Error(MathIssueKind.MalformedExpression, stop switch
                                {
                                    Stop.Right => "A \\left is missing its \\right.",
                                    Stop.Bracket => "A root index “[” is missing its “]”.",
                                    _ => "A matrix is missing its \\end.",
                                });
                        }

                        _depth--;
                        return Count(new MathRow(items));

                    case TokenKind.EndGroup:
                        if (stop != Stop.Group)
                        {
                            throw Error(MathIssueKind.UnbalancedBraces, "There is a “}” without a matching “{”.");
                        }

                        _depth--;
                        return Count(new MathRow(items));

                    case TokenKind.Align:
                        if (stop != Stop.Cell)
                        {
                            throw Error(MathIssueKind.MalformedExpression, "“&” is only used inside a matrix.");
                        }

                        _depth--;
                        return Count(new MathRow(items));

                    case TokenKind.Char when token.Text == "]" && stop == Stop.Bracket:
                        _depth--;
                        return Count(new MathRow(items));

                    case TokenKind.Command when token.Text == "right":
                        if (stop != Stop.Right)
                        {
                            throw Error(MathIssueKind.MalformedExpression, "A \\right has no matching \\left.");
                        }

                        _depth--;
                        return Count(new MathRow(items));

                    case TokenKind.Command when token.Text is "\\" or "end" or "cr":
                        if (stop != Stop.Cell)
                        {
                            throw Error(MathIssueKind.MalformedExpression, token.Text == "end"
                                ? "An \\end has no matching \\begin."
                                : "Line breaks (\\\\) are only supported inside a matrix.");
                        }

                        _depth--;
                        return Count(new MathRow(items));

                    default:
                        items.Add(ParseAtom());
                        break;
                }
            }
        }

        private MathNode ParseAtom()
        {
            var token = Peek();
            MathNode? nucleus = token.Kind is TokenKind.Superscript or TokenKind.Subscript or TokenKind.Prime
                ? Count(MathRow.Empty)
                : ParseBase();

            MathNode? sub = null;
            MathNode? sup = null;
            var primes = 0;

            while (true)
            {
                token = Peek();
                if (token.Kind == TokenKind.Superscript)
                {
                    Next();
                    if (sup is not null)
                    {
                        throw Error(MathIssueKind.MalformedExpression, "A symbol has two superscripts; group them with { }.");
                    }

                    sup = ParseArgument("^");
                }
                else if (token.Kind == TokenKind.Subscript)
                {
                    Next();
                    if (sub is not null)
                    {
                        throw Error(MathIssueKind.MalformedExpression, "A symbol has two subscripts; group them with { }.");
                    }

                    sub = ParseArgument("_");
                }
                else if (token.Kind == TokenKind.Prime)
                {
                    if (sup is not null)
                    {
                        throw Error(MathIssueKind.MalformedExpression, "A prime (′) must come before the superscript.");
                    }

                    Next();
                    primes++;
                }
                else if (token.Kind == TokenKind.Command && token.Text is "limits" or "nolimits")
                {
                    Next();
                    var limits = token.Text == "limits";
                    nucleus = nucleus switch
                    {
                        MathLargeOperator op => op with { Limits = limits },
                        MathFunctionName fn => fn with { Limits = limits },
                        _ => throw Error(MathIssueKind.MalformedExpression, "\\limits must follow an operator such as \\sum."),
                    };
                }
                else
                {
                    break;
                }
            }

            if (primes > 0)
            {
                var primeSymbol = Count(new MathSymbol(new string('′', primes), MathAtomClass.Ord, MathGlyphStyle.Upright));
                sup = sup is null ? primeSymbol : Count(new MathRow([primeSymbol, sup]));
            }

            return sub is null && sup is null ? nucleus : Count(new MathScripts(nucleus, sub, sup));
        }

        /// <summary>A script or command argument: a {group}, a single character, or one command (with its own arguments).</summary>
        private MathNode ParseArgument(string owner)
        {
            var token = Peek();
            switch (token.Kind)
            {
                case TokenKind.BeginGroup:
                case TokenKind.Char:
                case TokenKind.Command when token.Text is not ("right" or "end" or "\\" or "limits" or "nolimits"):
                    return ParseBase();
                default:
                    throw Error(MathIssueKind.MalformedExpression, owner is "^" or "_"
                        ? $"“{owner}” needs something after it."
                        : $"\\{owner} is missing an argument.");
            }
        }

        private MathNode ParseBase()
        {
            var token = Next();
            switch (token.Kind)
            {
                case TokenKind.BeginGroup:
                    var row = ParseRow(Stop.Group);
                    Next(); // the closing brace ParseRow stopped at
                    return row;

                case TokenKind.Char:
                    var (cls, style, text) = MathSymbols.ClassifyChar(token.Text);
                    return Count(new MathSymbol(text, cls, style));

                case TokenKind.Command:
                    return ParseCommand(token.Text);

                default:
                    throw Error(MathIssueKind.MalformedExpression, "The formula is incomplete.");
            }
        }

        private MathNode ParseCommand(string name)
        {
            if (MathSymbols.SymbolCommands.TryGetValue(name, out var symbol))
            {
                return Count(new MathSymbol(symbol.Text, symbol.Class, symbol.Style));
            }

            if (MathSymbols.LargeOperators.TryGetValue(name, out var op))
            {
                return Count(new MathLargeOperator(op.Symbol, op.IsIntegral, null));
            }

            if (MathSymbols.FunctionNames.TryGetValue(name, out var takesLimits))
            {
                return Count(new MathFunctionName(name, takesLimits, null));
            }

            if (MathSymbols.Spaces.TryGetValue(name, out var space))
            {
                return Count(new MathSpace(space));
            }

            if (MathSymbols.Accents.TryGetValue(name, out var accent))
            {
                return Count(new MathAccent(ParseArgument(name), accent));
            }

            switch (name)
            {
                case "frac" or "dfrac" or "tfrac":
                {
                    var numerator = ParseArgument(name);
                    var denominator = ParseArgument(name);
                    bool? forceDisplay = name switch { "dfrac" => true, "tfrac" => false, _ => null };
                    return Count(new MathFraction(numerator, denominator, HasRule: true, forceDisplay));
                }

                case "binom" or "dbinom" or "tbinom":
                {
                    var top = ParseArgument(name);
                    var bottom = ParseArgument(name);
                    bool? forceDisplay = name switch { "dbinom" => true, "tbinom" => false, _ => null };
                    return Count(new MathDelimited("(", Count(new MathFraction(top, bottom, HasRule: false, forceDisplay)), ")"));
                }

                case "sqrt":
                {
                    MathNode? index = null;
                    var next = Peek();
                    if (next.Kind == TokenKind.Char && next.Text == "[")
                    {
                        Next();
                        index = ParseRow(Stop.Bracket);
                        Next(); // "]"
                    }

                    return Count(new MathRadical(ParseArgument(name), index));
                }

                case "left":
                {
                    var left = ParseDelimiter("left");
                    var body = ParseRow(Stop.Right);
                    Next(); // \right
                    var right = ParseDelimiter("right");
                    return Count(new MathDelimited(left, body, right));
                }

                case "text" or "textrm" or "mbox" or "textit" or "textbf":
                    return Count(new MathText(ReadRawGroup(name)));

                case "operatorname":
                {
                    var operatorName = ReadRawGroup(name);
                    if (operatorName.Length == 0 || !operatorName.All(char.IsAsciiLetter))
                    {
                        throw Error(MathIssueKind.MalformedExpression, "\\operatorname needs a name made of letters.");
                    }

                    return Count(new MathFunctionName(operatorName, TakesLimits: false, null));
                }

                case "mathrm" or "mathbf" or "mathbb" or "mathit":
                {
                    var style = name switch
                    {
                        "mathrm" => MathGlyphStyle.Upright,
                        "mathbf" => MathGlyphStyle.Bold,
                        "mathbb" => MathGlyphStyle.DoubleStruck,
                        _ => MathGlyphStyle.Italic,
                    };
                    return Restyle(ParseArgument(name), style);
                }

                case "begin":
                    return ParseEnvironment();

                default:
                    throw Error(MathIssueKind.UnsupportedCommand, $"\\{name} isn’t supported.");
            }
        }

        private static MathNode Restyle(MathNode node, MathGlyphStyle style) => node switch
        {
            MathSymbol { Class: MathAtomClass.Ord } s when s.Text.Length == 1 && (char.IsAsciiLetter(s.Text[0]) || char.IsAsciiDigit(s.Text[0])) => s with { Style = style },
            MathRow row => row with { Items = row.Items.Select(item => Restyle(item, style)).ToList() },
            MathScripts scripts => scripts with { Base = Restyle(scripts.Base, style) },
            _ => node,
        };

        private string? ParseDelimiter(string owner)
        {
            var token = Next();
            if (token.Kind == TokenKind.Char)
            {
                if (token.Text == ".")
                {
                    return null;
                }

                if (MathSymbols.IsDelimiterChar(token.Text))
                {
                    return token.Text;
                }

                if (token.Text is "<" or ">")
                {
                    return token.Text == "<" ? "⟨" : "⟩";
                }
            }
            else if (token.Kind == TokenKind.Command && MathSymbols.DelimiterCommands.TryGetValue(token.Text, out var delimiter))
            {
                return delimiter;
            }

            throw Error(MathIssueKind.MalformedExpression, $"\\{owner} needs a delimiter such as ( or [ or \\{{.");
        }

        /// <summary>The raw characters of a {group} (for \text, \begin{…}): braces must balance; only escaped punctuation is allowed.</summary>
        private string ReadRawGroup(string owner)
        {
            var open = Next();
            if (open.Kind != TokenKind.BeginGroup)
            {
                throw Error(MathIssueKind.MalformedExpression, $"\\{owner} needs its text in {{ }}.");
            }

            var builder = new StringBuilder();
            var depth = 1;
            while (_pos < _s.Length)
            {
                var c = _s[_pos];
                if (c == '\\' && _pos + 1 < _s.Length)
                {
                    var escaped = _s[_pos + 1];
                    if (escaped is '{' or '}' or '$' or '%' or '&' or '_' or '#' or ' ')
                    {
                        builder.Append(escaped);
                        _pos += 2;
                        continue;
                    }

                    throw Error(MathIssueKind.UnsupportedCommand, "Commands aren’t supported inside \\text{…}.");
                }

                _pos++;
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}' && --depth == 0)
                {
                    return builder.ToString();
                }

                builder.Append(char.IsControl(c) ? ' ' : c);
            }

            throw Error(MathIssueKind.UnbalancedBraces, "A “{” is missing its closing “}”.");
        }

        private MathNode ParseEnvironment()
        {
            var environment = ReadRawGroup("begin").Trim();
            if (!MathSymbols.MatrixEnvironments.TryGetValue(environment, out var delimiters))
            {
                throw Error(MathIssueKind.UnsupportedCommand, $"The “{(environment.Length > 20 ? environment[..20] : environment)}” environment isn’t supported.");
            }

            if (++_depth > MaxDepth)
            {
                throw Error(MathIssueKind.ResourceLimitExceeded, "This formula is nested too deeply to format.");
            }

            var rows = new List<IReadOnlyList<MathNode>>();
            var cells = new List<MathNode>();
            while (true)
            {
                cells.Add(ParseRow(Stop.Cell));
                if (cells.Count > MaxMatrixColumns)
                {
                    throw Error(MathIssueKind.ResourceLimitExceeded, "This matrix has too many columns to format.");
                }

                var token = Next();
                if (token.Kind == TokenKind.Align)
                {
                    continue;
                }

                if (token.Kind == TokenKind.Command && token.Text is "\\" or "cr")
                {
                    rows.Add(cells);
                    cells = [];
                    if (rows.Count > MaxMatrixRows)
                    {
                        throw Error(MathIssueKind.ResourceLimitExceeded, "This matrix has too many rows to format.");
                    }

                    continue;
                }

                // \end{environment}
                var closing = ReadRawGroup("end").Trim();
                if (closing != environment)
                {
                    throw Error(MathIssueKind.MalformedExpression, "A matrix’s \\begin and \\end don’t match.");
                }

                // A trailing "\\" before \end leaves one empty cell: not a real row.
                if (!(cells.Count == 1 && cells[0] is MathRow { Items.Count: 0 } && rows.Count > 0))
                {
                    rows.Add(cells);
                }

                break;
            }

            _depth--;
            if (rows.Count > MaxMatrixRows)
            {
                throw Error(MathIssueKind.ResourceLimitExceeded, "This matrix has too many rows to format.");
            }

            return Count(new MathMatrix(rows, delimiters.Left, delimiters.Right));
        }
    }
}
