namespace Carowinds.Passdown.Desktop.Services;

/// <summary>How opening a frozen finalized PDF in the OS-associated viewer ended (spec Phase 9D Part I).</summary>
public enum PdfOpenOutcome
{
    Opened,
    Failed,
}

/// <summary>Result of one <see cref="IFinalizedPdfLauncher.OpenAsync"/> call.</summary>
public sealed record PdfOpenResult(PdfOpenOutcome Outcome, string? ErrorMessage)
{
    public static PdfOpenResult Opened() => new(PdfOpenOutcome.Opened, null);

    public static PdfOpenResult Failed(string errorMessage) => new(PdfOpenOutcome.Failed, errorMessage);
}

/// <summary>
/// Opens an already-retrieved frozen PDF (spec Phase 9D Part I) in whatever the OS has associated
/// with .pdf files. Deliberately never regenerates a PDF and never requires a Save dialog just to view
/// one — the caller hands over exact stored bytes it already has from
/// <see cref="Application.Finalization.IFinalizedPassdownPdfRetrievalService"/>.
/// </summary>
public interface IFinalizedPdfLauncher
{
    Task<PdfOpenResult> OpenAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default);
}
