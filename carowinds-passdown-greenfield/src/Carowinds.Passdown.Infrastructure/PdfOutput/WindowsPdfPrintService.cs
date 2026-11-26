using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Carowinds.Passdown.Application.PdfOutput;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.PdfOutput;

/// <summary>
/// Phase 33: direct Windows printing of an already-generated Passdown PDF.
/// <list type="number">
/// <item>The standard Windows print dialog (<c>PrintDlgEx</c>, owned by the app window) — printer choice,
/// Preferences (paper size, orientation, duplex, quality as the driver offers them), copies/collate, and
/// a page range. Nothing is ever sent without the user pressing Print.</item>
/// <item>The job is started on the calling (UI) thread so a "print to file" driver's own save prompt
/// (Microsoft Print to PDF) is modal over the app; pages are then spooled on a background thread.</item>
/// <item>Each page is rasterized by PDFium at the printer's resolution (capped at
/// <see cref="PrintLayoutMath.MaxRenderDpi"/>) in horizontal bands, so memory stays bounded, and drawn at
/// actual size (shrunk only when it does not fit the chosen paper).</item>
/// </list>
/// The PDF only ever lives in process memory: nothing is written to a temporary file or uploaded.
/// WINDOWS-ONLY: verify on a Windows VM (docs/phase33-live-pdf-preview-printing.md).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsPdfPrintService(Func<IntPtr> ownerWindowProvider, ILogger<WindowsPdfPrintService>? logger = null) : IPdfPrintService
{
    public bool IsSupported => true;

    public async Task<PdfPrintResult> PrintAsync(PdfPrintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = ownerWindowProvider();
        if (owner == IntPtr.Zero)
        {
            return PdfPrintResult.Unavailable("No application window is available to show the print dialog.");
        }

        PdfiumPdfRasterizer.PdfiumRasterDocument document;
        try
        {
            document = PdfiumPdfRasterizer.PdfiumRasterDocument.Open(request.PdfBytes);
        }
        catch (Exception ex) when (ex is PdfRasterizationException or DllNotFoundException or EntryPointNotFoundException)
        {
            logger?.LogError(ex, "The Passdown PDF could not be prepared for printing.");
            return PdfPrintResult.Failed("The Passdown could not be prepared for printing. Try again.");
        }

        using (document)
        {
            var dialog = ShowPrintDialog(owner, Math.Max(1, document.PageCount));
            if (dialog.Result is not null)
            {
                return dialog.Result;
            }

            var hdc = dialog.Hdc;
            try
            {
                var pages = PrintLayoutMath.ExpandPageRanges(dialog.Ranges, document.PageCount);
                if (pages.Count == 0)
                {
                    return PdfPrintResult.Failed("The selected page range does not contain any pages of this Passdown.");
                }

                var start = StartJob(hdc, request.DocumentName);
                if (start is not null)
                {
                    return start;
                }

                var spooled = await Task.Run(() => SpoolPages(hdc, document, pages, cancellationToken), CancellationToken.None).ConfigureAwait(true);
                return spooled ?? PdfPrintResult.Printed(dialog.PrinterName, pages.Count);
            }
            finally
            {
                Native.DeleteDC(hdc);
            }
        }
    }

    private PdfPrintResult? StartJob(IntPtr hdc, string documentName)
    {
        var namePointer = Marshal.StringToHGlobalUni(string.IsNullOrWhiteSpace(documentName) ? "Carowinds Operations Passdown" : documentName);
        try
        {
            var info = new Native.DOCINFOW { cbSize = Marshal.SizeOf<Native.DOCINFOW>(), lpszDocName = namePointer };
            if (Native.StartDocW(hdc, ref info) > 0)
            {
                return null;
            }

            var error = Marshal.GetLastPInvokeError();
            if (error == Native.ERROR_CANCELLED)
            {
                return PdfPrintResult.Cancelled(); // e.g. the Microsoft Print to PDF "Save As" prompt was cancelled.
            }

            logger?.LogWarning("StartDoc failed with Win32 error {Error}.", error);
            return PdfPrintResult.Failed("The printer could not start the print job. Check that the printer is on and connected, then try again.");
        }
        finally
        {
            Marshal.FreeHGlobal(namePointer);
        }
    }

    private PdfPrintResult? SpoolPages(IntPtr hdc, PdfiumPdfRasterizer.PdfiumRasterDocument document, IReadOnlyList<int> pages, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var pageIndex in pages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Native.AbortDoc(hdc);
                    return PdfPrintResult.Cancelled();
                }

                if (Native.StartPage(hdc) <= 0)
                {
                    Native.AbortDoc(hdc);
                    return PdfPrintResult.Failed("The printer stopped accepting the print job. Nothing more was sent.");
                }

                DrawPage(hdc, document, pageIndex);

                if (Native.EndPage(hdc) <= 0)
                {
                    Native.AbortDoc(hdc);
                    return PdfPrintResult.Failed("The printer stopped accepting the print job. Nothing more was sent.");
                }
            }

            if (Native.EndDoc(hdc) <= 0)
            {
                return PdfPrintResult.Failed("The print job could not be finished. Check the printer's queue.");
            }

            return null;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Printing the Passdown failed.");
            Native.AbortDoc(hdc);
            return PdfPrintResult.Failed("Printing failed. Nothing more was sent to the printer. Try again.");
        }
    }

    private static unsafe void DrawPage(IntPtr hdc, PdfiumPdfRasterizer.PdfiumRasterDocument document, int pageIndex)
    {
        var size = document.GetPageSize(pageIndex);
        var dpiX = Native.GetDeviceCaps(hdc, Native.LOGPIXELSX);
        var dpiY = Native.GetDeviceCaps(hdc, Native.LOGPIXELSY);
        var target = PrintLayoutMath.PlacePage(
            size.WidthPoints, size.HeightPoints, dpiX, dpiY,
            Native.GetDeviceCaps(hdc, Native.PHYSICALWIDTH), Native.GetDeviceCaps(hdc, Native.PHYSICALHEIGHT),
            Native.GetDeviceCaps(hdc, Native.PHYSICALOFFSETX), Native.GetDeviceCaps(hdc, Native.PHYSICALOFFSETY));

        var renderScale = PrintLayoutMath.RenderScaleFor(dpiX, dpiY);
        var renderWidth = Math.Max(1, (int)Math.Round(target.Width * renderScale));
        var renderHeight = Math.Max(1, (int)Math.Round(target.Height * renderScale));
        if (renderScale < 1.0)
        {
            Native.SetStretchBltMode(hdc, Native.HALFTONE);
            Native.SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
        }

        var bandHeight = PrintLayoutMath.BandHeightFor(renderWidth);
        var flags = PdfiumNative.FPDF_ANNOT | PdfiumNative.FPDF_PRINTING;
        for (var top = 0; top < renderHeight; top += bandHeight)
        {
            var rows = Math.Min(bandHeight, renderHeight - top);
            var band = PdfiumPdfRasterizer.RenderBand(document.Handle, pageIndex, renderWidth, renderHeight, top, rows, flags);
            var bits = PrintLayoutMath.ToBgr24(band.Bgra, band.PixelWidth, band.PixelHeight, band.Stride, out _);

            // Destination rows derived from the band edges (not accumulated) so stretched bands never leave seams.
            var destinationTop = target.Y + (int)Math.Round(top * (double)target.Height / renderHeight);
            var destinationBottom = target.Y + (int)Math.Round((top + rows) * (double)target.Height / renderHeight);

            var header = new Native.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(Native.BITMAPINFOHEADER),
                biWidth = renderWidth,
                biHeight = -rows, // negative: top-down rows
                biPlanes = 1,
                biBitCount = 24,
                biCompression = 0, // BI_RGB
            };

            fixed (byte* pointer = bits)
            {
                if (Native.StretchDIBits(hdc, target.X, destinationTop, target.Width, destinationBottom - destinationTop, 0, 0, renderWidth, rows, pointer, ref header, 0, Native.SRCCOPY) == 0)
                {
                    throw new InvalidOperationException("The printer driver rejected the page image.");
                }
            }
        }
    }

    private sealed record DialogOutcome(PdfPrintResult? Result, IntPtr Hdc, IReadOnlyList<(int From, int To)>? Ranges, string? PrinterName);

    private DialogOutcome ShowPrintDialog(IntPtr owner, int pageCount)
    {
        const int maxRanges = 16;
        var rangeBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<Native.PRINTPAGERANGE>() * maxRanges);
        var dialog = new Native.PRINTDLGEX
        {
            lStructSize = (uint)Marshal.SizeOf<Native.PRINTDLGEX>(),
            hwndOwner = owner,
            Flags = Native.PD_RETURNDC | Native.PD_USEDEVMODECOPIESANDCOLLATE | Native.PD_NOSELECTION | Native.PD_NOCURRENTPAGE,
            nPageRanges = 1,
            nMaxPageRanges = maxRanges,
            lpPageRanges = rangeBuffer,
            nMinPage = 1,
            nMaxPage = (uint)pageCount,
            nCopies = 1,
            nStartPage = Native.START_PAGE_GENERAL,
        };
        Marshal.StructureToPtr(new Native.PRINTPAGERANGE { nFromPage = 1, nToPage = (uint)pageCount }, rangeBuffer, false);

        try
        {
            var hr = Native.PrintDlgExW(ref dialog);
            if (hr != 0)
            {
                var extended = Native.CommDlgExtendedError();
                logger?.LogWarning("PrintDlgEx failed: HRESULT 0x{Hr:X8}, extended error 0x{Extended:X}.", hr, extended);
                if (dialog.hDC != IntPtr.Zero)
                {
                    Native.DeleteDC(dialog.hDC);
                }

                return new DialogOutcome(
                    extended is Native.PDERR_NODEFAULTPRN or Native.PDERR_NODEVICES or Native.PDERR_PRINTERNOTFOUND
                        ? PdfPrintResult.Unavailable("No printer is available. Add a printer in Windows Settings → Bluetooth & devices → Printers & scanners, then try again.")
                        : PdfPrintResult.Failed("The Windows print dialog could not be opened. Try again."),
                    IntPtr.Zero, null, null);
            }

            if (dialog.dwResultAction != Native.PD_RESULT_PRINT)
            {
                if (dialog.hDC != IntPtr.Zero)
                {
                    Native.DeleteDC(dialog.hDC);
                }

                return new DialogOutcome(PdfPrintResult.Cancelled(), IntPtr.Zero, null, null);
            }

            var printerName = ReadPrinterName(dialog.hDevNames);
            if (dialog.hDC == IntPtr.Zero)
            {
                return new DialogOutcome(PdfPrintResult.Unavailable("The selected printer is not available. Check that it is on and connected, or choose another printer."), IntPtr.Zero, null, printerName);
            }

            List<(int From, int To)>? ranges = null;
            if ((dialog.Flags & Native.PD_PAGENUMS) != 0)
            {
                ranges = [];
                for (var i = 0; i < dialog.nPageRanges && i < maxRanges; i++)
                {
                    var range = Marshal.PtrToStructure<Native.PRINTPAGERANGE>(rangeBuffer + (i * Marshal.SizeOf<Native.PRINTPAGERANGE>()));
                    ranges.Add(((int)range.nFromPage, (int)range.nToPage));
                }
            }

            return new DialogOutcome(null, dialog.hDC, ranges, printerName);
        }
        finally
        {
            Marshal.FreeHGlobal(rangeBuffer);
            if (dialog.hDevMode != IntPtr.Zero)
            {
                Native.GlobalFree(dialog.hDevMode);
            }

            if (dialog.hDevNames != IntPtr.Zero)
            {
                Native.GlobalFree(dialog.hDevNames);
            }
        }
    }

    private static string? ReadPrinterName(IntPtr hDevNames)
    {
        if (hDevNames == IntPtr.Zero)
        {
            return null;
        }

        var pointer = Native.GlobalLock(hDevNames);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // DEVNAMES: four WORD offsets (in characters) from the start of the block; the second is the device name.
            var deviceOffset = (ushort)Marshal.ReadInt16(pointer, 2);
            return Marshal.PtrToStringUni(pointer + (deviceOffset * 2));
        }
        finally
        {
            Native.GlobalUnlock(hDevNames);
        }
    }

    /// <summary>Win32 declarations (comdlg32/gdi32/kernel32). Struct layouts follow commdlg.h/wingdi.h with natural x64 alignment.</summary>
    private static partial class Native
    {
        public const uint PD_PAGENUMS = 0x00000002;
        public const uint PD_NOSELECTION = 0x00000004;
        public const uint PD_RETURNDC = 0x00000100;
        public const uint PD_USEDEVMODECOPIESANDCOLLATE = 0x00040000;
        public const uint PD_NOCURRENTPAGE = 0x00800000;
        public const uint START_PAGE_GENERAL = 0xFFFFFFFF;
        public const uint PD_RESULT_PRINT = 1;

        public const uint PDERR_NODEVICES = 0x1007;
        public const uint PDERR_NODEFAULTPRN = 0x1008;
        public const uint PDERR_PRINTERNOTFOUND = 0x100B;

        public const int ERROR_CANCELLED = 1223;

        public const int LOGPIXELSX = 88;
        public const int LOGPIXELSY = 90;
        public const int PHYSICALWIDTH = 110;
        public const int PHYSICALHEIGHT = 111;
        public const int PHYSICALOFFSETX = 112;
        public const int PHYSICALOFFSETY = 113;
        public const int HALFTONE = 4;
        public const uint SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        public struct PRINTPAGERANGE
        {
            public uint nFromPage;
            public uint nToPage;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PRINTDLGEX
        {
            public uint lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hDevMode;
            public IntPtr hDevNames;
            public IntPtr hDC;
            public uint Flags;
            public uint Flags2;
            public uint ExclusionFlags;
            public uint nPageRanges;
            public uint nMaxPageRanges;
            public IntPtr lpPageRanges;
            public uint nMinPage;
            public uint nMaxPage;
            public uint nCopies;
            public IntPtr hInstance;
            public IntPtr lpPrintTemplateName;
            public IntPtr lpCallback;
            public uint nPropertyPages;
            public IntPtr lphPropertyPages;
            public uint nStartPage;
            public uint dwResultAction;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DOCINFOW
        {
            public int cbSize;
            public IntPtr lpszDocName;
            public IntPtr lpszOutput;
            public IntPtr lpszDatatype;
            public uint fwType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [LibraryImport("comdlg32.dll")]
        public static partial int PrintDlgExW(ref PRINTDLGEX dialog);

        [LibraryImport("comdlg32.dll")]
        public static partial uint CommDlgExtendedError();

        [LibraryImport("gdi32.dll", SetLastError = true)]
        public static partial int StartDocW(IntPtr hdc, ref DOCINFOW info);

        [LibraryImport("gdi32.dll")]
        public static partial int StartPage(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        public static partial int EndPage(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        public static partial int EndDoc(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        public static partial int AbortDoc(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeleteDC(IntPtr hdc);

        [LibraryImport("gdi32.dll")]
        public static partial int GetDeviceCaps(IntPtr hdc, int index);

        [LibraryImport("gdi32.dll")]
        public static partial int SetStretchBltMode(IntPtr hdc, int mode);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr previous);

        [LibraryImport("gdi32.dll")]
        public static unsafe partial int StretchDIBits(IntPtr hdc, int xDest, int yDest, int destWidth, int destHeight, int xSrc, int ySrc, int srcWidth, int srcHeight, byte* bits, ref BITMAPINFOHEADER info, uint usage, uint rop);

        [LibraryImport("kernel32.dll")]
        public static partial IntPtr GlobalLock(IntPtr handle);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GlobalUnlock(IntPtr handle);

        [LibraryImport("kernel32.dll")]
        public static partial IntPtr GlobalFree(IntPtr handle);
    }
}
