using Carowinds.Passdown.Application.PdfOutput;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>Which panes the Associate Passdown body shows.</summary>
public enum PdfPreviewPaneMode
{
    /// <summary>Editor only (preview hidden, unavailable, or a narrow window with the editor in front).</summary>
    EditorOnly,

    /// <summary>Editor and preview side by side with a draggable divider.</summary>
    Split,

    /// <summary>Narrow window: the preview temporarily replaces the editor (Hide Preview returns to it).</summary>
    PreviewOnly,
}

/// <summary>The resolved layout for one available width.</summary>
public readonly record struct PdfPreviewLayoutState(PdfPreviewPaneMode Mode, double PreviewWidth, double MaxPreviewWidth);

/// <summary>
/// Phase 33: pure sizing rules for the side-by-side editor/preview layout. The editor keeps at least
/// <see cref="MinEditorWidth"/> (section rail included — its existing compact layout never squeezes below
/// that) and the preview at least <see cref="MinPreviewWidth"/> (its toolbar wraps, never overflows). When
/// both cannot fit, the window is "narrow" and shows one pane at a time instead of squeezing both.
/// </summary>
public static class PdfPreviewLayout
{
    /// <summary>Section rail (140) plus the narrowest editor body that does not clip its rows.</summary>
    public const double MinEditorWidth = 660;

    public const double MinPreviewWidth = 340;

    public const double SplitterWidth = 6;

    public static double NarrowThreshold => MinEditorWidth + SplitterWidth + MinPreviewWidth;

    public static bool IsNarrow(double availableWidth) => availableWidth < NarrowThreshold;

    /// <summary>The largest preview the editor's minimum allows at <paramref name="availableWidth"/>.</summary>
    public static double MaxPreviewWidth(double availableWidth) =>
        Math.Max(MinPreviewWidth, availableWidth - MinEditorWidth - SplitterWidth);

    /// <summary>Clamps a remembered preview width (possibly from a larger monitor) to what fits now.</summary>
    public static double ClampPreviewWidth(double preferredWidth, double availableWidth)
    {
        var preferred = double.IsFinite(preferredWidth) && preferredWidth > 0 ? preferredWidth : PdfPreviewLayoutPreferences.DefaultPreviewPaneWidth;
        return Math.Clamp(preferred, MinPreviewWidth, MaxPreviewWidth(availableWidth));
    }

    public static PdfPreviewLayoutState Resolve(double availableWidth, double preferredWidth, bool previewRequested, bool previewAvailable, bool previewInFrontWhenNarrow)
    {
        var max = MaxPreviewWidth(availableWidth);
        if (!previewAvailable)
        {
            return new PdfPreviewLayoutState(PdfPreviewPaneMode.EditorOnly, 0, max);
        }

        if (IsNarrow(availableWidth))
        {
            return new PdfPreviewLayoutState(previewInFrontWhenNarrow ? PdfPreviewPaneMode.PreviewOnly : PdfPreviewPaneMode.EditorOnly, 0, max);
        }

        return previewRequested
            ? new PdfPreviewLayoutState(PdfPreviewPaneMode.Split, ClampPreviewWidth(preferredWidth, availableWidth), max)
            : new PdfPreviewLayoutState(PdfPreviewPaneMode.EditorOnly, 0, max);
    }
}

/// <summary>Phase 33: zoom bounds, steps and fit calculations for the PDF preview.</summary>
public static class PdfPreviewZoom
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 4.0;

    /// <summary>DIPs per PDF point (96 / 72).</summary>
    public const double DipsPerPoint = 96.0 / 72.0;

    /// <summary>Breathing room around the page inside the preview viewport, per side.</summary>
    public const double PagePadding = 12;

    private static readonly double[] Steps = [0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0];

    public static double Clamp(double zoom) => double.IsFinite(zoom) ? Math.Clamp(zoom, MinZoom, MaxZoom) : 1.0;

    public static double StepIn(double zoom)
    {
        foreach (var step in Steps)
        {
            if (step > zoom + 0.005)
            {
                return step;
            }
        }

        return MaxZoom;
    }

    public static double StepOut(double zoom)
    {
        for (var i = Steps.Length - 1; i >= 0; i--)
        {
            if (Steps[i] < zoom - 0.005)
            {
                return Steps[i];
            }
        }

        return MinZoom;
    }

    public static double FitWidth(PdfPageSize page, double viewportWidth)
    {
        if (page.WidthPoints <= 0 || viewportWidth <= 0)
        {
            return 1.0;
        }

        return Clamp((viewportWidth - (2 * PagePadding)) / (page.WidthPoints * DipsPerPoint));
    }

    public static double FitPage(PdfPageSize page, double viewportWidth, double viewportHeight)
    {
        if (page.WidthPoints <= 0 || page.HeightPoints <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return 1.0;
        }

        var byWidth = (viewportWidth - (2 * PagePadding)) / (page.WidthPoints * DipsPerPoint);
        var byHeight = (viewportHeight - (2 * PagePadding)) / (page.HeightPoints * DipsPerPoint);
        return Clamp(Math.Min(byWidth, byHeight));
    }
}
