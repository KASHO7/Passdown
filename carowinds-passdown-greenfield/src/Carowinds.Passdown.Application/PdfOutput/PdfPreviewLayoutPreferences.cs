namespace Carowinds.Passdown.Application.PdfOutput;

/// <summary>How the preview chooses its zoom.</summary>
public enum PdfPreviewFitMode
{
    /// <summary>The page width fills the preview pane.</summary>
    FitWidth,

    /// <summary>The whole page is visible.</summary>
    FitPage,

    /// <summary>A zoom the user picked with Zoom In / Zoom Out.</summary>
    Custom,
}

/// <summary>
/// Phase 33: the Associate Passdown PDF preview's UI-only layout preferences. A device preference saved
/// on this computer only (one small JSON file, no database migration) and never part of Personal Sync.
/// <see cref="PreviewPaneWidth"/> is a preferred width in device-independent pixels; it is clamped to the
/// window actually available every time it is applied, so a value saved on a large monitor never clips
/// the editor on a smaller one.
/// </summary>
public sealed record PdfPreviewLayoutPreferences(bool IsPreviewVisible, double PreviewPaneWidth, PdfPreviewFitMode FitMode, double Zoom)
{
    public const double DefaultPreviewPaneWidth = 480;

    /// <summary>Built-in default: preview shown, fit to width.</summary>
    public static PdfPreviewLayoutPreferences BuiltIn { get; } = new(true, DefaultPreviewPaneWidth, PdfPreviewFitMode.FitWidth, 1.0);
}

/// <summary>Local persistence for <see cref="PdfPreviewLayoutPreferences"/>.</summary>
public interface IPdfPreviewLayoutPreferenceStore
{
    /// <summary>Never throws: a missing, unreadable or out-of-range value falls back to the built-in default for that value.</summary>
    Task<PdfPreviewLayoutPreferences> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes atomically; may throw on an I/O failure (the caller logs it — a layout preference is never worth an error dialog).</summary>
    Task SaveAsync(PdfPreviewLayoutPreferences preferences, CancellationToken cancellationToken = default);
}

/// <summary>In-memory store (tests, and the fallback when no store is composed). Never touches disk.</summary>
public sealed class InMemoryPdfPreviewLayoutPreferenceStore(PdfPreviewLayoutPreferences? initial = null) : IPdfPreviewLayoutPreferenceStore
{
    public PdfPreviewLayoutPreferences Current { get; private set; } = initial ?? PdfPreviewLayoutPreferences.BuiltIn;

    public int SaveCount { get; private set; }

    public Task<PdfPreviewLayoutPreferences> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task SaveAsync(PdfPreviewLayoutPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Current = preferences;
        SaveCount++;
        return Task.CompletedTask;
    }
}
