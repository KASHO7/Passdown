namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// Desktop clipboard seam (Phase 18F) — keeps platform clipboard calls out of ViewModel logic so the
/// Associate "Copy" actions are testable without a real desktop clipboard. Implementations throw on
/// failure; the caller turns that into a small, non-fatal message.
/// </summary>
public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}
