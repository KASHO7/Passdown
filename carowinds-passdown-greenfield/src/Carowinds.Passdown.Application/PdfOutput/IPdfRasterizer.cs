namespace Carowinds.Passdown.Application.PdfOutput;

/// <summary>
/// Phase 33: turns PDF bytes the application itself generated into page images. Used by the Associate
/// Passdown live preview (and, on Windows, by printing) so what is shown and printed is the actual
/// generated PDF — never a XAML re-creation of it. Implementations work fully offline.
/// </summary>
public interface IPdfRasterizer
{
    /// <summary>Opens <paramref name="pdfBytes"/> for rendering. Throws <see cref="PdfRasterizationException"/> when the bytes cannot be opened.</summary>
    IPdfRasterDocument Open(byte[] pdfBytes);
}

/// <summary>One opened PDF. Thread-safe: every call may come from any thread; calls after <see cref="IDisposable.Dispose"/> throw <see cref="ObjectDisposedException"/>.</summary>
public interface IPdfRasterDocument : IDisposable
{
    int PageCount { get; }

    /// <summary>Page size in PDF points (1/72 inch).</summary>
    PdfPageSize GetPageSize(int pageIndex);

    /// <summary>Renders the whole page onto a white background at <paramref name="pixelsPerPoint"/>.</summary>
    PdfRasterImage RenderPage(int pageIndex, double pixelsPerPoint, PdfRenderPurpose purpose = PdfRenderPurpose.Display, CancellationToken cancellationToken = default);
}

/// <summary>Page size in PDF points.</summary>
public readonly record struct PdfPageSize(double WidthPoints, double HeightPoints);

/// <summary>Whether the renderer should optimize for on-screen display or for a printer.</summary>
public enum PdfRenderPurpose
{
    Display,
    Print,
}

/// <summary>A rendered page: 32-bit BGRA, premultiplied (fully opaque), top-down rows.</summary>
public sealed record PdfRasterImage(int PixelWidth, int PixelHeight, int Stride, byte[] Bgra);

/// <summary>The PDF could not be opened or rendered.</summary>
public sealed class PdfRasterizationException : Exception
{
    public PdfRasterizationException()
    {
    }

    public PdfRasterizationException(string message)
        : base(message)
    {
    }

    public PdfRasterizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
