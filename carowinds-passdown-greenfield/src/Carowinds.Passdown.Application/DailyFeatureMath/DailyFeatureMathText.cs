namespace Carowinds.Passdown.Application.DailyFeatureMath;

public enum MathSegmentKind
{
    /// <summary>Ordinary prose, printed exactly as typed.</summary>
    Text,

    /// <summary>A <c>\( … \)</c> formula inside the sentence.</summary>
    InlineMath,

    /// <summary>A <c>\[ … \]</c> formula set apart on its own line.</summary>
    DisplayMath,
}

/// <summary>
/// One piece of a Daily Feature. <see cref="Start"/>/<see cref="Length"/> cover the source including
/// delimiters; <see cref="Content"/> is the formula between them (math only). A math segment whose
/// <see cref="Parse"/> failed is printed as its literal <see cref="Source"/>.
/// </summary>
public sealed record MathTextSegment(MathSegmentKind Kind, int Start, int Length, string Source, string Content, MathParseResult? Parse)
{
    public bool IsMath => Kind != MathSegmentKind.Text;

    public bool IsRenderableMath => IsMath && Parse is { Succeeded: true };
}

/// <summary>The interpretation of one Daily Feature text — computed on demand, never stored.</summary>
public sealed record DailyFeatureMathAnalysis(
    string Text,
    IReadOnlyList<MathTextSegment> Segments,
    bool ContainsMathDelimiters,
    IReadOnlyList<MathIssue> Issues)
{
    /// <summary>True when at least one region parsed and will be drawn as a formula.</summary>
    public bool HasRenderableMath => Segments.Any(segment => segment.IsRenderableMath);
}

/// <summary>
/// Phase 21D: finds the explicitly delimited math in a Daily Feature. Math is recognized only between
/// <c>\(</c> … <c>\)</c> (inline) and <c>\[</c> … <c>\]</c> (display); everything else — dollar
/// signs, lone backslashes, underscores, carets, Unicode symbols — is ordinary text and stays exactly
/// as typed. A doubled backslash (<c>\\(</c>) never opens math. An opener without its matching closer
/// is ordinary text (with an <see cref="MathIssueKind.UnclosedDelimiter"/> issue for the preview).
/// The stored Daily Feature is always this raw source; nothing here is persisted or synced.
/// </summary>
public static class DailyFeatureMathText
{
    /// <summary>Longest Daily Feature that is examined for math at all; longer text is treated as plain text.</summary>
    public const int MaxAnalyzedLength = 8000;

    /// <summary>Most formulas formatted in one Daily Feature; later regions are shown as typed.</summary>
    public const int MaxMathRegions = 40;

    /// <summary>Cheap check: could <paramref name="text"/> contain a math opener? (No parsing.)</summary>
    public static bool MayContainMath(string? text) =>
        !string.IsNullOrEmpty(text) && (text.Contains("\\(", StringComparison.Ordinal) || text.Contains("\\[", StringComparison.Ordinal));

    public static DailyFeatureMathAnalysis Analyze(string? text)
    {
        text ??= string.Empty;
        var issues = new List<MathIssue>();

        if (!MayContainMath(text))
        {
            return Plain(text, containsDelimiters: false, issues);
        }

        if (text.Length > MaxAnalyzedLength)
        {
            issues.Add(new MathIssue(MathIssueKind.ResourceLimitExceeded, "This Daily Feature is too long to format its math; it’s shown as typed."));
            return Plain(text, containsDelimiters: true, issues);
        }

        var segments = new List<MathTextSegment>();
        var textStart = 0;
        var regions = 0;
        var index = 0;

        while (index < text.Length - 1)
        {
            if (text[index] != '\\')
            {
                index++;
                continue;
            }

            var next = text[index + 1];
            if (next == '\\')
            {
                index += 2; // an escaped backslash: "\\(" is literal text, never an opener
                continue;
            }

            if (next is not ('(' or '['))
            {
                index++;
                continue;
            }

            var display = next == '[';
            var contentStart = index + 2;
            var closer = FindCloser(text, contentStart, display);
            if (closer < 0)
            {
                issues.Add(new MathIssue(MathIssueKind.UnclosedDelimiter, display
                    ? "A \\[ has no closing \\]; it’s shown as typed."
                    : "A \\( has no closing \\); it’s shown as typed."));
                index += 2;
                continue;
            }

            if (index > textStart)
            {
                segments.Add(TextSegment(text, textStart, index));
            }

            var end = closer + 2;
            var source = text[index..end];
            var content = text[contentStart..closer];
            MathParseResult parse;
            if (++regions > MaxMathRegions)
            {
                parse = new MathParseResult(null, new MathIssue(MathIssueKind.ResourceLimitExceeded, "Too many formulas to format; the rest are shown as typed."));
            }
            else
            {
                parse = MathParser.Parse(content);
            }

            if (parse.Issue is { } issue && !issues.Contains(issue))
            {
                issues.Add(issue);
            }

            segments.Add(new MathTextSegment(display ? MathSegmentKind.DisplayMath : MathSegmentKind.InlineMath, index, end - index, source, content, parse));
            index = end;
            textStart = end;
        }

        if (textStart < text.Length)
        {
            segments.Add(TextSegment(text, textStart, text.Length));
        }

        return new DailyFeatureMathAnalysis(text, segments, ContainsMathDelimiters: true, issues);
    }

    /// <summary>
    /// The text with every delimited math region (delimiters included) replaced by spaces, so a spell
    /// checker sees only the prose — at the same offsets, so its underlines still line up. Unclosed
    /// openers are left as typed (they are prose).
    /// </summary>
    public static string MaskMathForSpellCheck(string? text)
    {
        if (string.IsNullOrEmpty(text) || !MayContainMath(text) || text.Length > MaxAnalyzedLength)
        {
            return text ?? string.Empty;
        }

        var analysis = Analyze(text);
        if (!analysis.Segments.Any(segment => segment.IsMath))
        {
            return text;
        }

        var chars = text.ToCharArray();
        foreach (var segment in analysis.Segments.Where(segment => segment.IsMath))
        {
            for (var i = segment.Start; i < segment.Start + segment.Length; i++)
            {
                // Keep line breaks so line-based layout (and offsets) are untouched.
                if (chars[i] is not ('\n' or '\r'))
                {
                    chars[i] = ' ';
                }
            }
        }

        return new string(chars);
    }

    private static DailyFeatureMathAnalysis Plain(string text, bool containsDelimiters, List<MathIssue> issues) =>
        new(text, text.Length == 0 ? [] : [TextSegment(text, 0, text.Length)], containsDelimiters, issues);

    private static MathTextSegment TextSegment(string text, int start, int end) =>
        new(MathSegmentKind.Text, start, end - start, text[start..end], string.Empty, null);

    /// <summary>Index of the matching "\)" / "\]", or -1. Escaped backslashes are skipped; a different-kind delimiter ends the search.</summary>
    private static int FindCloser(string text, int from, bool display)
    {
        var index = from;
        while (index < text.Length - 1)
        {
            if (text[index] != '\\')
            {
                index++;
                continue;
            }

            var next = text[index + 1];
            switch (next)
            {
                case '\\':
                    index += 2; // "\\" (a matrix row break) is not a delimiter
                    continue;
                case ')' when !display:
                case ']' when display:
                    return index;
                case '(' or '[' or ')' or ']':
                    return -1; // a nested or mismatched delimiter: the region is unclosed
                default:
                    index++;
                    continue;
            }
        }

        return -1;
    }
}
