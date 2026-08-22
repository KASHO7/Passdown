namespace Carowinds.Passdown.Desktop.Services;

/// <summary>How the native save-file interaction for a diagnostic export ZIP ended (mirrors <see cref="PdfSaveOutcome"/>).</summary>
public enum DiagnosticExportOutcome
{
    Saved,
    Cancelled,
    Failed,
}

public sealed record DiagnosticExportSaveResult(DiagnosticExportOutcome Outcome, string? FilePath, string? ErrorMessage)
{
    public static DiagnosticExportSaveResult Cancelled() => new(DiagnosticExportOutcome.Cancelled, null, null);

    public static DiagnosticExportSaveResult Saved(string? filePath) => new(DiagnosticExportOutcome.Saved, filePath, null);

    public static DiagnosticExportSaveResult Failed(string errorMessage) => new(DiagnosticExportOutcome.Failed, null, errorMessage);
}

/// <summary>
/// The seam between <see cref="Carowinds.Passdown.Desktop.ViewModels.AssociateViewModel"/> and
/// Avalonia's native storage/save-file APIs for the diagnostic export ZIP (Phase 7F Part F/G) —
/// mirrors <see cref="IPdfSaveFilePicker"/> exactly, so the ViewModel stays constructible and
/// testable without a live <c>Window</c>, and the destination is always wherever the user
/// explicitly chose, never a fixed or app-relative folder.
/// </summary>
public interface IDiagnosticExportFilePicker
{
    Task<DiagnosticExportSaveResult> SaveAsync(byte[] zipBytes, string suggestedFileName, CancellationToken cancellationToken = default);
}
