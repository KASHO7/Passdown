using System.Diagnostics;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// <see cref="IFinalizedPdfLauncher"/> that writes the exact frozen bytes to a per-filename temp file
/// and asks the OS to open it with whatever the user has associated with .pdf (spec Part I). Reuses
/// the same temp path for a given <paramref name="suggestedFileName"/> across calls (overwriting it)
/// rather than minting a new file every time, so repeatedly opening history PDFs does not grow this
/// folder without bound. Deliberately never deletes the file after opening it — the OS PDF viewer may
/// still be reading it, and deleting out from under an open viewer would be worse than leaving a small,
/// reused temp file behind (spec Part I "clean up temp files where practical, without breaking the
/// viewer").
/// </summary>
public sealed class TempFileFinalizedPdfLauncher : IFinalizedPdfLauncher
{
    private static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "Carowinds Passdown", "History");

    public async Task<PdfOpenResult> OpenAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        try
        {
            Directory.CreateDirectory(TempDirectory);
            var filePath = Path.Combine(TempDirectory, suggestedFileName);
            await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken).ConfigureAwait(false);

            using var process = Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return process is null
                ? PdfOpenResult.Failed("Could not open the PDF with your default viewer.")
                : PdfOpenResult.Opened();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PdfOpenResult.Failed("The PDF could not be opened. Try again.");
        }
    }
}
