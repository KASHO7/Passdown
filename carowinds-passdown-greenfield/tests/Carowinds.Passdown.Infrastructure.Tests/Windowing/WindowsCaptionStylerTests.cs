using Carowinds.Passdown.Application.Windowing;
using Carowinds.Passdown.Infrastructure.Windowing;

namespace Carowinds.Passdown.Infrastructure.Tests.Windowing;

/// <summary>
/// Phase 35D: the navy title bar through documented DWM attributes only — caption colour first (so an older Windows
/// changes nothing), then light caption-button glyphs and the white title text; never over a high-contrast theme; nothing
/// at all off Windows. The real DWM call is WINDOWS-ONLY and checked on the Windows VM.
/// </summary>
public class WindowsCaptionStylerTests
{
    private static readonly CaptionColor Navy = CaptionColor.Parse("#1B3553");
    private static readonly CaptionColor White = CaptionColor.Parse("#FFFFFF");

    private sealed class Recorder
    {
        public List<(nint Hwnd, int Attribute, int Value)> Calls { get; } = [];

        public Func<int, int> Result { get; set; } = _ => 0;

        public int Set(nint hwnd, int attribute, int value)
        {
            Calls.Add((hwnd, attribute, value));
            return Result(attribute);
        }
    }

    [Fact]
    public void OnWindows_SetsCaptionColour_ThenLightGlyphs_ThenTitleText()
    {
        var recorder = new Recorder();
        var styler = new WindowsCaptionStyler(() => true, () => false, recorder.Set);

        var result = styler.Apply(42, Navy, White);

        Assert.Equal(CaptionStyleResult.Applied, result);
        Assert.Equal(
        [
            (42, WindowsCaptionStyler.DwmwaCaptionColor, 0x0053351B), // COLORREF 0x00BBGGRR
            (42, WindowsCaptionStyler.DwmwaUseImmersiveDarkMode, 1),
            (42, WindowsCaptionStyler.DwmwaTextColor, 0x00FFFFFF),
        ], recorder.Calls.Select(c => (c.Hwnd, c.Attribute, c.Value)));
        Assert.Equal(35, WindowsCaptionStyler.DwmwaCaptionColor);
        Assert.Equal(36, WindowsCaptionStyler.DwmwaTextColor);
        Assert.Equal(20, WindowsCaptionStyler.DwmwaUseImmersiveDarkMode);
    }

    [Fact]
    public void WindowsWithoutCaptionColour_ChangesNothingElse()
    {
        var recorder = new Recorder { Result = attribute => attribute == WindowsCaptionStyler.DwmwaCaptionColor ? unchecked((int)0x80070057) : 0 };
        var styler = new WindowsCaptionStyler(() => true, () => false, recorder.Set);

        Assert.Equal(CaptionStyleResult.NotSupportedByThisWindowsVersion, styler.Apply(42, Navy, White));
        Assert.Single(recorder.Calls); // no dark-mode flag, no text colour
    }

    [Fact]
    public void HighContrast_IsNeverOverridden()
    {
        var recorder = new Recorder();
        var styler = new WindowsCaptionStyler(() => true, () => true, recorder.Set);

        Assert.Equal(CaptionStyleResult.SkippedForHighContrast, styler.Apply(42, Navy, White));
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public void OffWindows_OrWithoutAHandle_NothingIsCalled()
    {
        var recorder = new Recorder();

        Assert.Equal(CaptionStyleResult.NotSupportedOnThisPlatform, new WindowsCaptionStyler(() => false, () => false, recorder.Set).Apply(42, Navy, White));
        Assert.Equal(CaptionStyleResult.Failed, new WindowsCaptionStyler(() => true, () => false, recorder.Set).Apply(0, Navy, White));
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public void AMissingDwmLibrary_IsReportedAsFailed_NotThrown()
    {
        var styler = new WindowsCaptionStyler(() => true, () => false, (_, _, _) => throw new DllNotFoundException("dwmapi.dll"));

        Assert.Equal(CaptionStyleResult.Failed, styler.Apply(42, Navy, White));
    }

    [Fact]
    public void TheDefaultConstructor_OnThisHost_NeverThrows()
    {
        var expected = OperatingSystem.IsWindows() ? CaptionStyleResult.Failed : CaptionStyleResult.NotSupportedOnThisPlatform;
        Assert.Equal(expected, new WindowsCaptionStyler().Apply(0, Navy, White));
    }

    [Theory]
    [InlineData("#1B3553", 0x1B, 0x35, 0x53)]
    [InlineData("#FF1B3553", 0x1B, 0x35, 0x53)]
    [InlineData("FFFFFF", 0xFF, 0xFF, 0xFF)]
    public void CaptionColor_ParsesHex(string hex, int r, int g, int b) =>
        Assert.Equal(new CaptionColor((byte)r, (byte)g, (byte)b), CaptionColor.Parse(hex));

    [Fact]
    public void CaptionColor_RejectsMalformedHex() => Assert.Throws<FormatException>(() => CaptionColor.Parse("#123"));
}
