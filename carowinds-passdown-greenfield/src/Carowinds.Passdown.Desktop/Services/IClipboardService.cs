namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// Desktop clipboard seam (Phase 18F) — keeps platform clipboard calls out of ViewModel logic so the
/// Associate "Copy" actions are testable without a real desktop clipboard. Implementations throw on
/// failure; the caller turns that into a small, non-fatal message.
/// </summary>
public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 29: places formatted text and its plain-text equivalent on the clipboard in one operation, so
    /// a rich editor (Teams) pastes the formatting and anything else pastes <paramref name="text"/>.
    /// <paramref name="htmlFragment"/> is already-safe markup; the implementation adds the platform
    /// envelope (Windows "HTML Format" / CF_HTML).
    /// </summary>
    Task SetTextAndHtmlAsync(string text, string htmlFragment, CancellationToken cancellationToken = default);

    /// <summary>Phase 29: places a local image file on the clipboard as a picture. Never reads anything but the given local file.</summary>
    Task SetImageFileAsync(string filePath, CancellationToken cancellationToken = default);
}
