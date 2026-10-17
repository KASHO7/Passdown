using Carowinds.Passdown.Application.DailyFeatureMath;

namespace Carowinds.Passdown.Documents.DailyFeatureMath;

/// <summary>A positioned primitive inside a <see cref="MathBox"/> (y grows downward; the box baseline is y = 0).</summary>
internal abstract record MathItem
{
    public abstract MathItem Offset(double dx, double dy);
}

/// <summary>One glyph drawn with its baseline-left at (<see cref="X"/>, <see cref="Y"/>), <see cref="Size"/> = em size (in the box's unit).</summary>
internal sealed record MathGlyphItem(TrueTypeFont Font, ushort Glyph, double X, double Y, double Size, double ScaleX = 1, double ScaleY = 1) : MathItem
{
    public override MathItem Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
}

/// <summary>A filled rectangle (fraction bars, radical bars, overlines). <see cref="Y"/> is its top edge.</summary>
internal sealed record MathRectItem(double X, double Y, double Width, double Height) : MathItem
{
    public override MathItem Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
}

/// <summary>A stroked polyline with round caps (the radical sign).</summary>
internal sealed record MathStrokeItem(IReadOnlyList<(double X, double Y)> Points, double Thickness) : MathItem
{
    public override MathItem Offset(double dx, double dy) => this with { Points = Points.Select(p => (p.X + dx, p.Y + dy)).ToArray() };
}

/// <summary>
/// Phase 21D: a TeX-style box — width, height above the baseline, depth below it — holding positioned
/// primitives. Units are whatever the caller lays out in (em of the base size inside the engine,
/// points once a paragraph is placed).
/// </summary>
internal sealed class MathBox
{
    private readonly List<MathItem> _items = [];

    public double Width { get; set; }

    public double Height { get; set; }

    public double Depth { get; set; }

    /// <summary>The atom class used for inter-atom spacing; null for explicit space (which never triggers spacing).</summary>
    public MathAtomClass? Class { get; set; } = MathAtomClass.Ord;

    /// <summary>How far the last glyph's ink overhangs its advance (TeX's italic correction), for superscript placement.</summary>
    public double ItalicCorrection { get; set; }

    /// <summary>True when the box is one plain symbol (scripts then use the font's standard shifts, not the base's own height/depth).</summary>
    public bool IsSimpleSymbol { get; set; }

    public IReadOnlyList<MathItem> Items => _items;

    public void Add(MathItem item) => _items.Add(item);

    /// <summary>Copies <paramref name="other"/>'s primitives into this box with its baseline-left at (dx, dy).</summary>
    public void Place(MathBox other, double dx, double dy)
    {
        foreach (var item in other._items)
        {
            _items.Add(item.Offset(dx, dy));
        }
    }

    /// <summary>A copy uniformly scaled by <paramref name="factor"/> about the baseline-left origin.</summary>
    public MathBox Scaled(double factor)
    {
        var box = new MathBox
        {
            Width = Width * factor,
            Height = Height * factor,
            Depth = Depth * factor,
            Class = Class,
            ItalicCorrection = ItalicCorrection * factor,
            IsSimpleSymbol = IsSimpleSymbol,
        };

        foreach (var item in _items)
        {
            box._items.Add(item switch
            {
                MathGlyphItem g => g with { X = g.X * factor, Y = g.Y * factor, Size = g.Size * factor },
                MathRectItem r => new MathRectItem(r.X * factor, r.Y * factor, r.Width * factor, r.Height * factor),
                MathStrokeItem s => new MathStrokeItem(s.Points.Select(p => (p.X * factor, p.Y * factor)).ToArray(), s.Thickness * factor),
                _ => item,
            });
        }

        return box;
    }

    public static MathBox Empty(double width = 0) => new() { Width = width, Class = null };
}
