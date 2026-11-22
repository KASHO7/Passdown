using System.Runtime.InteropServices;
using Carowinds.Passdown.Application.PdfOutput;

namespace Carowinds.Passdown.Infrastructure.PdfOutput;

/// <summary>
/// Phase 33: <see cref="IPdfRasterizer"/> backed by PDFium (BSD-3-Clause). Renders the application's own
/// generated PDF bytes — nothing is uploaded, written to disk, or re-created in XAML. All PDFium calls are
/// serialized process-wide (<see cref="PdfiumNative.Sync"/>), so documents may be used from any thread.
/// </summary>
public sealed class PdfiumPdfRasterizer : IPdfRasterizer
{
    /// <summary>Upper bound on either rendered dimension, so a runaway zoom can never allocate an enormous bitmap.</summary>
    public const int MaxPixelDimension = 8192;

    public IPdfRasterDocument Open(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        if (pdfBytes.Length == 0)
        {
            throw new PdfRasterizationException("The PDF is empty.");
        }

        return PdfiumRasterDocument.Open(pdfBytes);
    }

    /// <summary>Renders a band of a page (rows <paramref name="bandTop"/>..+<paramref name="bandHeight"/> of a page scaled to <paramref name="fullWidth"/>×<paramref name="fullHeight"/>) — used by Windows printing to stay memory-bounded at printer resolution.</summary>
    internal static PdfRasterImage RenderBand(IntPtr document, int pageIndex, int fullWidth, int fullHeight, int bandTop, int bandHeight, int flags)
    {
        lock (PdfiumNative.Sync)
        {
            var page = PdfiumNative.FPDF_LoadPage(document, pageIndex);
            if (page == IntPtr.Zero)
            {
                throw new PdfRasterizationException($"Page {pageIndex + 1} could not be loaded.");
            }

            try
            {
                return RenderLocked(page, fullWidth, bandHeight, 0, -bandTop, fullWidth, fullHeight, flags);
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }

    /// <summary>Caller holds <see cref="PdfiumNative.Sync"/>.</summary>
    internal static PdfRasterImage RenderLocked(IntPtr page, int bitmapWidth, int bitmapHeight, int startX, int startY, int sizeX, int sizeY, int flags)
    {
        var bitmap = PdfiumNative.FPDFBitmap_Create(bitmapWidth, bitmapHeight, 1);
        if (bitmap == IntPtr.Zero)
        {
            throw new PdfRasterizationException("Not enough memory to render the page.");
        }

        try
        {
            PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, bitmapWidth, bitmapHeight, 0xFFFFFFFF);
            PdfiumNative.FPDF_RenderPageBitmap(bitmap, page, startX, startY, sizeX, sizeY, 0, flags);

            var stride = PdfiumNative.FPDFBitmap_GetStride(bitmap);
            var buffer = PdfiumNative.FPDFBitmap_GetBuffer(bitmap);
            var bytes = new byte[stride * bitmapHeight];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return new PdfRasterImage(bitmapWidth, bitmapHeight, stride, bytes);
        }
        finally
        {
            PdfiumNative.FPDFBitmap_Destroy(bitmap);
        }
    }

    internal sealed class PdfiumRasterDocument : IPdfRasterDocument
    {
        private IntPtr _document;
        private IntPtr _buffer;
        private readonly PdfPageSize[] _pageSizes;

        private PdfiumRasterDocument(IntPtr document, IntPtr buffer, PdfPageSize[] pageSizes)
        {
            _document = document;
            _buffer = buffer;
            _pageSizes = pageSizes;
        }

        public int PageCount => _pageSizes.Length;

        /// <summary>The native document handle, for band rendering; valid until disposed.</summary>
        internal IntPtr Handle
        {
            get
            {
                ObjectDisposedException.ThrowIf(_document == IntPtr.Zero, this);
                return _document;
            }
        }

        public static PdfiumRasterDocument Open(byte[] pdfBytes)
        {
            // PDFium reads the buffer lazily for the document's whole lifetime, so it lives in unmanaged
            // memory (never moved by the GC) until Dispose.
            var buffer = Marshal.AllocHGlobal(pdfBytes.Length);
            Marshal.Copy(pdfBytes, 0, buffer, pdfBytes.Length);

            lock (PdfiumNative.Sync)
            {
                PdfiumNative.EnsureInitialized();
                var document = PdfiumNative.FPDF_LoadMemDocument64(buffer, (nuint)pdfBytes.Length, IntPtr.Zero);
                if (document == IntPtr.Zero)
                {
                    var error = PdfiumNative.FPDF_GetLastError();
                    Marshal.FreeHGlobal(buffer);
                    throw new PdfRasterizationException(error switch
                    {
                        PdfiumNative.FPDF_ERR_FORMAT => "The PDF is damaged or not a PDF.",
                        PdfiumNative.FPDF_ERR_PASSWORD => "The PDF is password protected.",
                        _ => $"The PDF could not be opened (PDFium error {error}).",
                    });
                }

                var count = PdfiumNative.FPDF_GetPageCount(document);
                var sizes = new PdfPageSize[Math.Max(0, count)];
                for (var i = 0; i < sizes.Length; i++)
                {
                    sizes[i] = PdfiumNative.FPDF_GetPageSizeByIndex(document, i, out var width, out var height) != 0
                        ? new PdfPageSize(width, height)
                        : new PdfPageSize(612, 792);
                }

                return new PdfiumRasterDocument(document, buffer, sizes);
            }
        }

        public PdfPageSize GetPageSize(int pageIndex)
        {
            ObjectDisposedException.ThrowIf(_document == IntPtr.Zero, this);
            ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _pageSizes.Length);
            return _pageSizes[pageIndex];
        }

        public PdfRasterImage RenderPage(int pageIndex, double pixelsPerPoint, PdfRenderPurpose purpose = PdfRenderPurpose.Display, CancellationToken cancellationToken = default)
        {
            var size = GetPageSize(pageIndex);
            if (!double.IsFinite(pixelsPerPoint) || pixelsPerPoint <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelsPerPoint));
            }

            var width = Math.Clamp((int)Math.Round(size.WidthPoints * pixelsPerPoint), 1, MaxPixelDimension);
            var height = Math.Clamp((int)Math.Round(size.HeightPoints * pixelsPerPoint), 1, MaxPixelDimension);
            var flags = PdfiumNative.FPDF_ANNOT | (purpose == PdfRenderPurpose.Print ? PdfiumNative.FPDF_PRINTING : 0);

            cancellationToken.ThrowIfCancellationRequested();
            lock (PdfiumNative.Sync)
            {
                // Checked again under the lock: Dispose takes the same lock, so a render can never race a close.
                ObjectDisposedException.ThrowIf(_document == IntPtr.Zero, this);
                var page = PdfiumNative.FPDF_LoadPage(_document, pageIndex);
                if (page == IntPtr.Zero)
                {
                    throw new PdfRasterizationException($"Page {pageIndex + 1} could not be loaded.");
                }

                try
                {
                    return RenderLocked(page, width, height, 0, 0, width, height, flags);
                }
                finally
                {
                    PdfiumNative.FPDF_ClosePage(page);
                }
            }
        }

        public void Dispose()
        {
            lock (PdfiumNative.Sync)
            {
                if (_document != IntPtr.Zero)
                {
                    PdfiumNative.FPDF_CloseDocument(_document);
                    _document = IntPtr.Zero;
                }

                if (_buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_buffer);
                    _buffer = IntPtr.Zero;
                }
            }
        }
    }
}
