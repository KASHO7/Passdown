namespace Carowinds.Passdown.Application.PdfOutput;

/// <summary>
/// Phase 33: sends one already-generated PDF document to a printer the user chooses. Implementations
/// must always show the platform's printer-selection dialog first (never print silently to the default
/// printer), must not upload the document anywhere, and must not leave the document on disk.
/// </summary>
public interface IPdfPrintService
{
    /// <summary>True when this platform can print from inside the application.</summary>
    bool IsSupported { get; }

    /// <summary>Shows the print dialog and, when the user confirms, prints <see cref="PdfPrintRequest.PdfBytes"/>. Never throws for an ordinary failure — the result says what happened.</summary>
    Task<PdfPrintResult> PrintAsync(PdfPrintRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What to print. <paramref name="PageCount"/> bounds the dialog's page-range choice.</summary>
public sealed record PdfPrintRequest(byte[] PdfBytes, string DocumentName, int PageCount);

/// <summary>How one print attempt ended.</summary>
public enum PdfPrintOutcome
{
    /// <summary>The job was handed to the printer's spooler.</summary>
    Printed,

    /// <summary>The user closed the print dialog (or a "print to file" save prompt) without printing.</summary>
    Cancelled,

    /// <summary>No printer could be used (none installed, or printing is not available on this platform).</summary>
    Unavailable,

    /// <summary>The job could not be created or sent.</summary>
    Failed,
}

/// <summary>Result of one <see cref="IPdfPrintService.PrintAsync"/> call; <see cref="Message"/> is user-facing text for Unavailable/Failed.</summary>
public sealed record PdfPrintResult(PdfPrintOutcome Outcome, string? Message = null, string? PrinterName = null, int PagesSent = 0)
{
    public static PdfPrintResult Printed(string? printerName, int pagesSent) => new(PdfPrintOutcome.Printed, null, printerName, pagesSent);

    public static PdfPrintResult Cancelled() => new(PdfPrintOutcome.Cancelled);

    public static PdfPrintResult Unavailable(string message) => new(PdfPrintOutcome.Unavailable, message);

    public static PdfPrintResult Failed(string message) => new(PdfPrintOutcome.Failed, message);
}
