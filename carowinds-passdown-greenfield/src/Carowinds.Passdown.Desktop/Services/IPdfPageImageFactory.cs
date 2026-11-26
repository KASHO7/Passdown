using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Carowinds.Passdown.Application.PdfOutput;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// Phase 33: turns a rendered PDF page into something an Avalonia <c>Image</c> can show. A seam so the
/// preview view model is testable without a rendering platform (tests supply a fake).
/// </summary>
public interface IPdfPageImageFactory
{
    /// <summary>The returned image is owned by the caller, which disposes it when it is replaced.</summary>
    IImage Create(PdfRasterImage page);

    /// <summary>
    /// Phase 34B-2: renders one page of <paramref name="document"/> into a new image (owned by the caller, as for
    /// <see cref="Create"/>). The default renders to an intermediate raster and copies it.
    /// </summary>
    IImage Render(IPdfRasterDocument document, int pageIndex, double pixelsPerPoint) =>
        Create(document.RenderPage(pageIndex, pixelsPerPoint));
}

/// <summary>Copies the BGRA pixels into a <see cref="WriteableBitmap"/> (safe to call from a background thread).</summary>
public sealed class AvaloniaPdfPageImageFactory : IPdfPageImageFactory
{
    public IImage Create(PdfRasterImage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var bitmap = new WriteableBitmap(new PixelSize(page.PixelWidth, page.PixelHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var rowBytes = page.PixelWidth * 4;
        for (var row = 0; row < page.PixelHeight; row++)
        {
            System.Runtime.InteropServices.Marshal.Copy(page.Bgra, row * page.Stride, buffer.Address + (row * buffer.RowBytes), rowBytes);
        }

        return bitmap;
    }

    /// <summary>
    /// Phase 34B-2: when the document supports it, PDFium renders straight into the new bitmap's locked pixel
    /// buffer — no full-page managed array (≈ 4 MB at 150 % scaling, a large-object-heap allocation) and no copy.
    /// The buffer is locked for exactly the duration of the render and the bitmap is disposed if rendering fails
    /// or is cancelled, so it can never escape half-drawn. Pixels are identical to <see cref="Create"/>'s.
    /// </summary>
    public IImage Render(IPdfRasterDocument document, int pageIndex, double pixelsPerPoint)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document is not IPdfDirectRasterDocument direct)
        {
            return Create(document.RenderPage(pageIndex, pixelsPerPoint));
        }

        var size = direct.GetRenderPixelSize(pageIndex, pixelsPerPoint);
        var bitmap = new WriteableBitmap(new PixelSize(size.Width, size.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        try
        {
            using (var buffer = bitmap.Lock())
            {
                direct.RenderPageInto(pageIndex, pixelsPerPoint, buffer.Address, buffer.RowBytes);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
