using Carowinds.Passdown.Application.DailyFeatureMath;

namespace Carowinds.Passdown.Documents.DailyFeatureMath;

/// <summary>TeX's four math styles: full-size display/text, and the two reduced script sizes.</summary>
internal enum MathStyle
{
    Display,
    Text,
    Script,
    ScriptScript,
}

/// <summary>
/// Phase 21D: lays out a <see cref="MathNode"/> tree into a <see cref="MathBox"/> (units: em of the
/// base font size, y downward, baseline at 0), following TeX's rules closely enough for correct
/// mathematical reading: atom spacing by class, reduced sizes for scripts and fraction parts,
/// fraction bars on the math axis, stacked limits for display Σ/Π/lim, side scripts for integrals,
/// radicals sized to their body, and delimiters that grow around their contents. Glyph metrics come
/// from the bundled STIX Two Math font, so the preview and every PDF place things identically.
/// </summary>
internal sealed class MathLayoutEngine(MathFontSet fonts)
{
    // Font-relative constants (em), modelled on STIX Two Math's own MATH-table values.
    private const double AxisHeight = 0.258;
    private const double RuleThickness = 0.068;
    private const double FractionNumShiftUp = 0.394;
    private const double FractionNumShiftUpDisplay = 0.677;
    private const double FractionDenShiftDown = 0.345;
    private const double FractionDenShiftDownDisplay = 0.686;
    private const double FractionGap = 0.068;
    private const double FractionGapDisplay = 0.17;
    private const double StackGap = 0.2;
    private const double StackGapDisplay = 0.45;
    private const double SuperscriptShiftUp = 0.363;
    private const double SuperscriptBottomMin = 0.108;
    private const double SuperscriptDrop = 0.27;
    private const double SubscriptShiftDown = 0.247;
    private const double SubscriptTopMax = 0.344;
    private const double SubscriptDrop = 0.06;
    private const double SubSuperscriptGapMin = 0.264;
    private const double ScriptSpace = 0.04;
    private const double UpperLimitGapMin = 0.135;
    private const double UpperLimitBaselineRiseMin = 0.2;
    private const double LowerLimitGapMin = 0.135;
    private const double LowerLimitBaselineDropMin = 0.6;
    private const double LimitExtraSpace = 0.06;
    private const double RadicalGap = 0.085;
    private const double RadicalGapDisplay = 0.17;
    private const double NullDelimiterSpace = 0.12;

    private readonly MathFontSet _fonts = fonts;

    public static double Scale(MathStyle style) => style switch
    {
        MathStyle.Script => 0.7,
        MathStyle.ScriptScript => 0.5,
        _ => 1.0,
    };

    private static MathStyle ScriptStyle(MathStyle style) => style is MathStyle.Display or MathStyle.Text ? MathStyle.Script : MathStyle.ScriptScript;

    private static MathStyle FractionStyle(MathStyle style) => style switch
    {
        MathStyle.Display => MathStyle.Text,
        MathStyle.Text => MathStyle.Script,
        _ => MathStyle.ScriptScript,
    };

    public MathBox Layout(MathNode node, MathStyle style) => node switch
    {
        MathRow row => LayoutRow(row.Items, style),
        MathSymbol symbol => LayoutSymbol(symbol, style),
        MathLargeOperator op => LayoutLargeOperator(op, style),
        MathFunctionName fn => LayoutFunctionName(fn, style),
        MathScripts scripts => LayoutScripts(scripts, style),
        MathFraction fraction => LayoutFraction(fraction, style),
        MathRadical radical => LayoutRadical(radical, style),
        MathDelimited delimited => LayoutDelimited(delimited.Left, Layout(delimited.Body, style), delimited.Right, style),
        MathSpace space => MathBox.Empty(space.Em * Scale(style)),
        MathText text => LayoutText(text.Text, style),
        MathAccent accent => LayoutAccent(accent, style),
        MathMatrix matrix => LayoutMatrix(matrix, style),
        _ => MathBox.Empty(),
    };

    /// <summary>
    /// The top-level row of a formula as consecutive pieces, split after each binary operator or
    /// relation (where TeX allows an inline formula to break across lines). The pieces placed side by
    /// side reproduce the unsplit row exactly.
    /// </summary>
    public IReadOnlyList<MathBox> LayoutBreakablePieces(MathNode root, MathStyle style)
    {
        if (root is not MathRow row)
        {
            return [Layout(root, style)];
        }

        var atoms = LayoutAtoms(row.Items, style);
        var pieces = new List<MathBox>();
        var current = NewRowBox();
        var x = 0.0;
        for (var i = 0; i < atoms.Count; i++)
        {
            var (box, spaceBefore) = atoms[i];
            x += spaceBefore;
            current.Place(box, x, 0);
            x += box.Width;
            Extend(current, box);

            var breakAfter = box.Class is MathAtomClass.Bin or MathAtomClass.Rel && i < atoms.Count - 1;
            if (breakAfter)
            {
                // Keep the space that follows the operator with this piece, so the joined pieces are identical.
                var spaceAfter = atoms[i + 1].SpaceBefore;
                x += spaceAfter;
                current.Width = x;
                pieces.Add(current);
                current = NewRowBox();
                x = 0;
                atoms[i + 1] = (atoms[i + 1].Box, 0);
            }
        }

        current.Width = x;
        pieces.Add(current);
        return pieces;
    }

    private static MathBox NewRowBox() => new() { Class = MathAtomClass.Ord };

    private static void Extend(MathBox target, MathBox part)
    {
        target.Height = System.Math.Max(target.Height, part.Height);
        target.Depth = System.Math.Max(target.Depth, part.Depth);
    }

    private MathBox LayoutRow(IReadOnlyList<MathNode> items, MathStyle style)
    {
        var atoms = LayoutAtoms(items, style);
        if (atoms.Count == 1 && atoms[0].SpaceBefore == 0)
        {
            var single = atoms[0].Box;
            return single;
        }

        var row = NewRowBox();
        var x = 0.0;
        foreach (var (box, spaceBefore) in atoms)
        {
            x += spaceBefore;
            row.Place(box, x, 0);
            x += box.Width;
            Extend(row, box);
            row.ItalicCorrection = box.ItalicCorrection;
        }

        row.Width = x;
        return row;
    }

    /// <summary>Each item's box with the TeX inter-atom space that precedes it (binary operators demoted to ordinary where TeX does).</summary>
    private List<(MathBox Box, double SpaceBefore)> LayoutAtoms(IReadOnlyList<MathNode> items, MathStyle style)
    {
        var boxes = items.Select(item => Layout(item, style)).ToList();

        // TeX rule: a Bin with nothing (or an operator, relation, opening or punctuation) before it, or
        // a relation, closing or punctuation after it, is an ordinary symbol ("-1", "(−x)").
        MathBox? previous = null;
        foreach (var box in boxes)
        {
            if (box.Class is null)
            {
                continue;
            }

            if (box.Class == MathAtomClass.Bin && (previous is null || previous.Class is MathAtomClass.Bin or MathAtomClass.Op or MathAtomClass.Rel or MathAtomClass.Open or MathAtomClass.Punct))
            {
                box.Class = MathAtomClass.Ord;
            }

            if (box.Class is MathAtomClass.Rel or MathAtomClass.Close or MathAtomClass.Punct && previous?.Class == MathAtomClass.Bin)
            {
                previous.Class = MathAtomClass.Ord;
            }

            previous = box;
        }

        if (previous?.Class == MathAtomClass.Bin)
        {
            previous.Class = MathAtomClass.Ord;
        }

        var result = new List<(MathBox, double)>(boxes.Count);
        MathAtomClass? left = null;
        foreach (var box in boxes)
        {
            var space = 0.0;
            if (box.Class is { } right)
            {
                if (left is { } l)
                {
                    space = InterAtomSpace(l, right, style);
                }

                left = right;
            }

            result.Add((box, space));
        }

        return result;
    }

    // TeX's spacing table (The TeXbook, chapter 18): 0 none, 1 thin, 2 medium, 3 thick; negative = only in display/text style.
    private static readonly int[,] SpacingTable =
    {
        //          Ord  Op  Bin  Rel Open Close Punct Inner
        /* Ord */  { 0,   1,  -2,  -3,  0,   0,    0,   -1 },
        /* Op  */  { 1,   1,   0,  -3,  0,   0,    0,   -1 },
        /* Bin */  { -2, -2,   0,   0, -2,   0,    0,   -2 },
        /* Rel */  { -3, -3,   0,   0, -3,   0,    0,   -3 },
        /* Open */ { 0,   0,   0,   0,  0,   0,    0,    0 },
        /* Close */{ 0,   1,  -2,  -3,  0,   0,    0,   -1 },
        /* Punct */{ -1, -1,   0,  -1, -1,  -1,   -1,   -1 },
        /* Inner */{ -1,  1,  -2,  -3, -1,   0,   -1,   -1 },
    };

    private static double InterAtomSpace(MathAtomClass left, MathAtomClass right, MathStyle style)
    {
        var code = SpacingTable[(int)left, (int)right];
        if (code < 0)
        {
            if (style is MathStyle.Script or MathStyle.ScriptScript)
            {
                return 0;
            }

            code = -code;
        }

        var mu = code switch { 1 => 3.0, 2 => 4.0, 3 => 5.0, _ => 0.0 };
        return mu / 18.0 * Scale(style);
    }

    // ---- Glyphs -----------------------------------------------------------------------------------

    /// <summary>Lays out a run of characters from the math font (falling back to the prose font, then a visible .notdef box).</summary>
    private MathBox LayoutGlyphRun(string text, double size, MathAtomClass? cls, MathGlyphStyle glyphStyle = MathGlyphStyle.Upright)
    {
        var box = new MathBox { Class = cls };
        var x = 0.0;
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var (font, glyph) = ResolveGlyph(MapStyle(rune.Value, glyphStyle));
            var metrics = font.GetMetrics(glyph);
            box.Add(new MathGlyphItem(font, glyph, x, 0, size));
            box.Height = System.Math.Max(box.Height, metrics.YMax * size);
            box.Depth = System.Math.Max(box.Depth, -metrics.YMin * size);
            box.ItalicCorrection = System.Math.Max(0, (metrics.XMax - metrics.Advance) * size);
            x += metrics.Advance * size;
            count++;
        }

        box.Width = x;
        box.IsSimpleSymbol = count == 1;
        return box;
    }

    private (TrueTypeFont Font, ushort Glyph) ResolveGlyph(int codePoint)
    {
        if (_fonts.Math.TryGetGlyph(codePoint, out var glyph))
        {
            return (_fonts.Math, glyph);
        }

        if (_fonts.ProseRegular.TryGetGlyph(codePoint, out glyph))
        {
            return (_fonts.ProseRegular, glyph);
        }

        return (_fonts.Math, 0); // .notdef: visibly "missing", never silently dropped
    }

    /// <summary>Maps a letter to its Unicode Mathematical Alphanumeric form (math italic x, bold, double-struck ℝ…), when the font has it.</summary>
    private int MapStyle(int codePoint, MathGlyphStyle style)
    {
        var mapped = style switch
        {
            MathGlyphStyle.Italic => codePoint switch
            {
                'h' => 0x210E,
                >= 'A' and <= 'Z' => 0x1D434 + (codePoint - 'A'),
                >= 'a' and <= 'z' => 0x1D44E + (codePoint - 'a'),
                >= 0x3B1 and <= 0x3C9 => 0x1D6FC + (codePoint - 0x3B1),
                0x3F5 => 0x1D716, // ϵ
                0x3D1 => 0x1D717, // ϑ
                0x3D5 => 0x1D719, // ϕ
                0x3F1 => 0x1D71A, // ϱ
                0x3D6 => 0x1D71B, // ϖ
                _ => codePoint,
            },
            MathGlyphStyle.Bold => codePoint switch
            {
                >= 'A' and <= 'Z' => 0x1D400 + (codePoint - 'A'),
                >= 'a' and <= 'z' => 0x1D41A + (codePoint - 'a'),
                >= '0' and <= '9' => 0x1D7CE + (codePoint - '0'),
                _ => codePoint,
            },
            MathGlyphStyle.DoubleStruck => codePoint switch
            {
                'C' => 0x2102, 'H' => 0x210D, 'N' => 0x2115, 'P' => 0x2119, 'Q' => 0x211A, 'R' => 0x211D, 'Z' => 0x2124,
                >= 'A' and <= 'Z' => 0x1D538 + (codePoint - 'A'),
                >= 'a' and <= 'z' => 0x1D552 + (codePoint - 'a'),
                >= '0' and <= '9' => 0x1D7D8 + (codePoint - '0'),
                _ => codePoint,
            },
            _ => codePoint,
        };

        return mapped != codePoint && !_fonts.Math.TryGetGlyph(mapped, out _) ? codePoint : mapped;
    }

    private MathBox LayoutSymbol(MathSymbol symbol, MathStyle style) =>
        LayoutGlyphRun(symbol.Text, Scale(style), symbol.Class, symbol.Style);

    private MathBox LayoutText(string text, MathStyle style)
    {
        var box = LayoutGlyphRun(text, Scale(style), MathAtomClass.Ord);
        box.IsSimpleSymbol = false;
        return box;
    }

    private MathBox LayoutFunctionName(MathFunctionName fn, MathStyle style)
    {
        var box = LayoutGlyphRun(fn.Name, Scale(style), MathAtomClass.Op);
        box.IsSimpleSymbol = false;
        box.ItalicCorrection = 0;
        return box;
    }

    /// <summary>Σ, ∫…: enlarged in display style and centered on the math axis.</summary>
    private MathBox LayoutLargeOperator(MathLargeOperator op, MathStyle style)
    {
        var s = Scale(style);
        var enlarge = style == MathStyle.Display ? (op.IsIntegral ? 1.75 : 1.4) : (op.IsIntegral ? 1.1 : 1.0);
        var size = s * enlarge;
        var (font, glyph) = ResolveGlyph(char.ConvertToUtf32(op.Symbol, 0));
        var metrics = font.GetMetrics(glyph);

        // Center the ink on the axis.
        var inkCenter = (metrics.YMax + metrics.YMin) / 2 * size;
        var baselineShift = inkCenter - (AxisHeight * s); // move the glyph down by this (y-down)
        var box = new MathBox
        {
            Class = MathAtomClass.Op,
            Width = metrics.Advance * size,
            Height = (metrics.YMax * size) - baselineShift,
            Depth = (-metrics.YMin * size) + baselineShift,
            ItalicCorrection = System.Math.Max(0, (metrics.XMax - metrics.Advance) * size),
        };
        box.Add(new MathGlyphItem(font, glyph, 0, baselineShift, size));
        return box;
    }

    // ---- Scripts and limits -----------------------------------------------------------------------

    private MathBox LayoutScripts(MathScripts scripts, MathStyle style)
    {
        var useLimits = scripts.Base switch
        {
            MathLargeOperator op => op.Limits ?? (!op.IsIntegral && style == MathStyle.Display),
            MathFunctionName fn => fn.Limits ?? (fn.TakesLimits && style == MathStyle.Display),
            _ => false,
        };

        var nucleus = Layout(scripts.Base, style);
        var scriptStyle = ScriptStyle(style);
        var sup = scripts.Superscript is null ? null : Layout(scripts.Superscript, scriptStyle);
        var sub = scripts.Subscript is null ? null : Layout(scripts.Subscript, scriptStyle);

        return useLimits ? LayoutLimits(nucleus, sup, sub, style) : LayoutSideScripts(nucleus, sup, sub, style, scripts.Base is MathLargeOperator { IsIntegral: true });
    }

    private static MathBox LayoutSideScripts(MathBox nucleus, MathBox? sup, MathBox? sub, MathStyle style, bool isIntegral)
    {
        var s = Scale(style);
        var scriptScale = Scale(ScriptStyle(style));
        var simple = nucleus.IsSimpleSymbol && nucleus.Class != MathAtomClass.Op;

        var u = 0.0;
        if (sup is not null)
        {
            u = System.Math.Max(SuperscriptShiftUp * s, sup.Depth + (SuperscriptBottomMin * s));
            if (!simple)
            {
                u = System.Math.Max(u, nucleus.Height - (SuperscriptDrop * scriptScale));
            }
        }

        var v = 0.0;
        if (sub is not null)
        {
            v = System.Math.Max(SubscriptShiftDown * s, sub.Height - (SubscriptTopMax * s));
            if (!simple)
            {
                v = System.Math.Max(v, nucleus.Depth + (SubscriptDrop * scriptScale));
            }
        }

        if (sup is not null && sub is not null)
        {
            var gap = (u - sup.Depth) - (sub.Height - v);
            if (gap < SubSuperscriptGapMin * s)
            {
                var extra = (SubSuperscriptGapMin * s) - gap;
                v += extra * 0.6;
                u += extra * 0.4;
            }
        }

        var box = new MathBox { Class = nucleus.Class };
        box.Place(nucleus, 0, 0);
        box.Height = nucleus.Height;
        box.Depth = nucleus.Depth;

        var supX = nucleus.Width + nucleus.ItalicCorrection;
        var subX = isIntegral ? nucleus.Width - (nucleus.ItalicCorrection * 0.6) : nucleus.Width;
        var right = nucleus.Width;
        if (sup is not null)
        {
            box.Place(sup, supX, -u);
            box.Height = System.Math.Max(box.Height, u + sup.Height);
            right = System.Math.Max(right, supX + sup.Width);
        }

        if (sub is not null)
        {
            box.Place(sub, subX, v);
            box.Depth = System.Math.Max(box.Depth, v + sub.Depth);
            right = System.Math.Max(right, subX + sub.Width);
        }

        box.Width = right + (ScriptSpace * s);
        return box;
    }

    private static MathBox LayoutLimits(MathBox nucleus, MathBox? sup, MathBox? sub, MathStyle style)
    {
        var s = Scale(style);
        var width = System.Math.Max(nucleus.Width, System.Math.Max(sup?.Width ?? 0, sub?.Width ?? 0));
        var box = new MathBox { Class = MathAtomClass.Op, Width = width };
        box.Place(nucleus, (width - nucleus.Width) / 2, 0);
        box.Height = nucleus.Height;
        box.Depth = nucleus.Depth;

        if (sup is not null)
        {
            var rise = nucleus.Height + System.Math.Max((UpperLimitGapMin * s) + sup.Depth, UpperLimitBaselineRiseMin * s);
            box.Place(sup, (width - sup.Width) / 2, -rise);
            box.Height = rise + sup.Height + (LimitExtraSpace * s);
        }

        if (sub is not null)
        {
            var drop = nucleus.Depth + System.Math.Max((LowerLimitGapMin * s) + sub.Height, LowerLimitBaselineDropMin * s * 0.6);
            box.Place(sub, (width - sub.Width) / 2, drop);
            box.Depth = drop + sub.Depth + (LimitExtraSpace * s);
        }

        return box;
    }

    // ---- Fractions --------------------------------------------------------------------------------

    private MathBox LayoutFraction(MathFraction fraction, MathStyle style)
    {
        var effective = fraction.ForceDisplay switch
        {
            true => MathStyle.Display,
            false when style == MathStyle.Display => MathStyle.Text,
            _ => style,
        };

        var s = Scale(effective);
        var display = effective == MathStyle.Display;
        var partStyle = FractionStyle(effective);
        var num = Layout(fraction.Numerator, partStyle);
        var den = Layout(fraction.Denominator, partStyle);

        var axis = AxisHeight * s;
        var rule = RuleThickness * s;
        var u = (display ? FractionNumShiftUpDisplay : FractionNumShiftUp) * s;
        var v = (display ? FractionDenShiftDownDisplay : FractionDenShiftDown) * s;

        if (fraction.HasRule)
        {
            var gap = (display ? FractionGapDisplay : FractionGap) * s;
            var numClearance = (u - num.Depth) - (axis + (rule / 2));
            if (numClearance < gap)
            {
                u += gap - numClearance;
            }

            var denClearance = (axis - (rule / 2)) - (den.Height - v);
            if (denClearance < gap)
            {
                v += gap - denClearance;
            }
        }
        else
        {
            var gap = (display ? StackGapDisplay : StackGap) * s;
            var clearance = (u - num.Depth) - (den.Height - v);
            if (clearance < gap)
            {
                u += (gap - clearance) / 2;
                v += (gap - clearance) / 2;
            }
        }

        var overhang = 0.08 * s;
        var outer = NullDelimiterSpace * s * 0.5;
        var inner = System.Math.Max(num.Width, den.Width) + (2 * overhang);
        var box = new MathBox
        {
            Class = MathAtomClass.Inner,
            Width = inner + (2 * outer),
            Height = u + num.Height,
            Depth = v + den.Depth,
        };

        box.Place(num, outer + ((inner - num.Width) / 2), -u);
        box.Place(den, outer + ((inner - den.Width) / 2), v);
        if (fraction.HasRule)
        {
            box.Add(new MathRectItem(outer, -(axis + (rule / 2)), inner, rule));
        }

        return box;
    }

    // ---- Radicals ---------------------------------------------------------------------------------

    private MathBox LayoutRadical(MathRadical radical, MathStyle style)
    {
        var s = Scale(style);
        var body = Layout(radical.Body, style);
        var theta = RuleThickness * s;
        var phi = (style == MathStyle.Display ? RadicalGapDisplay : RadicalGap) * s;

        var ruleTop = System.Math.Max(body.Height, 0.45 * s) + phi + theta;   // above the baseline
        var surdDepth = System.Math.Max(body.Depth, 0.05 * s) + (0.06 * s);   // below the baseline
        var total = ruleTop + surdDepth;
        var surdWidth = System.Math.Min((0.5 * s) + (0.08 * total), 1.1 * s);

        var shift = 0.0;
        MathBox? index = null;
        var indexBaseline = 0.0;
        if (radical.Index is not null)
        {
            index = Layout(radical.Index, MathStyle.ScriptScript);
            indexBaseline = -(-surdDepth + (0.62 * total)); // y-down position of the index baseline
            var room = 0.55 * surdWidth;
            shift = System.Math.Max(0, index.Width - room + (0.05 * s));
        }

        // Surd: a short rising tick, a heavy down-stroke, then a thin up-stroke meeting the bar.
        double X(double fraction) => shift + (fraction * surdWidth);
        var tickStart = (X(0.0), -(-surdDepth + (System.Math.Min(total, 1.2 * s) * 0.42)));
        var tickEnd = (X(0.2), tickStart.Item2 - (0.07 * s));
        var bottom = (X(0.48), surdDepth - (theta * 0.6));
        var top = (X(1.0), -(ruleTop - (theta / 2)));

        var box = new MathBox { Class = MathAtomClass.Ord };
        box.Add(new MathStrokeItem([tickStart, tickEnd], theta * 1.1));
        box.Add(new MathStrokeItem([tickEnd, bottom], theta * 2.1));
        box.Add(new MathStrokeItem([bottom, top], theta));

        var bodyX = shift + surdWidth + (0.05 * s);
        var barEnd = bodyX + body.Width + (0.06 * s);
        box.Add(new MathRectItem(X(1.0) - (theta / 2), -ruleTop, barEnd - X(1.0) + (theta / 2), theta));
        box.Place(body, bodyX, 0);

        box.Width = barEnd + (0.04 * s);
        box.Height = ruleTop + (theta * 0.5);
        box.Depth = surdDepth;

        if (index is not null)
        {
            var indexX = System.Math.Max(0, X(0.55) - index.Width);
            box.Place(index, indexX, indexBaseline);
            box.Height = System.Math.Max(box.Height, -indexBaseline + index.Height);
        }

        return box;
    }

    // ---- Delimiters, accents, matrices ------------------------------------------------------------

    private MathBox LayoutDelimited(string? left, MathBox body, string? right, MathStyle style)
    {
        var s = Scale(style);
        var axis = AxisHeight * s;
        var delta = System.Math.Max(body.Height - axis, body.Depth + axis);
        var target = System.Math.Max(2 * delta * 0.901, (2 * delta) - (0.5 * s));

        var leftBox = LayoutDelimiter(left, target, style, MathAtomClass.Open);
        var rightBox = LayoutDelimiter(right, target, style, MathAtomClass.Close);

        var box = new MathBox { Class = MathAtomClass.Inner };
        box.Place(leftBox, 0, 0);
        box.Place(body, leftBox.Width, 0);
        box.Place(rightBox, leftBox.Width + body.Width, 0);
        box.Width = leftBox.Width + body.Width + rightBox.Width;
        box.Height = System.Math.Max(body.Height, System.Math.Max(leftBox.Height, rightBox.Height));
        box.Depth = System.Math.Max(body.Depth, System.Math.Max(leftBox.Depth, rightBox.Depth));
        return box;
    }

    /// <summary>A delimiter at least <paramref name="target"/> tall (ink), centered on the axis; grown by vertical scaling of the font's own glyph.</summary>
    private MathBox LayoutDelimiter(string? delimiter, double target, MathStyle style, MathAtomClass cls)
    {
        var s = Scale(style);
        if (delimiter is null)
        {
            return new MathBox { Width = NullDelimiterSpace * s, Class = cls };
        }

        var (font, glyph) = ResolveGlyph(char.ConvertToUtf32(delimiter, 0));
        var metrics = font.GetMetrics(glyph);
        var natural = (metrics.YMax - metrics.YMin) * s;
        if (natural <= 0 || target <= natural * 1.05)
        {
            var plain = LayoutGlyphRun(delimiter, s, cls);
            plain.IsSimpleSymbol = false;
            return plain;
        }

        var scaleY = target / natural;
        var scaleX = System.Math.Min(1 + ((scaleY - 1) * 0.12), 1.6);
        var axis = AxisHeight * s;
        var inkCenter = (metrics.YMax + metrics.YMin) / 2 * s * scaleY;
        var baselineShift = inkCenter - axis;
        var box = new MathBox
        {
            Class = cls,
            Width = metrics.Advance * s * scaleX,
            Height = (metrics.YMax * s * scaleY) - baselineShift,
            Depth = (-metrics.YMin * s * scaleY) + baselineShift,
        };
        box.Add(new MathGlyphItem(font, glyph, 0, baselineShift, s, scaleX, scaleY));
        return box;
    }

    private MathBox LayoutAccent(MathAccent accent, MathStyle style)
    {
        var s = Scale(style);
        var body = Layout(accent.Body, style);
        var box = new MathBox { Class = MathAtomClass.Ord, Width = body.Width, Height = body.Height, Depth = body.Depth, ItalicCorrection = body.ItalicCorrection };
        box.Place(body, 0, 0);
        var theta = RuleThickness * s;
        var skew = body.IsSimpleSymbol ? body.ItalicCorrection * 0.5 : 0;

        switch (accent.Kind)
        {
            case MathAccentKind.Overline:
            {
                var top = body.Height + (0.12 * s) + theta;
                box.Add(new MathRectItem(0, -top, body.Width, theta));
                box.Height = top + (0.05 * s);
                return box;
            }

            case MathAccentKind.Underline:
            {
                var y = body.Depth + (0.12 * s);
                box.Add(new MathRectItem(0, y, body.Width, theta));
                box.Depth = y + theta + (0.05 * s);
                return box;
            }

            case MathAccentKind.Bar:
            {
                var width = System.Math.Max(0.35 * s, body.Width * 0.8);
                var top = System.Math.Max(body.Height, 0.45 * s) + (0.1 * s) + theta;
                box.Add(new MathRectItem(((body.Width - width) / 2) + skew, -top, width, theta));
                box.Height = top + (0.05 * s);
                return box;
            }
        }

        var accentChar = accent.Kind switch
        {
            MathAccentKind.Hat => "ˆ",
            MathAccentKind.Tilde => "˜",
            MathAccentKind.Dot => "˙",
            MathAccentKind.DoubleDot => "¨",
            _ => "→",
        };
        var accentScale = accent.Kind == MathAccentKind.Vec ? 0.7 : 1.0;
        var (font, glyph) = ResolveGlyph(char.ConvertToUtf32(accentChar, 0));
        var m = font.GetMetrics(glyph);
        var size = s * accentScale;

        // Ink bottom sits a small gap above the body (or at the accent's own natural height, if higher).
        var gap = 0.06 * s;
        var inkBottom = System.Math.Max(body.Height + gap, m.YMin * size);
        var baseline = -(inkBottom - (m.YMin * size)); // y-down position of the accent's baseline
        var inkCenterX = ((m.XMin + m.XMax) / 2) * size;
        var x = (body.Width / 2) + skew - inkCenterX;
        box.Add(new MathGlyphItem(font, glyph, x, baseline, size));
        box.Height = System.Math.Max(body.Height, -baseline + (m.YMax * size));
        return box;
    }

    private MathBox LayoutMatrix(MathMatrix matrix, MathStyle style)
    {
        var cellStyle = style == MathStyle.Display ? MathStyle.Text : style;
        var s = Scale(cellStyle);
        var cells = matrix.Rows.Select(row => row.Select(cell => Layout(cell, cellStyle)).ToList()).ToList();
        var columns = cells.Count == 0 ? 0 : cells.Max(row => row.Count);
        var widths = new double[columns];
        foreach (var row in cells)
        {
            for (var j = 0; j < row.Count; j++)
            {
                widths[j] = System.Math.Max(widths[j], row[j].Width);
            }
        }

        var columnGap = 0.8 * s;
        var grid = new MathBox { Class = MathAtomClass.Ord };
        var baseline = 0.0;
        double top = 0;
        double bottom = 0;
        for (var i = 0; i < cells.Count; i++)
        {
            var row = cells[i];
            var height = row.Count == 0 ? 0.7 * s : System.Math.Max(0.7 * s, row.Max(c => c.Height));
            var depth = row.Count == 0 ? 0.3 * s : System.Math.Max(0.3 * s, row.Max(c => c.Depth));
            if (i == 0)
            {
                top = -height;
            }
            else
            {
                var previousDepth = System.Math.Max(0.3 * s, cells[i - 1].Count == 0 ? 0 : cells[i - 1].Max(c => c.Depth));
                baseline += System.Math.Max(previousDepth + height + (0.25 * s), 1.2 * s);
            }

            var x = 0.0;
            for (var j = 0; j < columns; j++)
            {
                if (j < row.Count)
                {
                    grid.Place(row[j], x + ((widths[j] - row[j].Width) / 2), baseline);
                }

                x += widths[j] + (j < columns - 1 ? columnGap : 0);
            }

            bottom = baseline + depth;
        }

        grid.Width = widths.Sum() + (System.Math.Max(0, columns - 1) * columnGap);

        // Center the grid on the math axis.
        var axis = AxisHeight * s;
        var center = (top + bottom) / 2;
        var dy = -axis - center;
        var centered = new MathBox { Class = MathAtomClass.Ord, Width = grid.Width + (0.2 * s), Height = -(top + dy), Depth = bottom + dy };
        centered.Place(grid, 0.1 * s, dy);

        if (matrix.Left is null && matrix.Right is null)
        {
            return centered;
        }

        return LayoutDelimited(matrix.Left, centered, matrix.Right, cellStyle);
    }
}
