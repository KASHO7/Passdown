using System.Runtime.InteropServices;
using Carowinds.Passdown.Application.Preferences;

namespace Carowinds.Passdown.Infrastructure.Accessibility;

/// <summary>
/// Phase 35A: reads Windows' reduced-motion preference — Settings → Accessibility → Visual effects →
/// "Animation effects" — through <c>SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)</c>, the documented
/// per-user value behind WinRT <c>UISettings.AnimationsEnabled</c>. Animation effects off = reduced motion
/// requested. On any other operating system, or if the call fails, it reports
/// <see cref="SystemReducedMotion.NotDetected"/> and the in-app choice decides.
/// <para>WINDOWS-ONLY BEHAVIOR — flagged for Windows VM testing (docs/phase35-unified-ui-modernization.md).</para>
/// </summary>
public sealed partial class WindowsAnimationEffectsReader : ISystemReducedMotionReader
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    private readonly Func<bool> _isWindows;
    private readonly Func<int?> _readClientAreaAnimation;

    public WindowsAnimationEffectsReader()
        : this(OperatingSystem.IsWindows, ReadClientAreaAnimation)
    {
    }

    /// <summary>Test seam: <paramref name="readClientAreaAnimation"/> returns the raw BOOL, or null when the call failed.</summary>
    internal WindowsAnimationEffectsReader(Func<bool> isWindows, Func<int?> readClientAreaAnimation)
    {
        _isWindows = isWindows;
        _readClientAreaAnimation = readClientAreaAnimation;
    }

    public SystemReducedMotion Read()
    {
        if (!_isWindows())
        {
            return SystemReducedMotion.NotDetected;
        }

        try
        {
            return _readClientAreaAnimation() switch
            {
                null => SystemReducedMotion.NotDetected,
                0 => SystemReducedMotion.Requested,
                _ => SystemReducedMotion.NotRequested,
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return SystemReducedMotion.NotDetected;
        }
    }

    private static int? ReadClientAreaAnimation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        int enabled = 0;
        return SystemParametersInfoW(SpiGetClientAreaAnimation, 0, ref enabled, 0) ? enabled : null;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);
}
