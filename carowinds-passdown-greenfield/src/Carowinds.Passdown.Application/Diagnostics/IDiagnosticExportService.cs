namespace Carowinds.Passdown.Application.Diagnostics;

/// <summary>Result of building one diagnostic export ZIP in memory (Phase 7F Part G) — bytes plus a suggested, safe file name. Never written to the app directory by this seam; the caller (Desktop) decides the destination via a native save picker.</summary>
public sealed record DiagnosticExportResult(byte[] ZipBytes, string SuggestedFileName);

/// <summary>
/// Builds the full sanitized diagnostic export ZIP (Phase 7F Parts D/G): summary.txt, system.txt,
/// source-status.txt, and a sanitized log excerpt. Never includes the SQLite database file, drafts,
/// or any operational note content (Part D).
/// </summary>
public interface IDiagnosticExportService
{
    Task<DiagnosticExportResult> BuildZipAsync(CancellationToken cancellationToken = default);
}
