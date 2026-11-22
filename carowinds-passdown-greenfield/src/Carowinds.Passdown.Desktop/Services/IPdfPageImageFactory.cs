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
}
