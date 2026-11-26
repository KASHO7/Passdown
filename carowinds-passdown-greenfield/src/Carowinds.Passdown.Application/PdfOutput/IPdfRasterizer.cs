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

/// <summary>
/// Phase 34B-2: an opened document that can render straight into pixel memory the caller owns (for example a
/// locked on-screen bitmap), avoiding a full-page managed array and a copy per render. Optional — callers fall
/// back to <see cref="IPdfRasterDocument.RenderPage"/> when a document does not implement it.
/// </summary>
public interface IPdfDirectRasterDocument : IPdfRasterDocument
{
    /// <summary>The exact pixel size <see cref="IPdfRasterDocument.RenderPage"/> and <see cref="RenderPageInto"/> produce at <paramref name="pixelsPerPoint"/>.</summary>
    PdfPixelSize GetRenderPixelSize(int pageIndex, double pixelsPerPoint);

    /// <summary>
    /// Renders the whole page onto a white background into <paramref name="destination"/>: 32-bit BGRA rows of
    /// <paramref name="destinationStride"/> bytes, at least <see cref="GetRenderPixelSize"/> in size. The memory
    /// must stay valid and unmoved for the duration of the call; nothing keeps a reference to it afterwards.
    /// Produces exactly the pixels <see cref="IPdfRasterDocument.RenderPage"/> would.
    /// </summary>
    void RenderPageInto(int pageIndex, double pixelsPerPoint, IntPtr destination, int destinationStride, PdfRenderPurpose purpose = PdfRenderPurpose.Display, CancellationToken cancellationToken = default);
}

/// <summary>A rendered page size in pixels.</summary>
public readonly record struct PdfPixelSize(int Width, int Height);

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
