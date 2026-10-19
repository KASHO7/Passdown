using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 22B: the Fury banner and the app-shell section navigation (Views/MainWindow.axaml plus the
/// shell section of Themes/PassdownStyles.axaml). Markup, resource and asset assertions only — no real
/// Window and no brush color reads, so this class runs in the normal mixed Ui partition. The realized
/// geometry, keyboard and live theme-switch checks are in <see cref="Phase15ERealWindowLayoutTests"/>
/// (a run-alone real-Window class).
/// </summary>
public sealed partial class Phase22BShellNavigationTests
{
    private const string BannerAvaresUri = "avares://Carowinds.Passdown.Desktop/Assets/Branding/fury325-banner.jpg";

    // (visible label, command, selection flag, accessible name) — the established destinations, in
    // order. Phase 22C shortened the visible labels; Settings is a gear icon (no text label). Every
    // accessible name still contains its visible label.
    public static readonly (string? Label, string Command, string Flag, string AccessibleName)[] Destinations =
    [
        ("Dashboard", "ShowDashboardSectionCommand", "IsDashboardSectionSelected", "Dashboard section"),
        ("Calendar", "ShowOperationsCalendarSectionCommand", "IsOperationsCalendarSectionSelected", "Operations Calendar section"),
        ("Passdown", "ShowAssociateSectionCommand", "IsAssociateSectionSelected", "Associate Passdown section"),
        ("Notes", "ShowOperationalNotesSectionCommand", "IsOperationalNotesSectionSelected", "Operational Notes section"),
        ("History", "ShowHistorySectionCommand", "IsHistorySectionSelected", "History section"),
        (null, "ShowSettingsSectionCommand", "IsSettingsSectionSelected", "Settings"),
    ];

    private static string MainWindowMarkup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml"));

    private static string StylesMarkup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml"));

    private static string TokensMarkup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));

    private static List<string> NavigationButtons() =>
        NavButton().Matches(MainWindowMarkup).Select(m => m.Value).ToList();

    // ------------------------------------------------------------------ destinations

    [Fact]
    public void Navigation_KeepsExactlyTheEstablishedDestinations_InOrder_WithNoDuplicates()
    {
        var buttons = NavigationButtons();
        Assert.Equal(Destinations.Length, buttons.Count);
        var commands = buttons.Select(b => Attribute(b, "Command")).ToList();
        Assert.Equal(Destinations.Select(d => $"{{Binding {d.Command}}}"), commands);
        Assert.Equal(commands.Count, commands.Distinct().Count());

        // Phase 22C: the shortened visible labels, in order; Settings has no text label (gear icon).
        var labels = buttons.Take(5).Select(b => Attribute(b, "Content")).ToList();
        Assert.Equal(["Dashboard", "Calendar", "Passdown", "Notes", "History"], labels);
        Assert.DoesNotMatch(new Regex("\\sContent=\""), buttons[5]);
        foreach (var retired in new[] { "Content=\"Operations Calendar\"", "Content=\"Associate Passdown\"", "Content=\"Operational Notes\"", "Content=\"Settings\"" })
        {
            Assert.DoesNotContain(retired, MainWindowMarkup);
        }
    }

    [Fact]
    public void EveryDestination_RoutesThroughItsOwnCommand_AndShowsItsOwnSelectionFlag()
    {
        var buttons = NavigationButtons();
        for (var i = 0; i < Destinations.Length; i++)
        {
            var (label, command, flag, accessibleName) = Destinations[i];
            Assert.Equal($"{{Binding {command}}}", Attribute(buttons[i], "Command"));
            Assert.Equal($"{{Binding {flag}}}", Attribute(buttons[i], "Classes.active"));
            Assert.StartsWith("appSectionItem", Attribute(buttons[i], "Classes"));
            Assert.Equal(accessibleName, Attribute(buttons[i], "AutomationProperties.Name"));
            if (label is not null)
            {
                Assert.Contains(label, accessibleName); // WCAG 2.5.3: the accessible name contains the visible label
            }
        }

        // Each selection flag drives exactly one item, so at most one item can ever be active.
        foreach (var (_, _, flag, _) in Destinations)
        {
            Assert.Single(Regex.Matches(MainWindowMarkup, $"Classes.active=\"\\{{Binding {flag}\\}}\""));
        }
    }

    [Fact]
    public void SettingsGear_IsATrailingAccessibleIconItem_UsingTheBuiltInPathIcon()
    {
        var settings = NavigationButtons()[^1];
        Assert.Contains("x:Name=\"SettingsNavigationItem\"", settings);
        Assert.Equal("appSectionItem iconItem", Attribute(settings, "Classes"));
        Assert.Equal("Settings", Attribute(settings, "AutomationProperties.Name"));
        Assert.Equal("Settings", Attribute(settings, "ToolTip.Tip"));
        Assert.Equal("1", Attribute(settings, "Grid.Column")); // the trailing Auto column, right edge of the strip

        var markup = MainWindowMarkup.Replace("\r\n", "\n");
        var element = markup[markup.IndexOf("<Button x:Name=\"SettingsNavigationItem\"", StringComparison.Ordinal)..];
        element = element[..(element.IndexOf("</Button>", StringComparison.Ordinal))];
        Assert.Contains("<PathIcon Data=\"{StaticResource PassdownSettingsIconGeometry}\" AutomationProperties.AccessibilityView=\"Raw\" />", element);
        Assert.Contains("<StreamGeometry x:Key=\"PassdownSettingsIconGeometry\">", TokensMarkup);

        // The icon variant keeps every appSectionItem state; it only drops the string label template.
        var section = ShellStylesSection();
        var icon = Block(section, "Button.appSectionItem.iconItem");
        Assert.Contains("<Setter Property=\"ContentTemplate\" Value=\"{x:Null}\" />", icon);
        var padding = Regex.Match(icon, "Property=\"Padding\" Value=\"(\\d+),(\\d+)\"");
        Assert.True(padding.Success);
        // 16px icon + vertical padding stays within a text item's height (8+8 padding + a 14px label line),
        // so the gear never makes the strip taller.
        Assert.True(16 + 2 * int.Parse(padding.Groups[2].Value, CultureInfo.InvariantCulture) <= 16 + 16, "gear item taller than a text item");
        Assert.Contains("<Setter Property=\"Width\" Value=\"{DynamicResource PassdownNavigationIconSize}\" />", Block(section, "Button.appSectionItem.iconItem PathIcon"));
    }

    [Fact]
    public void PageHosting_IsUnchanged_EverySectionHostedOnceAndBoundToItsFlag()
    {
        foreach (var (view, flag) in new[]
        {
            ("DashboardView", "IsDashboardSectionSelected"), ("OperationsCalendarView", "IsOperationsCalendarSectionSelected"),
            ("AssociateView", "IsAssociateSectionSelected"), ("OperationalNotesView", "IsOperationalNotesSectionSelected"),
            ("HistoryView", "IsHistorySectionSelected"), ("PersonalSyncSettingsView", "IsSettingsSectionSelected"),
        })
        {
            Assert.Single(Regex.Matches(MainWindowMarkup, $"<views:{view} "));
            Assert.Contains($"<Border IsVisible=\"{{Binding {flag}}}\">\n                <views:{view} ", MainWindowMarkup.Replace("\r\n", "\n"));
        }
    }

    [Fact]
    public void Navigation_IsAWrappingLandmark_SoNarrowWindowsKeepEveryDestinationReachable()
    {
        // Phase 22C: the landmark is the Grid holding the wrapping label row and the trailing gear.
        Assert.Matches(new Regex("<Grid x:Name=\"AppNavigationLayout\" ColumnDefinitions=\"\\*,Auto\"\\s+AutomationProperties.Name=\"Application sections\" AutomationProperties.LandmarkType=\"Navigation\">"), MainWindowMarkup);
        Assert.Contains("<WrapPanel x:Name=\"AppNavigationItems\" Grid.Column=\"0\" Orientation=\"Horizontal\">", MainWindowMarkup);
        Assert.DoesNotContain("ScrollViewer", MainWindowMarkup[..MainWindowMarkup.IndexOf("<views:", StringComparison.Ordinal)]);
    }

    // ------------------------------------------------------------------ banner

    [Fact]
    public void Banner_ShowsOneWordmark_AndNoStackedDuplicateTitle()
    {
        Assert.Single(Regex.Matches(MainWindowMarkup, "Text=\"CAROWINDS OPERATIONS\""));
        Assert.DoesNotContain("Text=\"CAROWINDS\"", MainWindowMarkup);
        Assert.DoesNotContain("Text=\"OPERATIONS\"", MainWindowMarkup);
        Assert.Contains("<TextBlock x:Name=\"BrandWordmark\" Classes=\"brandWordmark\"", MainWindowMarkup);
        Assert.Contains("<Border x:Name=\"BrandBanner\" DockPanel.Dock=\"Top\" Classes=\"brandBanner\">", MainWindowMarkup);
    }

    [Fact]
    public void BannerImage_IsABundledRelativeResource_NeverAUrlOrDeveloperPath()
    {
        var image = Regex.Match(MainWindowMarkup, "<Image x:Name=\"BrandBannerImage\"[^>]*/>", RegexOptions.Singleline).Value;
        Assert.Contains("Source=\"/Assets/Branding/fury325-banner.jpg\"", image);
        Assert.Contains("Stretch=\"UniformToFill\"", image); // fills the width without distortion
        foreach (var forbidden in new[] { "http:", "https:", "file:", "/Users/", "/home/", ":\\", "OneDrive" })
        {
            Assert.DoesNotContain(forbidden, image, StringComparison.OrdinalIgnoreCase);
        }

        var withoutNamespaces = Regex.Replace(MainWindowMarkup, "xmlns(:\\w+)?=\"[^\"]*\"", string.Empty);
        Assert.DoesNotMatch(new Regex("https?://", RegexOptions.IgnoreCase), withoutNamespaces);
        Assert.Contains("<AvaloniaResource Include=\"Assets\\**\" />",
            File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj")));
    }

    [Fact]
    public void BannerImage_IsEmbeddedInTheDesktopAssembly()
    {
        Assert.True(AssetLoader.Exists(new Uri(BannerAvaresUri)), $"{BannerAvaresUri} is not an embedded Avalonia resource");
        using var stream = AssetLoader.Open(new Uri(BannerAvaresUri));
        Assert.Equal(0xFF, stream.ReadByte()); // JPEG SOI marker
        Assert.Equal(0xD8, stream.ReadByte());
    }

    [Fact]
    public void BannerAsset_IsASingleReasonablySizedPanoramicJpeg()
    {
        var path = MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Assets/Branding/fury325-banner.jpg");
        var bytes = File.ReadAllBytes(path);
        Assert.InRange(bytes.Length, 20_000, 300_000);

        var (width, height) = JpegSize(bytes);
        Assert.Equal(1920, width);
        Assert.True(width / (double)height >= 4, $"banner asset {width}x{height} is not a panoramic crop");

        // No duplicate copies of the photograph anywhere under src/.
        var src = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(path))))!;
        var copies = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f).Contains("fury", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(copies);
    }

    [Fact]
    public void BannerPhotograph_IsDecorative_HiddenFromScreenReadersAndHitTesting()
    {
        var image = Regex.Match(MainWindowMarkup, "<Image x:Name=\"BrandBannerImage\"[^>]*/>", RegexOptions.Singleline).Value;
        Assert.Contains("AutomationProperties.AccessibilityView=\"Raw\"", image);
        Assert.Contains("IsHitTestVisible=\"False\"", image);
        Assert.Contains("AutomationProperties.Name=\"Carowinds Operations\"", MainWindowMarkup);
    }

    [Fact]
    public void BannerTokens_KeepTheHeaderCompact()
    {
        Assert.InRange(Resolve<double>("PassdownBannerHeight"), 72, 84);
        Assert.InRange(Resolve<double>("PassdownFontSizeWordmark"), 14, 18);
        var item = Resolve<Avalonia.Thickness>("PassdownNavigationItemPadding");
        Assert.InRange(item.Top + item.Bottom, 12, 20); // compact, still a comfortable hit target
        Resolve<Avalonia.Thickness>("PassdownNavigationStripPadding");
        Assert.IsAssignableFrom<IBrush>(Resolve<object>("PassdownBannerOverlayBrush"));
    }

    // WCAG relative luminance. The overlay must guarantee white wordmark text ≥ 4.5:1 even when the
    // photo pixel behind it is pure white, so legibility never depends on which part of the photo shows.
    [Fact]
    public void BannerOverlay_GuaranteesWordmarkContrast_OverTheBrightestPossiblePhotoPixel()
    {
        var brush = Regex.Match(TokensMarkup, "<LinearGradientBrush x:Key=\"PassdownBannerOverlayBrush\" StartPoint=\"([^\"]+)\" EndPoint=\"([^\"]+)\">(.*?)</LinearGradientBrush>", RegexOptions.Singleline);
        Assert.True(brush.Success);
        Assert.DoesNotContain("%", brush.Groups[2].Value); // absolute: same scrim behind the text at every width
        var span = double.Parse(brush.Groups[2].Value.Split(',')[0], CultureInfo.InvariantCulture);
        var stops = Regex.Matches(brush.Groups[3].Value, "Offset=\"([0-9.]+)\" Color=\"(#[0-9A-Fa-f]{8})\"")
            .Select(m => (Offset: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Color: Color.Parse(m.Groups[2].Value)))
            .ToList();
        Assert.True(stops.Count >= 2);

        // Wordmark: 24px margin + 18px mark + 12px spacing + ~245px of 16px Bold letter-spaced text.
        const double wordmarkEnd = 300;
        foreach (var x in new[] { 0d, wordmarkEnd / 2, wordmarkEnd })
        {
            var (alpha, overlay) = Sample(stops, x / span);
            var composite = Color.FromRgb(Mix(overlay.R, alpha), Mix(overlay.G, alpha), Mix(overlay.B, alpha));
            var ratio = Contrast(Colors.White, composite);
            Assert.True(ratio >= 4.5, $"white wordmark over a white photo pixel at x={x} is {ratio:F2}:1");
        }

        // The right side stays light enough for the ride to remain visible.
        Assert.True(stops[^1].Color.A / 255.0 <= 0.6, "the overlay should not black out the photograph");

        static byte Mix(byte overlayChannel, double alpha) => (byte)Math.Round(alpha * overlayChannel + (1 - alpha) * 255);
    }

    private static (double Alpha, Color Color) Sample(List<(double Offset, Color Color)> stops, double t)
    {
        t = Math.Clamp(t, 0, 1);
        for (var i = 1; i < stops.Count; i++)
        {
            if (t <= stops[i].Offset)
            {
                var (o0, c0) = stops[i - 1];
                var (o1, c1) = stops[i];
                var f = (t - o0) / (o1 - o0);
                return ((c0.A + (c1.A - c0.A) * f) / 255.0, c0);
            }
        }

        return (stops[^1].Color.A / 255.0, stops[^1].Color);
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // ------------------------------------------------------------------ shared styling

    private static List<string> SharedSelectors()
    {
        var styles = Avalonia.Application.Current!.Styles[1] switch
        {
            StyleInclude include => Assert.IsAssignableFrom<Styles>(include.Loaded),
            Styles s => s,
            var other => throw new Xunit.Sdk.XunitException($"App.Styles[1] is {other.GetType()}"),
        };
        return styles.OfType<Style>().Select(s => s.Selector!.ToString()).ToList();
    }

    [Fact]
    public void ShellStyles_LiveInTheSharedStyles_NotInTheWindow()
    {
        var selectors = SharedSelectors();
        foreach (var expected in new[]
        {
            "Border.brandBanner", "TextBlock.brandWordmark", "Border.appNavigation",
            "Button.appSectionItem", "Button.appSectionItem.active", "Button.appSectionItem:pressed",
            "Button.appSectionItem:pointerover /template/ ContentPresenter#PART_ContentPresenter",
            "Button.appSectionItem:pressed /template/ ContentPresenter#PART_ContentPresenter",
            "Button.appSectionItem:disabled /template/ ContentPresenter#PART_ContentPresenter",
        })
        {
            Assert.Contains(expected, selectors);
        }

        Assert.DoesNotContain("<Window.Styles>", MainWindowMarkup);
        Assert.DoesNotContain("<Style Selector=", MainWindowMarkup);
    }

    private static string ShellStylesSection()
    {
        var start = StylesMarkup.IndexOf("App shell: Fury banner and section navigation", StringComparison.Ordinal);
        var end = StylesMarkup.IndexOf("Lists, popups and loading feedback", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        return StylesMarkup[start..end];
    }

    [Fact]
    public void ShellStyles_UseOnlySemanticTokens_NoLiteralColors()
    {
        var section = ShellStylesSection();
        Assert.DoesNotMatch(new Regex("Value=\"#[0-9A-Fa-f]{3,8}\""), section);
        foreach (var token in new[]
        {
            "PassdownSurfaceBrush", "PassdownDividerBrush", "PassdownTextSecondaryBrush", "PassdownTextPrimaryBrush",
            "PassdownAccentForegroundBrush", "PassdownHoverBackgroundBrush", "PassdownPressedBackgroundBrush",
            "PassdownTextDisabledBrush", "PassdownBannerHeight", "PassdownBrandDarkBrush", "PassdownHeaderForegroundBrush",
            "PassdownNavigationItemPadding", "PassdownNavigationStripPadding", "PassdownFontSizeBody",
        })
        {
            Assert.Contains($"{{DynamicResource {token}}}", section);
        }

        // Accent fills (PassdownAccentBrush) are for primary actions, never a navigation surface.
        Assert.DoesNotContain("PassdownAccentBrush}", section);
        Assert.DoesNotContain("PassdownSelectedBackgroundBrush", section);
    }

    [Fact]
    public void ActiveItem_IsIndicatedByShapeWeightAndScreenReaderStatus_NotColorAlone()
    {
        var section = ShellStylesSection();
        var item = Block(section, "Button.appSectionItem");
        var active = Block(section, "Button.appSectionItem.active");

        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"0,0,0,2\" />", item); // underline slot on every item
        Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"Transparent\" />", item);
        Assert.Contains("<Setter Property=\"FontWeight\" Value=\"Normal\" />", item);
        Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"{DynamicResource PassdownAccentForegroundBrush}\" />", active);
        Assert.Contains("<Setter Property=\"FontWeight\" Value=\"SemiBold\" />", active);
        Assert.Contains("<Setter Property=\"AutomationProperties.ItemStatus\" Value=\"Current page\" />", active);

        // Hover never draws the underline, so hovered and active stay distinguishable; active keeps it on hover.
        var hover = Block(section, "Button.appSectionItem:pointerover /template/ ContentPresenter#PART_ContentPresenter");
        Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"Transparent\" />", hover);
        Assert.Contains("Button.appSectionItem.active:pointerover /template/ ContentPresenter#PART_ContentPresenter", section);
    }

    [Fact]
    public void ItemLabels_ReserveTheSemiBoldWidth_SoSelectionNeverMovesNeighbors()
    {
        var item = Block(ShellStylesSection(), "Button.appSectionItem");
        Assert.Contains("<TextBlock Text=\"{Binding}\" FontWeight=\"SemiBold\" Opacity=\"0\"", item);
        Assert.Contains("AutomationProperties.AccessibilityView=\"Raw\"", item); // the width placeholder is not read twice
    }

    [Fact]
    public void ShellFocus_StaysFluentsKeyboardAdorner_OnTheSharedFocusToken()
    {
        // No shell style replaces a template or touches FocusAdorner, so :focus-visible keeps Fluent's
        // adorner, whose stroke Phase 22A mapped to PassdownFocusBorderBrush.
        var section = ShellStylesSection();
        Assert.DoesNotContain("FocusAdorner", section);
        Assert.DoesNotContain("\"Template\"", section);
        Assert.Contains("<SolidColorBrush x:Key=\"SystemControlFocusVisualPrimaryBrush\"", TokensMarkup);
    }

    [Theory]
    [InlineData("PassdownSurfaceBrush")]
    [InlineData("PassdownTextSecondaryBrush")]
    [InlineData("PassdownTextPrimaryBrush")]
    [InlineData("PassdownAccentForegroundBrush")]
    [InlineData("PassdownHoverBackgroundBrush")]
    [InlineData("PassdownDividerBrush")]
    public void NavigationBrushes_ResolveSeparatelyPerTheme_SoALiveSwitchRestylesTheStrip(string key) =>
        Assert.NotSame(Resolve<IBrush>(key, ThemeVariant.Light), Resolve<IBrush>(key, ThemeVariant.Dark));

    [Theory]
    [InlineData("PassdownBannerOverlayBrush")]
    [InlineData("PassdownBrandDarkBrush")]
    [InlineData("PassdownHeaderForegroundBrush")]
    public void BannerBrushes_AreThemeInvariant_TheBannerStaysDarkInBothThemes(string key)
    {
        Assert.Same(Resolve<IBrush>(key, ThemeVariant.Light), Resolve<IBrush>(key, ThemeVariant.Dark));
        var darkStart = TokensMarkup.IndexOf("<ResourceDictionary x:Key=\"Dark\">", StringComparison.Ordinal);
        Assert.DoesNotContain($"x:Key=\"{key}\"", TokensMarkup[darkStart..]);
    }

    // ------------------------------------------------------------------ helpers

    private static T Resolve<T>(string key, ThemeVariant? variant = null)
    {
        Assert.True(Avalonia.Application.Current!.TryGetResource(key, variant ?? ThemeVariant.Light, out var value), $"resource '{key}' missing");
        return Assert.IsAssignableFrom<T>(value);
    }

    private static string Attribute(string element, string name)
    {
        var m = Regex.Match(element, $"\\s{Regex.Escape(name)}=\"([^\"]*)\"");
        Assert.True(m.Success, $"attribute {name} missing in {element}");
        return m.Groups[1].Value;
    }

    private static string Block(string section, string selector)
    {
        var start = section.IndexOf($"<Style Selector=\"{selector}\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"style {selector} missing");
        return section[start..(section.IndexOf("</Style>", start, StringComparison.Ordinal))];
    }

    private static (int Width, int Height) JpegSize(byte[] bytes)
    {
        for (var i = 2; i + 9 < bytes.Length;)
        {
            if (bytes[i] != 0xFF) { i++; continue; }
            var marker = bytes[i + 1];
            var length = (bytes[i + 2] << 8) | bytes[i + 3];
            if (marker is >= 0xC0 and <= 0xC3)
            {
                return ((bytes[i + 7] << 8) | bytes[i + 8], (bytes[i + 5] << 8) | bytes[i + 6]);
            }

            i += 2 + length;
        }

        throw new Xunit.Sdk.XunitException("no JPEG frame header");
    }

    [GeneratedRegex("<Button [^>]*Classes=\"appSectionItem[^\"]*\"[^>]*>", RegexOptions.Singleline)]
    private static partial Regex NavButton();
}
