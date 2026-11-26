namespace Carowinds.Passdown.Application.Windowing;

/// <summary>An opaque RGB colour for the operating system's window caption (title bar).</summary>
public readonly record struct CaptionColor(byte Red, byte Green, byte Blue)
{
    /// <summary>Parses "#RRGGBB" (or "#AARRGGBB", alpha ignored).</summary>
    public static CaptionColor Parse(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        var digits = hex.TrimStart('#');
        if (digits.Length == 8)
        {
            digits = digits[2..];
        }

        if (digits.Length != 6)
        {
            throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
        }

        var value = Convert.ToInt32(digits, 16);
        return new CaptionColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}

/// <summary>What happened when the caption colour was requested.</summary>
public enum CaptionStyleResult
{
    /// <summary>The caption, its title text and its caption buttons now use the requested colours.</summary>
    Applied,

    /// <summary>Not Windows (the native title bar of the current platform is left exactly as it is).</summary>
    NotSupportedOnThisPlatform,

    /// <summary>This Windows version has no caption-colour attribute (before Windows 11) — the system title bar is kept.</summary>
    NotSupportedByThisWindowsVersion,

    /// <summary>A Windows high-contrast theme is on — its own title-bar colours are kept, never overridden.</summary>
    SkippedForHighContrast,

    /// <summary>The window has no native handle yet, or the call failed; nothing was changed.</summary>
    Failed,
}

/// <summary>
/// Phase 35D: colours the native window title bar (caption) while keeping the operating system's own title bar —
/// so dragging, double-click maximize, minimize/maximize/close, edge resizing, Snap layouts, maximized bounds and
/// per-monitor DPI all stay native. Presentation only: never touches data, preferences or the window's identity.
/// </summary>
public interface IWindowCaptionStyler
{
    /// <param name="windowHandle">The native window handle (HWND on Windows).</param>
    /// <param name="caption">The caption background.</param>
    /// <param name="text">The caption title text.</param>
    CaptionStyleResult Apply(nint windowHandle, CaptionColor caption, CaptionColor text);
}
