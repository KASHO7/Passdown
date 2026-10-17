namespace Carowinds.Passdown.Documents.DailyFeatureMath;

/// <summary>
/// Phase 21D: the fonts the Daily Feature math renderer draws with, all bundled inside this assembly
/// (never system or downloaded fonts): STIX Two Math (SIL OFL 1.1) for formulas and math symbols, and
/// the same Lato faces the PDFs already use (SIL OFL 1.1) for the surrounding prose. Loaded once,
/// lazily; if anything fails to load, <see cref="Shared"/> is null and every caller falls back to plain
/// text — math support is never a dependency for ordinary passdown operations.
/// </summary>
public sealed class MathFontSet
{
    /// <summary>Logical name of the embedded STIX Two Math font (see Carowinds.Passdown.Documents.csproj).</summary>
    public const string MathFontResource = "DailyFeatureMath.STIXTwoMath-Regular.ttf";

    /// <summary>The family name QuestPDF registers the STIX font under (used as a plain-text glyph fallback).</summary>
    public const string MathFontFamily = "STIX Two Math";

    private static readonly Lazy<MathFontSet?> SharedInstance = new(LoadOrNull, LazyThreadSafetyMode.ExecutionAndPublication);

    public MathFontSet(TrueTypeFont math, TrueTypeFont proseItalic, TrueTypeFont proseRegular)
    {
        Math = math;
        ProseItalic = proseItalic;
        ProseRegular = proseRegular;
    }

    public TrueTypeFont Math { get; }

    public TrueTypeFont ProseItalic { get; }

    public TrueTypeFont ProseRegular { get; }

    /// <summary>The process-wide font set, or null when the bundled fonts could not be read.</summary>
    public static MathFontSet? Shared => SharedInstance.Value;

    /// <summary>The raw bytes of an embedded font resource of this assembly (also used to register STIX with QuestPDF).</summary>
    public static byte[] ReadEmbeddedFont(string logicalName)
    {
        var assembly = typeof(MathFontSet).Assembly;
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded font resource '{logicalName}' not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static MathFontSet? LoadOrNull()
    {
        try
        {
            return new MathFontSet(
                new TrueTypeFont(ReadEmbeddedFont(MathFontResource), MathFontFamily),
                new TrueTypeFont(ReadEmbeddedFont("LatoFont.Lato-Italic.ttf"), "Lato Italic"),
                new TrueTypeFont(ReadEmbeddedFont("LatoFont.Lato-Regular.ttf"), "Lato"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or IndexOutOfRangeException or ArgumentException)
        {
            MathRenderDiagnostics.Report(MathRenderStatus.RendererUnavailable);
            return null;
        }
    }
}
