namespace Carowinds.Passdown.Application.PdfOutput;

/// <summary>Phase 33: the print service on platforms without in-app printing (the macOS/Linux development hosts). Never prints.</summary>
public sealed class UnsupportedPlatformPdfPrintService : IPdfPrintService
{
    public const string UnsupportedMessage = "Printing from the app is available on Windows. Use Save PDF and print the saved file instead.";

    public bool IsSupported => false;

    public Task<PdfPrintResult> PrintAsync(PdfPrintRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(PdfPrintResult.Unavailable(UnsupportedMessage));
}
