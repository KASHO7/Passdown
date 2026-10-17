using System.Buffers.Binary;

namespace Carowinds.Passdown.Documents.DailyFeatureMath;

/// <summary>One drawing command in a glyph outline or a laid-out path (y grows downward once placed).</summary>
public enum PathVerb : byte
{
    MoveTo,
    LineTo,
    QuadTo,
    Close,
}

/// <summary>A path command; <see cref="X1"/>/<see cref="Y1"/> are the control point of a quadratic curve.</summary>
public readonly record struct PathCommand(PathVerb Verb, double X, double Y, double X1 = 0, double Y1 = 0);

/// <summary>A glyph's horizontal advance and ink bounds, in em (y up, baseline 0).</summary>
public readonly record struct GlyphMetrics(double Advance, double XMin, double YMin, double XMax, double YMax);

/// <summary>
/// Phase 21D: a minimal, read-only TrueType ("glyf"-outline) font reader — just the tables the math
/// layout needs: cmap (formats 4 and 12), hmtx/hhea, head, maxp, loca and glyf (simple and composite
/// glyphs). It reads the bundled, trusted font bytes only (never a user-supplied font) and fails
/// closed: a malformed table raises <see cref="InvalidDataException"/> at load, which callers treat as
/// "math renderer unavailable". Outlines are cached per glyph; the cache is bounded by the font's own
/// glyph count. Thread-safe after construction.
/// </summary>
public sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<int, ushort> _cmap;
    private readonly ushort[] _advances;
    private readonly short[] _leftSideBearings;
    private readonly uint[] _glyphOffsets;
    private readonly uint _glyfOffset;
    private readonly Dictionary<ushort, IReadOnlyList<PathCommand>> _outlineCache = [];
    private readonly Lock _outlineGate = new();

    public TrueTypeFont(byte[] data, string name)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        Name = name;

        var tables = ReadTableDirectory();
        uint Table(string tag) => tables.TryGetValue(tag, out var offset)
            ? offset
            : throw new InvalidDataException($"Font '{name}' has no '{tag}' table.");

        var head = Table("head");
        UnitsPerEm = U16(head + 18);
        var indexToLocFormat = S16(head + 50);

        var hhea = Table("hhea");
        Ascender = S16(hhea + 4) / (double)UnitsPerEm;
        Descender = -S16(hhea + 6) / (double)UnitsPerEm;
        var numberOfHMetrics = U16(hhea + 34);

        GlyphCount = U16(Table("maxp") + 4);

        var hmtx = Table("hmtx");
        _advances = new ushort[GlyphCount];
        _leftSideBearings = new short[GlyphCount];
        ushort lastAdvance = 0;
        for (var glyph = 0; glyph < GlyphCount; glyph++)
        {
            if (glyph < numberOfHMetrics)
            {
                lastAdvance = U16(hmtx + (uint)(glyph * 4));
                _leftSideBearings[glyph] = S16(hmtx + (uint)(glyph * 4) + 2);
            }
            else
            {
                _leftSideBearings[glyph] = S16(hmtx + (uint)(numberOfHMetrics * 4) + (uint)((glyph - numberOfHMetrics) * 2));
            }

            _advances[glyph] = lastAdvance;
        }

        var loca = Table("loca");
        _glyfOffset = Table("glyf");
        _glyphOffsets = new uint[GlyphCount + 1];
        for (var glyph = 0; glyph <= GlyphCount; glyph++)
        {
            _glyphOffsets[glyph] = indexToLocFormat == 0
                ? U16(loca + (uint)(glyph * 2)) * 2u
                : U32(loca + (uint)(glyph * 4));
        }

        _cmap = ReadCmap(Table("cmap"));
    }

    public string Name { get; }

    public int UnitsPerEm { get; }

    public int GlyphCount { get; }

    /// <summary>Typographic ascender in em (positive).</summary>
    public double Ascender { get; }

    /// <summary>Typographic descender in em (positive, below the baseline).</summary>
    public double Descender { get; }

    public bool TryGetGlyph(int codePoint, out ushort glyph) => _cmap.TryGetValue(codePoint, out glyph) && glyph != 0;

    public GlyphMetrics GetMetrics(ushort glyph)
    {
        if (glyph >= GlyphCount)
        {
            glyph = 0;
        }

        var advance = _advances[glyph] / (double)UnitsPerEm;
        var start = _glyphOffsets[glyph];
        if (_glyphOffsets[glyph + 1] <= start)
        {
            return new GlyphMetrics(advance, 0, 0, 0, 0); // empty glyph (space)
        }

        var offset = _glyfOffset + start;
        var em = (double)UnitsPerEm;
        return new GlyphMetrics(advance, S16(offset + 2) / em, S16(offset + 4) / em, S16(offset + 6) / em, S16(offset + 8) / em);
    }

    /// <summary>The glyph's outline in em units, y up, origin at the glyph's baseline-left.</summary>
    public IReadOnlyList<PathCommand> GetOutline(ushort glyph)
    {
        if (glyph >= GlyphCount)
        {
            glyph = 0;
        }

        lock (_outlineGate)
        {
            if (_outlineCache.TryGetValue(glyph, out var cached))
            {
                return cached;
            }
        }

        var commands = new List<PathCommand>();
        AppendOutline(glyph, 1, 0, 0, 1, 0, 0, commands, depth: 0);
        var em = (double)UnitsPerEm;
        var scaled = commands.Select(c => c with { X = c.X / em, Y = c.Y / em, X1 = c.X1 / em, Y1 = c.Y1 / em }).ToArray();

        lock (_outlineGate)
        {
            _outlineCache[glyph] = scaled;
        }

        return scaled;
    }

    private void AppendOutline(ushort glyph, double a, double b, double c, double d, double dx, double dy, List<PathCommand> commands, int depth)
    {
        if (depth > 8 || glyph >= GlyphCount)
        {
            return;
        }

        var start = _glyphOffsets[glyph];
        var end = _glyphOffsets[glyph + 1];
        if (end <= start)
        {
            return;
        }

        var offset = _glyfOffset + start;
        var contourCount = S16(offset);
        if (contourCount >= 0)
        {
            AppendSimpleGlyph(offset, contourCount, a, b, c, d, dx, dy, commands);
        }
        else
        {
            AppendCompositeGlyph(offset + 10, a, b, c, d, dx, dy, commands, depth);
        }
    }

    private void AppendSimpleGlyph(uint offset, int contourCount, double a, double b, double c, double d, double dx, double dy, List<PathCommand> commands)
    {
        var endPoints = new int[contourCount];
        var p = offset + 10;
        for (var i = 0; i < contourCount; i++)
        {
            endPoints[i] = U16(p);
            p += 2;
        }

        var pointCount = contourCount == 0 ? 0 : endPoints[^1] + 1;
        var instructionLength = U16(p);
        p += 2u + instructionLength;

        var flags = new byte[pointCount];
        for (var i = 0; i < pointCount;)
        {
            var flag = _data[p++];
            flags[i++] = flag;
            if ((flag & 8) != 0)
            {
                var repeat = _data[p++];
                for (var r = 0; r < repeat && i < pointCount; r++)
                {
                    flags[i++] = flag;
                }
            }
        }

        var xs = new int[pointCount];
        var ys = new int[pointCount];
        var value = 0;
        for (var i = 0; i < pointCount; i++)
        {
            var flag = flags[i];
            if ((flag & 2) != 0)
            {
                var delta = _data[p++];
                value += (flag & 16) != 0 ? delta : -delta;
            }
            else if ((flag & 16) == 0)
            {
                value += S16(p);
                p += 2;
            }

            xs[i] = value;
        }

        value = 0;
        for (var i = 0; i < pointCount; i++)
        {
            var flag = flags[i];
            if ((flag & 4) != 0)
            {
                var delta = _data[p++];
                value += (flag & 32) != 0 ? delta : -delta;
            }
            else if ((flag & 32) == 0)
            {
                value += S16(p);
                p += 2;
            }

            ys[i] = value;
        }

        (double X, double Y) T(double x, double y) => ((a * x) + (c * y) + dx, (b * x) + (d * y) + dy);

        var first = 0;
        for (var contour = 0; contour < contourCount; contour++)
        {
            var last = endPoints[contour];
            var count = last - first + 1;
            if (count <= 0)
            {
                first = last + 1;
                continue;
            }

            bool OnCurve(int i) => (flags[first + (i % count)] & 1) != 0;
            (double X, double Y) Point(int i) => (xs[first + (i % count)], ys[first + (i % count)]);

            // Start at an on-curve point (or the midpoint of two off-curve points).
            var startIndex = -1;
            for (var i = 0; i < count; i++)
            {
                if (OnCurve(i))
                {
                    startIndex = i;
                    break;
                }
            }

            (double X, double Y) startPoint;
            (double X, double Y)? pendingControl = null;
            if (startIndex < 0)
            {
                // Every point is off-curve: start midway between the first two, with the second as the pending control.
                var p0 = Point(0);
                var p1 = Point(1);
                startPoint = ((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
                startIndex = 1;
                pendingControl = p1;
            }
            else
            {
                startPoint = Point(startIndex);
            }

            var s = T(startPoint.X, startPoint.Y);
            commands.Add(new PathCommand(PathVerb.MoveTo, s.X, s.Y));

            for (var step = 1; step <= count; step++)
            {
                var i = startIndex + step;
                var point = step == count ? startPoint : Point(i);
                var onCurve = step == count || OnCurve(i);

                if (onCurve)
                {
                    var target = T(point.X, point.Y);
                    if (pendingControl is { } control)
                    {
                        var cp = T(control.X, control.Y);
                        commands.Add(new PathCommand(PathVerb.QuadTo, target.X, target.Y, cp.X, cp.Y));
                        pendingControl = null;
                    }
                    else
                    {
                        commands.Add(new PathCommand(PathVerb.LineTo, target.X, target.Y));
                    }
                }
                else
                {
                    if (pendingControl is { } control)
                    {
                        var mid = ((control.X + point.X) / 2, (control.Y + point.Y) / 2);
                        var target = T(mid.Item1, mid.Item2);
                        var cp = T(control.X, control.Y);
                        commands.Add(new PathCommand(PathVerb.QuadTo, target.X, target.Y, cp.X, cp.Y));
                    }

                    pendingControl = point;
                }
            }

            if (pendingControl is { } trailing)
            {
                var target = T(startPoint.X, startPoint.Y);
                var cp = T(trailing.X, trailing.Y);
                commands.Add(new PathCommand(PathVerb.QuadTo, target.X, target.Y, cp.X, cp.Y));
            }

            commands.Add(new PathCommand(PathVerb.Close, 0, 0));
            first = last + 1;
        }
    }

    private void AppendCompositeGlyph(uint p, double a, double b, double c, double d, double dx, double dy, List<PathCommand> commands, int depth)
    {
        const int ArgsAreWords = 0x0001;
        const int ArgsAreXyValues = 0x0002;
        const int HaveScale = 0x0008;
        const int MoreComponents = 0x0020;
        const int HaveXyScale = 0x0040;
        const int HaveTwoByTwo = 0x0080;

        int flags;
        do
        {
            flags = U16(p);
            var component = U16(p + 2);
            p += 4;

            double arg1;
            double arg2;
            if ((flags & ArgsAreWords) != 0)
            {
                arg1 = S16(p);
                arg2 = S16(p + 2);
                p += 4;
            }
            else
            {
                arg1 = (sbyte)_data[p];
                arg2 = (sbyte)_data[p + 1];
                p += 2;
            }

            double ca = 1, cb = 0, cc = 0, cd = 1;
            if ((flags & HaveScale) != 0)
            {
                ca = cd = F2Dot14(p);
                p += 2;
            }
            else if ((flags & HaveXyScale) != 0)
            {
                ca = F2Dot14(p);
                cd = F2Dot14(p + 2);
                p += 4;
            }
            else if ((flags & HaveTwoByTwo) != 0)
            {
                ca = F2Dot14(p);
                cb = F2Dot14(p + 2);
                cc = F2Dot14(p + 4);
                cd = F2Dot14(p + 6);
                p += 8;
            }

            // Point-matching components (args not x/y offsets) are rare in these fonts; place them unshifted.
            var ox = (flags & ArgsAreXyValues) != 0 ? arg1 : 0;
            var oy = (flags & ArgsAreXyValues) != 0 ? arg2 : 0;

            // Compose: parent ∘ component.
            var na = (a * ca) + (c * cb);
            var nb = (b * ca) + (d * cb);
            var nc = (a * cc) + (c * cd);
            var nd = (b * cc) + (d * cd);
            var ndx = (a * ox) + (c * oy) + dx;
            var ndy = (b * ox) + (d * oy) + dy;
            AppendOutline(component, na, nb, nc, nd, ndx, ndy, commands, depth + 1);
        }
        while ((flags & MoreComponents) != 0);
    }

    private Dictionary<string, uint> ReadTableDirectory()
    {
        var numTables = U16(4);
        var tables = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (var i = 0; i < numTables; i++)
        {
            var record = 12u + (uint)(i * 16);
            var tag = System.Text.Encoding.ASCII.GetString(_data, (int)record, 4);
            tables[tag] = U32(record + 8);
        }

        return tables;
    }

    private Dictionary<int, ushort> ReadCmap(uint cmap)
    {
        var map = new Dictionary<int, ushort>();
        var numTables = U16(cmap + 2);
        uint? format4 = null;
        uint? format12 = null;
        for (var i = 0; i < numTables; i++)
        {
            var record = cmap + 4 + (uint)(i * 8);
            var platform = U16(record);
            var encoding = U16(record + 2);
            var subtable = cmap + U32(record + 4);
            var format = U16(subtable);
            if (format == 12 && (platform == 0 || (platform == 3 && encoding == 10)))
            {
                format12 ??= subtable;
            }
            else if (format == 4 && (platform == 0 || (platform == 3 && encoding == 1)))
            {
                format4 ??= subtable;
            }
        }

        if (format12 is { } table12)
        {
            var groups = U32(table12 + 12);
            for (var g = 0u; g < groups; g++)
            {
                var group = table12 + 16 + (g * 12);
                var startChar = U32(group);
                var endChar = U32(group + 4);
                var startGlyph = U32(group + 8);
                if (endChar - startChar > 0x10000)
                {
                    continue;
                }

                for (var ch = startChar; ch <= endChar; ch++)
                {
                    map[(int)ch] = (ushort)(startGlyph + (ch - startChar));
                }
            }

            return map;
        }

        if (format4 is not { } table4)
        {
            throw new InvalidDataException($"Font '{Name}' has no Unicode cmap.");
        }

        var segCount = U16(table4 + 6) / 2;
        var endCodes = table4 + 14;
        var startCodes = endCodes + (uint)(segCount * 2) + 2;
        var idDeltas = startCodes + (uint)(segCount * 2);
        var idRangeOffsets = idDeltas + (uint)(segCount * 2);
        for (var s = 0; s < segCount; s++)
        {
            int endCode = U16(endCodes + (uint)(s * 2));
            int startCode = U16(startCodes + (uint)(s * 2));
            var idDelta = S16(idDeltas + (uint)(s * 2));
            var rangeOffsetPosition = idRangeOffsets + (uint)(s * 2);
            var idRangeOffset = U16(rangeOffsetPosition);
            for (var ch = startCode; ch <= endCode && ch != 0xFFFF; ch++)
            {
                ushort glyph;
                if (idRangeOffset == 0)
                {
                    glyph = (ushort)(ch + idDelta);
                }
                else
                {
                    var glyphAddress = rangeOffsetPosition + idRangeOffset + (uint)((ch - startCode) * 2);
                    glyph = U16(glyphAddress);
                    if (glyph != 0)
                    {
                        glyph = (ushort)(glyph + idDelta);
                    }
                }

                map[ch] = glyph;
            }
        }

        return map;
    }

    private ushort U16(uint offset) => offset + 2 <= _data.Length
        ? BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan((int)offset, 2))
        : throw new InvalidDataException($"Font '{Name}' is truncated.");

    private short S16(uint offset) => (short)U16(offset);

    private uint U32(uint offset) => offset + 4 <= _data.Length
        ? BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan((int)offset, 4))
        : throw new InvalidDataException($"Font '{Name}' is truncated.");

    private double F2Dot14(uint offset) => S16(offset) / 16384.0;
}
