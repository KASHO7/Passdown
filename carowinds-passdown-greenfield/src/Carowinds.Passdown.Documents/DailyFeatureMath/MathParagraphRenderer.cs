using System.Globalization;
using System.Text;
using Carowinds.Passdown.Application.DailyFeatureMath;

namespace Carowinds.Passdown.Documents.DailyFeatureMath;

/// <summary>Typography of the paragraph the math sits in (one per PDF template, plus the Associate preview).</summary>
/// <param name="FontSize">Prose size in points (or device-independent pixels for the preview).</param>
/// <param name="LineHeight">Baseline-to-baseline distance as a multiple of <paramref name="FontSize"/> for ordinary lines.</param>
/// <param name="ItalicProse">Whether the prose is set in Lato Italic (all three templates print the Daily Feature in italic).</param>
public sealed record MathParagraphStyle(double FontSize, double LineHeight, bool ItalicProse);

/// <summary>A stroked polyline in line coordinates.</summary>
public sealed record MathStroke(IReadOnlyList<(double X, double Y)> Points, double Thickness);

/// <summary>
/// One laid-out line, in its own coordinates: (0, 0) is the line's top-left, y grows downward.
/// <see cref="Fill"/> holds every glyph outline and rule as one non-zero-winding path; strokes are the
/// radical signs. The same data drives the Avalonia preview (geometry) and the PDF (SVG).
/// </summary>
public sealed record MathRenderedLine(double Width, double Height, double Baseline, IReadOnlyList<PathCommand> Fill, IReadOnlyList<MathStroke> Strokes);

/// <summary>A laid-out Daily Feature: lines stacked top to bottom, plus any issues that made regions fall back to their literal source.</summary>
public sealed record MathRenderedParagraph(double Width, IReadOnlyList<MathRenderedLine> Lines, IReadOnlyList<MathIssue> Issues)
{
    public double Height => Lines.Sum(line => line.Height);
}

/// <summary>
/// Phase 21D: lays out a whole Daily Feature that contains math — prose words in Lato, formulas from
/// the shared <see cref="MathParser"/> tree via <see cref="MathLayoutEngine"/> — into wrapped lines of
/// vector outlines. One implementation for the Associate preview and the Classic, Compact and Modern
/// PDFs, so a formula means the same thing everywhere; only size, line height and width differ.
///
/// Rules: words wrap at spaces and line breaks; an inline formula wraps only after a top-level binary
/// operator or relation; a display formula gets its own centered line(s) with balanced space around
/// it; anything still wider than the line is scaled down to fit (never clipped). A region that failed
/// to parse is set as its literal source text. Results are cached (bounded, process-local, never
/// persisted or synced) by text + width + style.
/// </summary>
public sealed class MathParagraphRenderer
{
    /// <summary>Most cached layouts; the oldest is evicted first.</summary>
    public const int CacheCapacity = 64;

    /// <summary>Formulas are set slightly larger than the prose so their x-height matches Lato's.</summary>
    private const double MathSizeRatio = 1.06;

    /// <summary>Tallest formula piece, in multiples of the prose font size (≈ 20 lines of text).</summary>
    private const double MaxFormulaHeightInLines = 24;

    private static readonly Lazy<MathParagraphRenderer?> SharedInstance = new(
        () => MathFontSet.Shared is { } fonts ? new MathParagraphRenderer(fonts) : null,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly MathFontSet _fonts;
    private readonly MathLayoutEngine _engine;
    private readonly Dictionary<(string Text, double Width, MathParagraphStyle Style), LinkedListNode<CacheEntry>> _cache = [];
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly Lock _cacheGate = new();

    private sealed record CacheEntry((string Text, double Width, MathParagraphStyle Style) Key, MathRenderedParagraph Value);

    public MathParagraphRenderer(MathFontSet fonts)
    {
        _fonts = fonts;
        _engine = new MathLayoutEngine(fonts);
    }

    /// <summary>The process-wide renderer, or null when the bundled fonts are unavailable (callers then show plain text).</summary>
    public static MathParagraphRenderer? Shared => SharedInstance.Value;

    /// <summary>Number of cached layouts (test seam).</summary>
    public int CachedCount
    {
        get
        {
            lock (_cacheGate)
            {
                return _cache.Count;
            }
        }
    }

    /// <summary>
    /// Lays out <paramref name="text"/> at <paramref name="width"/>. Returns null only if layout itself
    /// failed unexpectedly (the caller then prints the plain text); a formula problem is not a failure —
    /// that region is shown as typed and reported in <see cref="MathRenderedParagraph.Issues"/>.
    /// </summary>
    public MathRenderedParagraph? Render(string text, double width, MathParagraphStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        width = System.Math.Max(20, System.Math.Floor(width * 10) / 10);
        var key = (text, width, style);

        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Value;
            }
        }

        MathRenderedParagraph result;
        try
        {
            result = Layout(DailyFeatureMathText.Analyze(text), width, style);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MathRenderDiagnostics.Report(MathRenderStatus.RenderFailed);
            return null;
        }

        foreach (var issue in result.Issues)
        {
            MathRenderDiagnostics.Report(issue.Kind switch
            {
                MathIssueKind.UnsupportedCommand => MathRenderStatus.UnsupportedCommand,
                MathIssueKind.ResourceLimitExceeded => MathRenderStatus.ResourceLimitExceeded,
                _ => MathRenderStatus.MalformedExpression,
            });
        }

        lock (_cacheGate)
        {
            if (!_cache.ContainsKey(key))
            {
                var node = _lru.AddFirst(new CacheEntry(key, result));
                _cache[key] = node;
                while (_cache.Count > CacheCapacity && _lru.Last is { } oldest)
                {
                    _lru.RemoveLast();
                    _cache.Remove(oldest.Value.Key);
                }
            }
        }

        return result;
    }

    public void ClearCache()
    {
        lock (_cacheGate)
        {
            _cache.Clear();
            _lru.Clear();
        }
    }

    // ---- Paragraph assembly -----------------------------------------------------------------------

    /// <summary>A unit of the paragraph: a word (or glued word+formula run part), a space, a hard break, or a display block.</summary>
    private sealed record Fragment(MathBox? Box, bool IsSpace, bool CanBreakBefore, bool IsHardBreak = false, IReadOnlyList<MathBox>? Display = null);

    private MathRenderedParagraph Layout(DailyFeatureMathAnalysis analysis, double width, MathParagraphStyle style)
    {
        var fontSize = style.FontSize;
        var mathSize = fontSize * MathSizeRatio;
        var maxPieceHeight = fontSize * MaxFormulaHeightInLines;

        // A formula taller than the cap (a deep stack of fractions, a big matrix) is scaled down so any
        // single line always fits on a fresh page — and the preview never grows without bound.
        MathBox Cap(MathBox piece) => piece.Height + piece.Depth > maxPieceHeight
            ? piece.Scaled(maxPieceHeight / (piece.Height + piece.Depth))
            : piece;
        var proseFont = style.ItalicProse ? _fonts.ProseItalic : _fonts.ProseRegular;
        var fragments = new List<Fragment>();

        foreach (var segment in analysis.Segments)
        {
            if (segment.IsRenderableMath)
            {
                var root = segment.Parse!.Root!;
                if (segment.Kind == MathSegmentKind.DisplayMath)
                {
                    var pieces = _engine.LayoutBreakablePieces(root, MathStyle.Display).Select(piece => Cap(piece.Scaled(mathSize))).ToList();
                    fragments.Add(new Fragment(null, false, true, Display: pieces));
                }
                else
                {
                    var pieces = _engine.LayoutBreakablePieces(root, MathStyle.Text);
                    for (var i = 0; i < pieces.Count; i++)
                    {
                        fragments.Add(new Fragment(Cap(pieces[i].Scaled(mathSize)), false, CanBreakBefore: i > 0));
                    }
                }

                continue;
            }

            // Prose — and any formula that couldn't be formatted, shown exactly as typed.
            AddProse(segment.Source, proseFont, fontSize, fragments);
        }

        var lines = BreakLines(fragments, width, style, proseFont);
        return new MathRenderedParagraph(width, lines, analysis.Issues);
    }

    private void AddProse(string text, TrueTypeFont font, double fontSize, List<Fragment> fragments)
    {
        var builder = new StringBuilder();

        void FlushWord()
        {
            if (builder.Length > 0)
            {
                fragments.Add(new Fragment(ProseRun(builder.ToString(), font, fontSize), false, false));
                builder.Clear();
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                FlushWord();
                fragments.Add(new Fragment(null, false, true, IsHardBreak: true));
                continue;
            }

            if (c is ' ' or '\t')
            {
                FlushWord();
                fragments.Add(new Fragment(ProseRun(" ", font, fontSize), true, true));
                continue;
            }

            builder.Append(c);
        }

        FlushWord();
    }

    /// <summary>A run of prose glyphs (prose font, falling back per character to the math font for symbols Lato lacks).</summary>
    private MathBox ProseRun(string text, TrueTypeFont font, double fontSize)
    {
        var box = new MathBox { Class = MathAtomClass.Ord };
        var x = 0.0;
        foreach (var rune in text.EnumerateRunes())
        {
            var codePoint = char.IsControl((char)System.Math.Min(rune.Value, 0xFFFF)) && rune.Value < 0x10000 ? ' ' : rune.Value;
            TrueTypeFont glyphFont;
            ushort glyph;
            if (font.TryGetGlyph(codePoint, out glyph))
            {
                glyphFont = font;
            }
            else if (_fonts.Math.TryGetGlyph(codePoint, out glyph))
            {
                glyphFont = _fonts.Math;
            }
            else if (codePoint == ' ')
            {
                x += 0.25 * fontSize;
                continue;
            }
            else
            {
                glyphFont = font;
                glyph = 0; // visible .notdef box
            }

            var metrics = glyphFont.GetMetrics(glyph);
            box.Add(new MathGlyphItem(glyphFont, glyph, x, 0, fontSize));
            box.Height = System.Math.Max(box.Height, metrics.YMax * fontSize);
            box.Depth = System.Math.Max(box.Depth, -metrics.YMin * fontSize);
            x += metrics.Advance * fontSize;
        }

        box.Width = x;
        return box;
    }

    // ---- Line breaking ----------------------------------------------------------------------------

    private sealed class LineBuilder
    {
        public List<(MathBox Box, bool IsSpace)> Items { get; } = [];

        public double Width => Items.Sum(item => item.Box.Width);

        public bool HasContent => Items.Any(item => !item.IsSpace);
    }

    private List<MathRenderedLine> BreakLines(List<Fragment> fragments, double width, MathParagraphStyle style, TrueTypeFont proseFont)
    {
        var lines = new List<MathRenderedLine>();
        var current = new LineBuilder();
        var displayGap = style.FontSize * 0.3;

        void EmitCurrent(bool force)
        {
            // Trailing spaces never count toward the line.
            while (current.Items.Count > 0 && current.Items[^1].IsSpace)
            {
                current.Items.RemoveAt(current.Items.Count - 1);
            }

            if (current.Items.Count > 0 || force)
            {
                lines.Add(BuildLine(current.Items.Select(item => item.Box).ToList(), width, style, proseFont, centered: false, extraAbove: 0, extraBelow: 0));
            }

            current = new LineBuilder();
        }

        var previousWasDisplay = false;
        for (var index = 0; index < fragments.Count; index++)
        {
            var fragment = fragments[index];
            if (fragment.IsHardBreak)
            {
                // A line break right after a display formula is already implied by the formula.
                if (previousWasDisplay && !current.HasContent)
                {
                    previousWasDisplay = false;
                    continue;
                }

                EmitCurrent(force: true);
                previousWasDisplay = false;
                continue;
            }

            if (fragment.Display is { } pieces)
            {
                if (current.HasContent)
                {
                    EmitCurrent(force: false);
                }
                else
                {
                    current = new LineBuilder();
                }

                var displayLines = PackPieces(pieces, width);
                for (var i = 0; i < displayLines.Count; i++)
                {
                    var above = i == 0 && lines.Count > 0 ? displayGap : 0;
                    var below = i == displayLines.Count - 1 && index < fragments.Count - 1 ? displayGap : 0;
                    lines.Add(BuildLine(displayLines[i], width, style, proseFont, centered: true, above, below));
                }

                previousWasDisplay = true;
                continue;
            }

            previousWasDisplay = false;
            var box = fragment.Box!;
            if (fragment.IsSpace)
            {
                if (current.Items.Count > 0)
                {
                    current.Items.Add((box, true));
                }

                continue;
            }

            if (current.Width + box.Width <= width || !current.HasContent)
            {
                if (box.Width > width)
                {
                    // A single unit wider than the line: split a word into characters; scale a formula piece.
                    foreach (var part in FitOversized(box, width, fragment))
                    {
                        if (current.HasContent && current.Width + part.Width > width)
                        {
                            EmitCurrent(force: false);
                        }

                        current.Items.Add((part, false));
                    }

                    continue;
                }

                current.Items.Add((box, false));
                continue;
            }

            // Overflow: break before this fragment if allowed, else at the last space in the line.
            if (fragment.CanBreakBefore)
            {
                EmitCurrent(force: false);
                current.Items.Add((box, false));
                continue;
            }

            var lastSpace = current.Items.FindLastIndex(item => item.IsSpace);
            if (lastSpace >= 0)
            {
                var carried = current.Items.Skip(lastSpace + 1).ToList();
                current.Items.RemoveRange(lastSpace, current.Items.Count - lastSpace);
                EmitCurrent(force: false);
                current.Items.AddRange(carried);
                if (current.Width + box.Width > width && current.HasContent)
                {
                    EmitCurrent(force: false);
                }

                current.Items.Add((box, false));
                continue;
            }

            // No opportunity at all (one long glued run): break here anyway rather than overflow.
            EmitCurrent(force: false);
            current.Items.Add((box, false));
        }

        EmitCurrent(force: false);
        return lines;
    }

    private static IEnumerable<MathBox> FitOversized(MathBox box, double width, Fragment fragment)
    {
        var glyphs = box.Items.OfType<MathGlyphItem>().ToList();
        var isProseWord = !fragment.CanBreakBefore && glyphs.Count > 1 && glyphs.Count == box.Items.Count && glyphs.All(g => g.Y == 0);
        if (!isProseWord)
        {
            yield return box.Scaled(width / box.Width);
            yield break;
        }

        // Character-level pieces of a very long word.
        for (var i = 0; i < glyphs.Count; i++)
        {
            var glyph = glyphs[i];
            var next = i + 1 < glyphs.Count ? glyphs[i + 1].X : box.Width;
            var metrics = glyph.Font.GetMetrics(glyph.Glyph);
            var piece = new MathBox
            {
                Class = MathAtomClass.Ord,
                Width = next - glyph.X,
                Height = metrics.YMax * glyph.Size,
                Depth = -metrics.YMin * glyph.Size,
            };
            piece.Add(glyph with { X = 0 });
            yield return piece;
        }
    }

    /// <summary>Packs display-formula pieces into as few lines as fit; a piece wider than the line is scaled to fit.</summary>
    private static List<List<MathBox>> PackPieces(IReadOnlyList<MathBox> pieces, double width)
    {
        var total = pieces.Sum(piece => piece.Width);
        if (total <= width)
        {
            return [pieces.ToList()];
        }

        var lines = new List<List<MathBox>>();
        var current = new List<MathBox>();
        var currentWidth = 0.0;
        foreach (var piece in pieces)
        {
            var fitted = piece.Width > width ? piece.Scaled(width / piece.Width) : piece;
            if (current.Count > 0 && currentWidth + fitted.Width > width)
            {
                lines.Add(current);
                current = [];
                currentWidth = 0;
            }

            current.Add(fitted);
            currentWidth += fitted.Width;
        }

        if (current.Count > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static MathRenderedLine BuildLine(List<MathBox> boxes, double width, MathParagraphStyle style, TrueTypeFont proseFont, bool centered, double extraAbove, double extraBelow)
    {
        var fontSize = style.FontSize;
        var pitch = fontSize * style.LineHeight;
        var natural = (proseFont.Ascender + proseFont.Descender) * fontSize;
        var leading = System.Math.Max(0, pitch - natural);
        var proseAscent = (proseFont.Ascender * fontSize) + (leading / 2);
        var proseDescent = (proseFont.Descender * fontSize) + (leading / 2);
        var clearance = fontSize * 0.1;

        var ascent = proseAscent;
        var descent = proseDescent;
        foreach (var box in boxes)
        {
            ascent = System.Math.Max(ascent, box.Height + clearance);
            descent = System.Math.Max(descent, box.Depth + clearance);
        }

        ascent += extraAbove;
        descent += extraBelow;
        var lineWidth = boxes.Sum(box => box.Width);
        var x = centered ? System.Math.Max(0, (width - lineWidth) / 2) : 0;

        var line = new MathBox();
        foreach (var box in boxes)
        {
            line.Place(box, x, ascent);
            x += box.Width;
        }

        var fill = new List<PathCommand>();
        var strokes = new List<MathStroke>();
        foreach (var item in line.Items)
        {
            switch (item)
            {
                case MathGlyphItem glyph:
                    AppendGlyph(glyph, fill);
                    break;
                case MathRectItem rect:
                    // Same winding as TrueType outer contours once y is flipped, so overlaps union under non-zero fill.
                    fill.Add(new PathCommand(PathVerb.MoveTo, rect.X, rect.Y));
                    fill.Add(new PathCommand(PathVerb.LineTo, rect.X, rect.Y + rect.Height));
                    fill.Add(new PathCommand(PathVerb.LineTo, rect.X + rect.Width, rect.Y + rect.Height));
                    fill.Add(new PathCommand(PathVerb.LineTo, rect.X + rect.Width, rect.Y));
                    fill.Add(new PathCommand(PathVerb.Close, 0, 0));
                    break;
                case MathStrokeItem stroke:
                    strokes.Add(new MathStroke(stroke.Points, stroke.Thickness));
                    break;
            }
        }

        return new MathRenderedLine(width, ascent + descent, ascent, fill, strokes);
    }

    private static void AppendGlyph(MathGlyphItem glyph, List<PathCommand> fill)
    {
        var sx = glyph.Size * glyph.ScaleX;
        var sy = glyph.Size * glyph.ScaleY;
        foreach (var c in glyph.Font.GetOutline(glyph.Glyph))
        {
            fill.Add(c.Verb == PathVerb.Close
                ? c
                : new PathCommand(c.Verb, glyph.X + (c.X * sx), glyph.Y - (c.Y * sy), glyph.X + (c.X1 * sx), glyph.Y - (c.Y1 * sy)));
        }
    }

    // ---- SVG --------------------------------------------------------------------------------------

    /// <summary>
    /// One line as a self-contained SVG (paths only — no text, fonts, images, scripts or external
    /// references), sized in points for QuestPDF.
    /// </summary>
    public static string ToSvg(MathRenderedLine line, string color)
    {
        var builder = new StringBuilder(line.Fill.Count * 24);
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(F(line.Width)).Append("\" height=\"").Append(F(line.Height))
            .Append("\" viewBox=\"0 0 ").Append(F(line.Width)).Append(' ').Append(F(line.Height)).Append("\">");

        if (line.Fill.Count > 0)
        {
            builder.Append("<path fill=\"").Append(color).Append("\" fill-rule=\"nonzero\" d=\"");
            foreach (var c in line.Fill)
            {
                switch (c.Verb)
                {
                    case PathVerb.MoveTo: builder.Append('M').Append(F(c.X)).Append(' ').Append(F(c.Y)); break;
                    case PathVerb.LineTo: builder.Append('L').Append(F(c.X)).Append(' ').Append(F(c.Y)); break;
                    case PathVerb.QuadTo: builder.Append('Q').Append(F(c.X1)).Append(' ').Append(F(c.Y1)).Append(' ').Append(F(c.X)).Append(' ').Append(F(c.Y)); break;
                    case PathVerb.Close: builder.Append('Z'); break;
                }
            }

            builder.Append("\"/>");
        }

        foreach (var stroke in line.Strokes)
        {
            builder.Append("<path fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(F(stroke.Thickness))
                .Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"");
            for (var i = 0; i < stroke.Points.Count; i++)
            {
                builder.Append(i == 0 ? 'M' : 'L').Append(F(stroke.Points[i].X)).Append(' ').Append(F(stroke.Points[i].Y));
            }

            builder.Append("\"/>");
        }

        builder.Append("</svg>");
        return builder.ToString();
    }

    private static string F(double value) => System.Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
