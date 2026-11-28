using Avalonia.Platform;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 34C: <c>Assets/logo.png</c> (the 1254×1254 source art for <c>logo.ico</c>) is no longer embedded in
/// Carowinds.Passdown.Desktop.dll, because nothing loads it at runtime. These checks pin that the icon is
/// still embedded (the retired Phase 22B banner no longer is — Phase 35G), and that the source art stays in the repository.
/// Asset and markup assertions only (no real Window), so this class runs in the mixed Ui partition.
/// </summary>
public sealed class Phase34CEmbeddedAssetTests
{
    private const string DesktopAssets = "avares://Carowinds.Passdown.Desktop/Assets/";

    [Fact]
    public void LogoIco_IsStillAnEmbeddedResource_ForTheWindowIcon()
    {
        var uri = new Uri(DesktopAssets + "logo.ico");
        Assert.True(AssetLoader.Exists(uri), $"{uri} must stay embedded: MainWindow.axaml uses it as the window icon");
        using var stream = AssetLoader.Open(uri);
        Assert.Equal(0x00, stream.ReadByte()); // ICONDIR: reserved (0), type 1 = icon
        Assert.Equal(0x00, stream.ReadByte());
        Assert.Equal(0x01, stream.ReadByte());
    }

    [Fact]
    public void RetiredBrandingBanner_IsNoLongerEmbedded()
    {
        // Phase 35G: the 35A sidebar shell replaced the photo banner and was accepted on Windows; the unused
        // banner was then removed, so only the icon remains embedded from Assets.
        Assert.False(AssetLoader.Exists(new Uri(DesktopAssets + "Branding/fury325-banner.jpg")));
    }

    [Fact]
    public void LogoPngSourceArt_IsNotEmbedded()
    {
        Assert.False(
            AssetLoader.Exists(new Uri(DesktopAssets + "logo.png")),
            "logo.png is source art only; nothing loads it at runtime, so it must not be embedded in Desktop.dll");
    }

    [Fact]
    public void LogoPngSourceArt_StaysInTheRepository_AndNoMarkupOrCodeLoadsIt()
    {
        var png = MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Assets/logo.png");
        var bytes = File.ReadAllBytes(png);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]); // PNG signature

        var desktopRoot = Path.GetDirectoryName(Path.GetDirectoryName(png))!;
        var sources = Directory.EnumerateFiles(desktopRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".axaml", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        foreach (var path in sources)
        {
            Assert.DoesNotContain("logo.png", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DesktopProject_RemovesOnlyLogoPngFromTheResourceGlob()
    {
        var project = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj"));
        Assert.Contains("<AvaloniaResource Include=\"Assets\\**\" />", project);
        Assert.Contains("<AvaloniaResource Remove=\"Assets\\logo.png\" />", project);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(project, "<AvaloniaResource Remove=").Cast<object>());
        Assert.Contains("<ApplicationIcon>Assets\\logo.ico</ApplicationIcon>", project);
    }
}
