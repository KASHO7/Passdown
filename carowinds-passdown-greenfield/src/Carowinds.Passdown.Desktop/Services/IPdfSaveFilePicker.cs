namespace Carowinds.Passdown.Desktop.Services;

/// <summary>How the native save-file interaction ended (spec Phase 4F Part I).</summary>
public enum PdfSaveOutcome
{
    Saved,
    Cancelled,
    Failed,
}

/// <summary>Result of one <see cref="IPdfSaveFilePicker.SaveAsync"/> call. <see cref="FilePath"/> is only ever whatever the native picker/user chose — this project never hard-codes a destination folder.</summary>
public sealed record PdfSaveResult(PdfSaveOutcome Outcome, string? FilePath, string? ErrorMessage)
{
    public static PdfSaveResult Cancelled() => new(PdfSaveOutcome.Cancelled, null, null);

    public static PdfSaveResult Saved(string? filePath) => new(PdfSaveOutcome.Saved, filePath, null);

    public static PdfSaveResult Failed(string errorMessage) => new(PdfSaveOutcome.Failed, null, errorMessage);
}

/// <summary>
/// The one seam between <see cref="Carowinds.Passdown.Desktop.ViewModels.AssociateViewModel"/> and
/// Avalonia's native storage/save-file APIs (spec Part I/Part T: file-picker integration belongs in
/// Desktop, but not directly inside the ViewModel, so the ViewModel stays constructible and testable
/// without a live <c>Window</c>).
/// </summary>
public interface IPdfSaveFilePicker
{
    /// <summary>Opens the native save-file picker pre-filled with <paramref name="suggestedFileName"/>, then writes <paramref name="pdfBytes"/> to wherever the user chose. Cancelling the picker returns <see cref="PdfSaveOutcome.Cancelled"/>, never <see cref="PdfSaveOutcome.Failed"/>.</summary>
    Task<PdfSaveResult> SaveAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default);
}
