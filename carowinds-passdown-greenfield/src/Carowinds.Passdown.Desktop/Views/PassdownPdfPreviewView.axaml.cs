using Avalonia;
using Avalonia.Controls;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Views;

/// <summary>
/// Phase 33: view glue only — reports the page viewport size and the window's render scaling to the view
/// model (which owns fit-zoom and render-resolution decisions). No generation, rendering or persistence here.
/// </summary>
public partial class PassdownPdfPreviewView : UserControl
{
    /// <summary>Room reserved for a vertical scroll bar, so fit-to-width never toggles the bar on and off.</summary>
    private const double ScrollBarAllowance = 16;

    public PassdownPdfPreviewView()
    {
        InitializeComponent();
        PageScrollViewer.SizeChanged += (_, _) => ReportViewport();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ReportViewport();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ReportViewport();
    }

    private void ReportViewport()
    {
        if (DataContext is not PassdownPdfPreviewViewModel viewModel)
        {
            return;
        }

        var bounds = PageScrollViewer.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        viewModel.UpdateViewport(Math.Max(1, bounds.Width - ScrollBarAllowance), bounds.Height, scaling);
    }
}
