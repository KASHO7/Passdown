using System.Diagnostics;
using Carowinds.Passdown.Infrastructure.Files;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// <see cref="IFinalizedPdfLauncher"/> that writes the exact frozen bytes to an app-owned temp file
/// and asks the OS to open it with whatever the user has associated with .pdf (spec Part I).
/// Phase 18B PRIV-002: each open uses a new unique file (<see cref="HistoryPdfTempFileStore"/>), so a
/// viewer still holding yesterday's copy locked can no longer make "Open Final PDF" fail. The file is
/// not deleted after a successful open — the OS viewer may still be reading it — but stale copies are
/// swept by age before every open and once at startup; a failed write, or a launch that throws (no
/// viewer could be started), removes its file at once.
/// </summary>
public sealed class TempFileFinalizedPdfLauncher(HistoryPdfTempFileStore store) : IFinalizedPdfLauncher
{
    public TempFileFinalizedPdfLauncher()
        : this(new HistoryPdfTempFileStore(HistoryPdfTempFileStore.DefaultDirectory))
    {
    }

    public async Task<PdfOpenResult> OpenAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        store.CleanStale();

        string filePath;
        try
        {
            filePath = await store.WriteAsync(pdfBytes, suggestedFileName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PdfOpenResult.Failed("The PDF could not be opened. Try again.");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            // A null process is not proof nothing opened the file: on Windows, a viewer that is already
            // running (e.g. Acrobat) receives the file via the shell without a new process. So the file
            // is left for the age-based sweep rather than deleted from under that viewer.
            return process is null
                ? PdfOpenResult.Failed("Could not open the PDF with your default viewer.")
                : PdfOpenResult.Opened();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            store.TryDelete(filePath);
            return PdfOpenResult.Failed("The PDF could not be opened. Try again.");
        }
    }
}
