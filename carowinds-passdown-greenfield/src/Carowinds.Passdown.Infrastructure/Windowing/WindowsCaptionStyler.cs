using System.Runtime.InteropServices;
using Carowinds.Passdown.Application.Windowing;

namespace Carowinds.Passdown.Infrastructure.Windowing;

/// <summary>
/// Phase 35D: the navy application title bar, done the supported way — the window keeps its standard Windows title
/// bar and only its colours change, through the documented DWM window attributes:
/// <list type="bullet">
/// <item><c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> (20) = on, so the minimize/maximize/close glyphs are drawn light on the
/// dark caption in both app themes;</item>
/// <item><c>DWMWA_CAPTION_COLOR</c> (35) = the title-bar colour;</item>
/// <item><c>DWMWA_TEXT_COLOR</c> (36) = the title text colour.</item>
/// </list>
/// Nothing else about the window changes: no custom chrome, no extended client area, no hit-testing, so dragging,
/// double-click maximize, the caption buttons, edge resizing, Snap layouts, maximized bounds and DPI scaling remain
/// Windows' own. The caption-colour attributes exist from Windows 11 (build 22000); earlier Windows rejects them and
/// keeps its system title bar. A high-contrast theme is never overridden.
/// <para>WINDOWS-ONLY BEHAVIOR — flagged for Windows VM testing (docs/phase35d-shows-notes-shared-polish.md).</para>
/// </summary>
public sealed partial class WindowsCaptionStyler : IWindowCaptionStyler
{
    internal const int DwmwaUseImmersiveDarkMode = 20;
    internal const int DwmwaCaptionColor = 35;
    internal const int DwmwaTextColor = 36;

    private const uint SpiGetHighContrast = 0x0042;
    private const uint HcfHighContrastOn = 0x00000001;

    private readonly Func<bool> _isWindows;
    private readonly Func<bool> _isHighContrastOn;
    private readonly Func<nint, int, int, int> _setAttribute;

    public WindowsCaptionStyler()
        : this(OperatingSystem.IsWindows, IsHighContrastOn, SetAttribute)
    {
    }

    /// <summary>Test seam: <paramref name="setAttribute"/> returns the HRESULT of DwmSetWindowAttribute(hwnd, attribute, value).</summary>
    internal WindowsCaptionStyler(Func<bool> isWindows, Func<bool> isHighContrastOn, Func<nint, int, int, int> setAttribute)
    {
        _isWindows = isWindows;
        _isHighContrastOn = isHighContrastOn;
        _setAttribute = setAttribute;
    }

    /// <summary>A Win32 COLORREF: 0x00BBGGRR.</summary>
    internal static int ToColorRef(CaptionColor color) => color.Red | (color.Green << 8) | (color.Blue << 16);

    public CaptionStyleResult Apply(nint windowHandle, CaptionColor caption, CaptionColor text)
    {
        if (!_isWindows())
        {
            return CaptionStyleResult.NotSupportedOnThisPlatform;
        }

        if (windowHandle == 0)
        {
            return CaptionStyleResult.Failed;
        }

        try
        {
            if (_isHighContrastOn())
            {
                return CaptionStyleResult.SkippedForHighContrast;
            }

            // The caption colour first: if this Windows has no such attribute, change nothing at all (not even the
            // dark-mode flag), so an older Windows keeps exactly the title bar it had.
            if (_setAttribute(windowHandle, DwmwaCaptionColor, ToColorRef(caption)) < 0)
            {
                return CaptionStyleResult.NotSupportedByThisWindowsVersion;
            }

            _setAttribute(windowHandle, DwmwaUseImmersiveDarkMode, 1);
            _setAttribute(windowHandle, DwmwaTextColor, ToColorRef(text));
            return CaptionStyleResult.Applied;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return CaptionStyleResult.Failed;
        }
    }

    private static int SetAttribute(nint hwnd, int attribute, int value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return -1;
        }

        return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
    }

    private static bool IsHighContrastOn()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var info = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
        return SystemParametersInfoW(SpiGetHighContrast, info.Size, ref info, 0) && (info.Flags & HcfHighContrastOn) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, ref HighContrast pvParam, uint fWinIni);
}
