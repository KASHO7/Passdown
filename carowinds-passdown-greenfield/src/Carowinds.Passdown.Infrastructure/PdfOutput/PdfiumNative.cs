using System.Runtime.InteropServices;

namespace Carowinds.Passdown.Infrastructure.PdfOutput;

/// <summary>
/// Phase 33: the minimal PDFium C API surface the preview and printer need (fpdfview.h, chromium/8138).
/// PDFium is not thread-safe, so every call goes through <see cref="Sync"/> — callers must hold it.
/// The native library ships as runtimes/&lt;rid&gt;/native/(lib)pdfium.(dll|dylib|so) from the
/// bblanchon.PDFium.* packages; the build is without V8/XFA, so no PDF JavaScript can ever run.
/// </summary>
internal static partial class PdfiumNative
{
    private const string Library = "pdfium";

    /// <summary>Serializes every PDFium call in the process.</summary>
    public static readonly Lock Sync = new();

    public const int FPDF_ANNOT = 0x01;
    public const int FPDF_LCD_TEXT = 0x02;
    public const int FPDF_PRINTING = 0x800;

    public const uint FPDF_ERR_SUCCESS = 0;
    public const uint FPDF_ERR_FORMAT = 3;
    public const uint FPDF_ERR_PASSWORD = 4;

    private static volatile bool _initialized;

    /// <summary>Phase 34B-2: whether the native library has been loaded and initialized in this process (read without the lock; only ever goes false → true).</summary>
    public static bool IsInitialized => _initialized;

    /// <summary>Initializes PDFium once per process. Caller holds <see cref="Sync"/>.</summary>
    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        FPDF_InitLibrary();
        _initialized = true;
    }

    [LibraryImport(Library)]
    private static partial void FPDF_InitLibrary();

    [LibraryImport(Library)]
    public static partial IntPtr FPDF_LoadMemDocument64(IntPtr dataBuffer, nuint size, IntPtr password);

    [LibraryImport(Library)]
    public static partial void FPDF_CloseDocument(IntPtr document);

    [LibraryImport(Library)]
    public static partial uint FPDF_GetLastError();

    [LibraryImport(Library)]
    public static partial int FPDF_GetPageCount(IntPtr document);

    [LibraryImport(Library)]
    public static partial int FPDF_GetPageSizeByIndex(IntPtr document, int pageIndex, out double width, out double height);

    [LibraryImport(Library)]
    public static partial IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

    [LibraryImport(Library)]
    public static partial void FPDF_ClosePage(IntPtr page);

    [LibraryImport(Library)]
    public static partial IntPtr FPDFBitmap_Create(int width, int height, int alpha);

    /// <summary>PDFium's 32-bit BGRA bitmap format (the same layout <see cref="FPDFBitmap_Create"/> uses with alpha).</summary>
    public const int FPDFBitmap_BGRA = 4;

    /// <summary>Phase 34B-2: wraps caller-owned pixel memory; PDFium never frees it. The memory must stay valid until <see cref="FPDFBitmap_Destroy"/>.</summary>
    [LibraryImport(Library)]
    public static partial IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);

    [LibraryImport(Library)]
    public static partial void FPDFBitmap_Destroy(IntPtr bitmap);

    [LibraryImport(Library)]
    public static partial int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    [LibraryImport(Library)]
    public static partial IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

    [LibraryImport(Library)]
    public static partial int FPDFBitmap_GetStride(IntPtr bitmap);

    [LibraryImport(Library)]
    public static partial void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
}
