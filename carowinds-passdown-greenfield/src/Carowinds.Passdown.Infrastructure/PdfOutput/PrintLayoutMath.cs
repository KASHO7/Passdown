namespace Carowinds.Passdown.Infrastructure.PdfOutput;

/// <summary>A rectangle in printer device pixels, relative to the printable-area origin GDI draws from.</summary>
public readonly record struct DeviceRect(int X, int Y, int Width, int Height);

/// <summary>
/// Phase 33: the platform-neutral arithmetic behind Windows printing, kept pure so it is tested on every
/// host (the GDI calls themselves can only be exercised on Windows).
/// </summary>
public static class PrintLayoutMath
{
    /// <summary>Above this resolution the page is rasterized at this DPI and stretched by the driver — sharp for text, bounded in memory and spool size.</summary>
    public const int MaxRenderDpi = 600;

    /// <summary>
    /// Where a PDF page lands on the physical sheet: actual size (100%, so the PDF's own margins and page
    /// breaks are exactly preserved) when it fits the sheet, otherwise uniformly shrunk to fit; centered on
    /// the physical sheet either way. Coordinates are relative to the printable origin (they may be
    /// slightly negative — the printer clips its hardware margins, which the PDF's own margins exceed).
    /// </summary>
    public static DeviceRect PlacePage(
        double pageWidthPoints, double pageHeightPoints,
        int dpiX, int dpiY,
        int physicalWidth, int physicalHeight,
        int physicalOffsetX, int physicalOffsetY)
    {
        if (pageWidthPoints <= 0 || pageHeightPoints <= 0 || dpiX <= 0 || dpiY <= 0 || physicalWidth <= 0 || physicalHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageWidthPoints), "Page and device dimensions must be positive.");
        }

        var pageWidthInches = pageWidthPoints / 72.0;
        var pageHeightInches = pageHeightPoints / 72.0;
        var sheetWidthInches = physicalWidth / (double)dpiX;
        var sheetHeightInches = physicalHeight / (double)dpiY;

        // A tolerance of half a device pixel keeps an exact Letter-on-Letter match at 100%.
        var fitScale = Math.Min((sheetWidthInches + (0.5 / dpiX)) / pageWidthInches, (sheetHeightInches + (0.5 / dpiY)) / pageHeightInches);
        var scale = Math.Min(1.0, fitScale);

        var width = (int)Math.Round(pageWidthInches * scale * dpiX);
        var height = (int)Math.Round(pageHeightInches * scale * dpiY);
        var x = ((physicalWidth - width) / 2) - physicalOffsetX;
        var y = ((physicalHeight - height) / 2) - physicalOffsetY;
        return new DeviceRect(x, y, width, height);
    }

    /// <summary>Fraction (≤ 1) of the device resolution the page is rasterized at.</summary>
    public static double RenderScaleFor(int dpiX, int dpiY) =>
        Math.Min(1.0, MaxRenderDpi / (double)Math.Max(1, Math.Max(dpiX, dpiY)));

    /// <summary>Rows per band so one band stays near <paramref name="maxBandBytes"/> (32-bit pixels).</summary>
    public static int BandHeightFor(int renderWidth, int maxBandBytes = 16 * 1024 * 1024) =>
        (int)Math.Max(1L, maxBandBytes / Math.Max(1L, renderWidth * 4L));

    /// <summary>
    /// The zero-based pages to print, in the order chosen: all pages when <paramref name="ranges"/> is
    /// null or empty, otherwise each 1-based inclusive range clamped to the document (out-of-range and
    /// reversed ranges are ignored, never guessed).
    /// </summary>
    public static IReadOnlyList<int> ExpandPageRanges(IReadOnlyList<(int From, int To)>? ranges, int pageCount)
    {
        if (pageCount <= 0)
        {
            return [];
        }

        if (ranges is null || ranges.Count == 0)
        {
            return Enumerable.Range(0, pageCount).ToList();
        }

        var pages = new List<int>();
        foreach (var (from, to) in ranges)
        {
            if (from < 1 || to < from || from > pageCount)
            {
                continue;
            }

            for (var page = from; page <= Math.Min(to, pageCount); page++)
            {
                pages.Add(page - 1);
            }
        }

        return pages;
    }

    /// <summary>Converts one 32-bit BGRA band to the 24-bit bottom-up-free (top-down) DIB rows GDI expects, each row padded to 4 bytes.</summary>
    public static byte[] ToBgr24(byte[] bgra, int width, int height, int sourceStride, out int targetStride)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        targetStride = ((width * 3) + 3) & ~3;
        var target = new byte[targetStride * height];
        for (var row = 0; row < height; row++)
        {
            var source = row * sourceStride;
            var destination = row * targetStride;
            for (var column = 0; column < width; column++)
            {
                target[destination] = bgra[source];
                target[destination + 1] = bgra[source + 1];
                target[destination + 2] = bgra[source + 2];
                source += 4;
                destination += 3;
            }
        }

        return target;
    }
}
