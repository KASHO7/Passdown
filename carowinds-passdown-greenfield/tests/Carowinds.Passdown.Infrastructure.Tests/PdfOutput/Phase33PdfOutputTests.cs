using System.Text.Json;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Infrastructure.PdfOutput;
using Carowinds.Passdown.Infrastructure.Preferences;

namespace Carowinds.Passdown.Infrastructure.Tests.PdfOutput;

/// <summary>
/// Phase 33: the platform-neutral printing arithmetic (placement at actual size, page ranges, memory-bounded
/// bands, DIB conversion). The GDI/print-dialog calls themselves only run on Windows and are NOT exercised
/// here — physical printing is validated by the Windows checklist, never claimed by these tests.
/// </summary>
public class PrintLayoutMathTests
{
    // Letter on Letter at 600 DPI: exact actual size (100%), origin on the sheet edge.
    [Fact]
    public void LetterOnLetter_PrintsAtActualSize()
    {
        var rect = PrintLayoutMath.PlacePage(612, 792, 600, 600, 5100, 6600, 75, 75);

        Assert.Equal(new DeviceRect(-75, -75, 5100, 6600), rect);
    }

    [Fact]
    public void LetterOnLetter_At300Dpi_IsAlsoActualSize()
    {
        var rect = PrintLayoutMath.PlacePage(612, 792, 300, 300, 2550, 3300, 0, 0);

        Assert.Equal(new DeviceRect(0, 0, 2550, 3300), rect);
    }

    // Letter onto A4 (narrower): uniformly shrunk to fit and centered, aspect ratio preserved.
    [Fact]
    public void LetterOnA4_ShrinksToFit_PreservingAspect()
    {
        // A4 at 600 DPI: 4961 × 7016 px
        var rect = PrintLayoutMath.PlacePage(612, 792, 600, 600, 4961, 7016, 0, 0);

        Assert.Equal(4961, rect.Width);
        Assert.Equal(612.0 / 792.0, rect.Width / (double)rect.Height, 3);
        Assert.Equal(0, rect.X);
        Assert.True(rect.Y > 0); // centered vertically
    }

    // Portrait page on landscape paper: shrunk to the sheet height, centered horizontally.
    [Fact]
    public void PortraitPageOnLandscapePaper_FitsTheHeight()
    {
        var rect = PrintLayoutMath.PlacePage(612, 792, 600, 600, 6600, 5100, 0, 0);

        Assert.Equal(5100, rect.Height);
        Assert.True(rect.Width < 6600);
        Assert.Equal((6600 - rect.Width) / 2, rect.X);
    }

    [Fact]
    public void LargerPaper_NeverEnlargesThePage()
    {
        // Legal paper (8.5 × 14) at 300 DPI: the Letter page stays 100%, centered.
        var rect = PrintLayoutMath.PlacePage(612, 792, 300, 300, 2550, 4200, 0, 0);

        Assert.Equal(2550, rect.Width);
        Assert.Equal(3300, rect.Height);
        Assert.Equal(450, rect.Y);
    }

    [Fact]
    public void InvalidDimensions_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PrintLayoutMath.PlacePage(0, 792, 600, 600, 5100, 6600, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrintLayoutMath.PlacePage(612, 792, 0, 600, 5100, 6600, 0, 0));
    }

    [Theory]
    [InlineData(300, 300, 1.0)]
    [InlineData(600, 600, 1.0)]
    [InlineData(1200, 1200, 0.5)]
    [InlineData(2400, 1200, 0.25)]
    public void RenderScale_CapsAt600Dpi(int dpiX, int dpiY, double expected)
    {
        Assert.Equal(expected, PrintLayoutMath.RenderScaleFor(dpiX, dpiY), 6);
    }

    [Fact]
    public void BandHeight_KeepsEachBandNear16MB()
    {
        var rows = PrintLayoutMath.BandHeightFor(5100);
        Assert.InRange(rows * 5100 * 4, 1, 16 * 1024 * 1024);
        Assert.True(rows > 100);
        Assert.Equal(1, PrintLayoutMath.BandHeightFor(int.MaxValue / 2));
    }

    [Fact]
    public void PageRanges_DefaultToAllPages()
    {
        Assert.Equal([0, 1, 2], PrintLayoutMath.ExpandPageRanges(null, 3));
        Assert.Equal([0, 1, 2], PrintLayoutMath.ExpandPageRanges([], 3));
        Assert.Empty(PrintLayoutMath.ExpandPageRanges(null, 0));
    }

    [Fact]
    public void PageRanges_AreClamped_AndInvalidRangesIgnored()
    {
        Assert.Equal([1, 2], PrintLayoutMath.ExpandPageRanges([(2, 9)], 3));
        Assert.Equal([0, 2], PrintLayoutMath.ExpandPageRanges([(1, 1), (3, 3)], 3));
        Assert.Empty(PrintLayoutMath.ExpandPageRanges([(4, 6)], 3));
        Assert.Empty(PrintLayoutMath.ExpandPageRanges([(3, 2), (0, 1)], 3));
    }

    [Fact]
    public void Bgr24Conversion_DropsAlpha_AndPadsRowsToFourBytes()
    {
        // 3 × 2 pixels, BGRA
        byte[] bgra =
        [
            1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255,
            10, 11, 12, 255, 13, 14, 15, 255, 16, 17, 18, 255,
        ];

        var bgr = PrintLayoutMath.ToBgr24(bgra, 3, 2, 12, out var stride);

        Assert.Equal(12, stride); // 9 bytes rounded up to 12
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 0, 0, 10, 11, 12, 13, 14, 15, 16, 17, 18, 0, 0, 0 }, bgr);
    }
}

/// <summary>Phase 33: the preview's local layout preferences file — defaults when missing or bad, round-trip, never synced.</summary>
public class FilePdfPreviewLayoutPreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-pdf-preview-layout-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor() => Path.Combine(_directory, "pdf-preview-layout.json");

    private string WriteRaw(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(), content);
        return PathFor();
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsBuiltIn_AndCreatesNothing()
    {
        Assert.Equal(PdfPreviewLayoutPreferences.BuiltIn, await new FilePdfPreviewLayoutPreferenceStore(PathFor()).LoadAsync());
        Assert.False(Directory.Exists(_directory));
        Assert.True(PdfPreviewLayoutPreferences.BuiltIn.IsPreviewVisible);
    }

    [Fact]
    public async Task Save_ThenLoad_RoundTrips_AcrossNewInstances()
    {
        var preferences = new PdfPreviewLayoutPreferences(false, 612.5, PdfPreviewFitMode.Custom, 1.25);
        await new FilePdfPreviewLayoutPreferenceStore(PathFor()).SaveAsync(preferences);

        Assert.Equal(preferences, await new FilePdfPreviewLayoutPreferenceStore(PathFor()).LoadAsync());
        Assert.Equal(["pdf-preview-layout.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
        using var document = JsonDocument.Parse(File.ReadAllText(PathFor()));
        Assert.Equal(["IsPreviewVisible", "PreviewPaneWidth", "FitMode", "Zoom"], document.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task Load_UnreadableFile_ReturnsBuiltIn(string content)
    {
        Assert.Equal(PdfPreviewLayoutPreferences.BuiltIn, await new FilePdfPreviewLayoutPreferenceStore(WriteRaw(content)).LoadAsync());
    }

    [Fact]
    public async Task Load_OutOfRangeValues_FallBackPerValue()
    {
        WriteRaw("""{ "IsPreviewVisible": false, "PreviewPaneWidth": -40, "FitMode": "Sideways", "Zoom": 99 }""");

        var loaded = await new FilePdfPreviewLayoutPreferenceStore(PathFor()).LoadAsync();

        Assert.False(loaded.IsPreviewVisible); // the valid value is kept
        Assert.Equal(PdfPreviewLayoutPreferences.DefaultPreviewPaneWidth, loaded.PreviewPaneWidth);
        Assert.Equal(PdfPreviewFitMode.FitWidth, loaded.FitMode);
        Assert.Equal(1.0, loaded.Zoom);
    }

    // 28. A device preference: local app data beside the other device preferences, outside the sync folder,
    // no database involvement.
    [Fact]
    public void DefaultPath_IsLocalOnly_BesideTheOtherDevicePreferences()
    {
        Assert.Equal(Path.Combine(AppPaths.LocalApplicationDataDirectory, "pdf-preview-layout.json"), FilePdfPreviewLayoutPreferenceStore.DefaultPath);
        Assert.Equal(Path.GetDirectoryName(FileAppearancePreferenceStore.DefaultPath), Path.GetDirectoryName(FilePdfPreviewLayoutPreferenceStore.DefaultPath));
        Assert.DoesNotContain("Sync", FilePdfPreviewLayoutPreferenceStore.DefaultPath, StringComparison.Ordinal);
    }
}
