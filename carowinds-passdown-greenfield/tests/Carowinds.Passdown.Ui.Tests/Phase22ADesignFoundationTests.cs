using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Carowinds.Passdown.Desktop.Converters;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 22A: the shared design foundation (Themes/PassdownTokens.axaml + Themes/PassdownStyles.axaml).
/// Resource/style-object and markup assertions only — no pixel comparisons and no real Window, so
/// this class runs in the normal mixed Ui partition.
/// </summary>
public sealed partial class Phase22ADesignFoundationTests
{
    private static readonly string[] ColorTokens =
    [
        "PassdownAppBackgroundBrush", "PassdownSurfaceBrush", "PassdownSurfaceSecondaryBrush", "PassdownSurfaceElevatedBrush",
        "PassdownPopupSurfaceBrush", "PassdownOverlayScrimBrush",
        "PassdownTextPrimaryBrush", "PassdownTextSecondaryBrush", "PassdownTextMutedBrush", "PassdownTextDisabledBrush", "PassdownTextOnAccentBrush",
        "PassdownBorderBrush", "PassdownDividerBrush", "PassdownBorderStrongBrush", "PassdownFocusBorderBrush",
        "PassdownAccentBrush", "PassdownAccentHoverBrush", "PassdownAccentPressedBrush", "PassdownAccentForegroundBrush",
        "PassdownSelectedBackgroundBrush", "PassdownSelectedForegroundBrush", "PassdownHoverBackgroundBrush", "PassdownPressedBackgroundBrush",
        "PassdownChipBrush", "PassdownChipAccentBrush", "PassdownChipAccentForegroundBrush",
        "PassdownSuccessBrush", "PassdownSuccessBackgroundBrush", "PassdownWarningBrush", "PassdownWarningBackgroundBrush",
        "PassdownErrorBrush", "PassdownErrorBackgroundBrush", "PassdownInfoBrush", "PassdownInfoBackgroundBrush",
        "PassdownStaleBrush", "PassdownStaleBackgroundBrush", "PassdownNotPublishedBrush", "PassdownUnavailableBrush",
        "PassdownNeutralStatusBackgroundBrush",
        // Fluent overrides that keep popups, inputs and focus on the shared palette.
        "SystemControlFocusVisualPrimaryBrush", "FlyoutPresenterBackground", "MenuFlyoutPresenterBackground",
        "ComboBoxDropDownBackground", "ToolTipBackground", "TextControlBorderBrush", "TextControlPlaceholderForeground",
        // Phase 22B app shell (theme-invariant brand tokens).
        "PassdownBrandDarkBrush", "PassdownHeaderForegroundBrush", "PassdownBannerOverlayBrush",
    ];

    private static ThemeVariant Variant(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    private static T Resolve<T>(string key, ThemeVariant variant)
    {
        Assert.True(Avalonia.Application.Current!.TryGetResource(key, variant, out var value), $"resource '{key}' missing for {variant}");
        return Assert.IsType<T>(value, exactMatch: false);
    }

    // Token colors are read from the XAML source rather than from the resolved SolidColorBrush: a
    // brush is dispatcher-owned, and this class runs in the mixed Ui partition where the test thread
    // is not guaranteed to be the dispatcher thread (see docs/release-manifest.md).
    private static readonly Lazy<Dictionary<string, Dictionary<string, Color>>> TokenColors = new(() =>
    {
        var xaml = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));
        var result = new Dictionary<string, Dictionary<string, Color>>();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var start = xaml.IndexOf($"<ResourceDictionary x:Key=\"{theme}\">", StringComparison.Ordinal);
            var end = xaml.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
            result[theme] = BrushColor().Matches(xaml[start..end])
                .ToDictionary(m => m.Groups[1].Value, m => Color.Parse(m.Groups[2].Value));
        }

        return result;
    });

    private static Color ColorOf(string key, ThemeVariant variant)
    {
        var theme = variant == ThemeVariant.Dark ? "Dark" : "Light";
        Assert.True(TokenColors.Value[theme].TryGetValue(key, out var color), $"{theme} token '{key}' has no literal color");
        return color;
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void EverySemanticColorToken_ResolvesToABrush_InBothThemes(string theme)
    {
        foreach (var key in ColorTokens)
        {
            Resolve<IBrush>(key, Variant(theme));
        }
    }

    [Fact]
    public void TypographySpacingAndRadiusTokens_Resolve()
    {
        foreach (var key in new[] { "PassdownFontSizePageTitle", "PassdownFontSizePrimaryValue", "PassdownFontSizeStatValue", "PassdownFontSizeSectionHeading", "PassdownFontSizeSubsection", "PassdownFontSizeBody", "PassdownFontSizeMetadata", "PassdownFontSizeCaption", "PassdownFontSizeTableHeader", "PassdownFontSizeWordmark", "PassdownBannerHeight", "PassdownSpace1", "PassdownSpace2", "PassdownSpace3", "PassdownSpace4", "PassdownSpace6" })
        {
            Assert.True(Resolve<double>(key, ThemeVariant.Light) > 0, key);
        }

        foreach (var key in new[] { "PassdownPagePadding", "PassdownPageHeaderPadding", "PassdownPanelPadding", "PassdownInsetPadding", "PassdownButtonPadding", "PassdownCompactButtonPadding", "PassdownChipPadding", "PassdownRowPadding", "PassdownNavigationStripPadding", "PassdownNavigationItemPadding" })
        {
            Resolve<Thickness>(key, ThemeVariant.Light);
        }

        var radii = new[] { "PassdownRadiusSmall", "PassdownRadiusControl", "PassdownRadiusCard", "PassdownRadiusPopup" }
            .Select(k => Resolve<CornerRadius>(k, ThemeVariant.Light).TopLeft).ToList();
        Assert.Equal(radii.OrderBy(r => r), radii); // a strictly increasing, restrained hierarchy
        Assert.True(radii[^1] <= 8, "no surface should be more rounded than a popup");
    }

    [Theory]
    [InlineData("PassdownAppBackgroundBrush")]
    [InlineData("PassdownSurfaceBrush")]
    [InlineData("PassdownTextPrimaryBrush")]
    [InlineData("PassdownTextSecondaryBrush")]
    [InlineData("PassdownBorderBrush")]
    [InlineData("PassdownAccentForegroundBrush")]
    [InlineData("PassdownSelectedBackgroundBrush")]
    [InlineData("PassdownErrorBrush")]
    public void DarkTheme_IsDesignedSeparately_NotTheLightValue(string key) =>
        Assert.NotEqual(ColorOf(key, ThemeVariant.Light), ColorOf(key, ThemeVariant.Dark));

    [Fact]
    public void DarkBackgrounds_AreCharcoalNotPureBlack_AndSurfacesAreDistinguishable()
    {
        var app = ColorOf("PassdownAppBackgroundBrush", ThemeVariant.Dark);
        var surface = ColorOf("PassdownSurfaceBrush", ThemeVariant.Dark);
        Assert.NotEqual(Color.FromRgb(0, 0, 0), app);
        Assert.NotEqual(app, surface);
        Assert.NotEqual(ColorOf("PassdownAppBackgroundBrush", ThemeVariant.Light), ColorOf("PassdownSurfaceBrush", ThemeVariant.Light));
    }

    // WCAG relative-luminance contrast, measured on the token values themselves (not a claim of
    // whole-screen conformance — see docs/phase22a-design-foundation.md §14).
    [Theory]
    [InlineData("Light", "PassdownTextPrimaryBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Light", "PassdownTextSecondaryBrush", "PassdownAppBackgroundBrush", 4.5)]
    [InlineData("Light", "PassdownTextMutedBrush", "PassdownAppBackgroundBrush", 4.5)]
    [InlineData("Light", "PassdownTextOnAccentBrush", "PassdownAccentBrush", 4.5)]
    [InlineData("Light", "PassdownTextOnAccentBrush", "PassdownAccentHoverBrush", 4.5)]
    [InlineData("Light", "PassdownAccentForegroundBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Light", "PassdownSelectedForegroundBrush", "PassdownSelectedBackgroundBrush", 4.5)]
    [InlineData("Light", "PassdownErrorBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Light", "PassdownWarningBrush", "PassdownWarningBackgroundBrush", 4.5)]
    [InlineData("Light", "PassdownStaleBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Light", "PassdownSuccessBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Light", "PassdownChipAccentForegroundBrush", "PassdownChipAccentBrush", 4.5)]
    [InlineData("Light", "PassdownBorderStrongBrush", "PassdownAppBackgroundBrush", 3.0)]
    [InlineData("Light", "PassdownFocusBorderBrush", "PassdownSurfaceBrush", 3.0)]
    [InlineData("Dark", "PassdownTextPrimaryBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Dark", "PassdownTextSecondaryBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Dark", "PassdownTextMutedBrush", "PassdownPopupSurfaceBrush", 4.5)]
    [InlineData("Dark", "PassdownTextOnAccentBrush", "PassdownAccentBrush", 4.5)]
    [InlineData("Dark", "PassdownTextOnAccentBrush", "PassdownAccentHoverBrush", 4.5)]
    [InlineData("Dark", "PassdownAccentForegroundBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Dark", "PassdownSelectedForegroundBrush", "PassdownSelectedBackgroundBrush", 4.5)]
    [InlineData("Dark", "PassdownErrorBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Dark", "PassdownWarningBrush", "PassdownWarningBackgroundBrush", 4.5)]
    [InlineData("Dark", "PassdownStaleBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Dark", "PassdownSuccessBrush", "PassdownSurfaceBrush", 4.5)]
    [InlineData("Dark", "PassdownChipAccentForegroundBrush", "PassdownChipAccentBrush", 4.5)]
    [InlineData("Dark", "PassdownBorderStrongBrush", "PassdownSurfaceBrush", 3.0)]
    [InlineData("Dark", "PassdownFocusBorderBrush", "PassdownSurfaceBrush", 3.0)]
    public void KeyTokenPairs_MeetContrastTargets(string theme, string foreground, string background, double minimum)
    {
        var ratio = Contrast(ColorOf(foreground, Variant(theme)), ColorOf(background, Variant(theme)));
        Assert.True(ratio >= minimum, $"{theme}: {foreground} on {background} is {ratio:F2}:1 (< {minimum}:1)");
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Theory]
    [InlineData("PassdownSurfaceBrush")]
    [InlineData("PassdownTextPrimaryBrush")]
    [InlineData("PassdownAccentBrush")]
    [InlineData("FlyoutPresenterBackground")]
    public void ThemeSwitch_ResolvesADifferentBrushPerVariant(string key) =>
        // What a live Light/Dark switch re-resolves through every DynamicResource reference.
        Assert.NotSame(Resolve<IBrush>(key, ThemeVariant.Light), Resolve<IBrush>(key, ThemeVariant.Dark));

    [Fact]
    public void ThemeDependentTokens_AreOnlyReferencedDynamically_SoLiveSwitchingRestyles()
    {
        var tokens = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));
        var darkStart = tokens.IndexOf("<ResourceDictionary x:Key=\"Dark\">", StringComparison.Ordinal);
        var themed = BrushColor().Matches(tokens[darkStart..]).Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Contains("PassdownSurfaceBrush", themed);

        var files = new[] { "Themes/PassdownStyles.axaml", "App.axaml" }
            .Concat(new[] { "MainWindow", "DashboardView", "OperationsCalendarView", "AssociateView", "OperationalNotesView", "HistoryView", "PersonalSyncSettingsView", "PriceHistoryView" }.Select(v => $"Views/{v}.axaml"));
        foreach (var file in files)
        {
            var markup = File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/{file}"));
            foreach (Match m in StaticReference().Matches(markup))
            {
                Assert.DoesNotContain(m.Groups[1].Value, themed);
            }
        }

        var app = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/App.axaml"));
        Assert.Contains("RequestedThemeVariant=\"Default\"", app); // follows the OS light/dark setting
    }

    // The XAML compiler inlines the avares StyleInclude, so App.Styles[1] is the loaded Styles itself.
    private static Styles SharedStyles()
    {
        var appStyles = Avalonia.Application.Current!.Styles;
        return appStyles[1] switch
        {
            StyleInclude include => Assert.IsAssignableFrom<Styles>(include.Loaded),
            Styles styles => styles,
            var other => throw new Xunit.Sdk.XunitException($"App.Styles[1] is {other.GetType()}, expected the Phase 22A shared styles"),
        };
    }

    private static List<string> SharedSelectors() =>
        SharedStyles().OfType<Style>().Select(s => s.Selector!.ToString()).ToList();

    [Fact]
    public void SharedStyles_AreLoadedAfterFluent_WithButtonInputPopupAndStatusStyles()
    {
        var appStyles = Avalonia.Application.Current!.Styles.ToList();
        Assert.Equal(2, appStyles.Count);
        Assert.IsType<Avalonia.Themes.Fluent.FluentTheme>(appStyles[0]);

        var selectors = SharedSelectors();
        foreach (var expected in new[]
        {
            "Window", "TextBlock.pageTitle", "TextBlock.sectionTitle", "TextBlock.muted", "TextBlock.fieldError",
            "Border.panel", "Border.heroPanel", "Border.chip", "Border.statusBadge", "Border.statusBadge.error",
            "Button.primaryAction", "Button.secondaryAction", "Button.warningAction",
            "Button.primaryAction:pointerover /template/ ContentPresenter#PART_ContentPresenter",
            "Button.secondaryAction:pressed /template/ ContentPresenter#PART_ContentPresenter",
            "ListBox.selectableRows ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter",
            "TextBlock.statusStale", "TextBlock.statusNotPublished",
        })
        {
            Assert.Contains(expected, selectors);
        }
    }

    [Fact]
    public void SharedStyles_OnlyTargetNamedClasses_ExceptTheWindowBackground()
    {
        // Broad type-only selectors (e.g. "Button", "TextBox") would restyle every control in the app.
        var unscoped = SharedSelectors().SelectMany(s => s.Split(','))
            .Select(s => s.Trim()).Where(s => !s.Contains('.')).ToList();
        Assert.Equal(["Window"], unscoped);
    }

    [Theory]
    [InlineData("MainWindow")]
    [InlineData("DashboardView")]
    [InlineData("OperationsCalendarView")]
    [InlineData("AssociateView")]
    [InlineData("OperationalNotesView")]
    [InlineData("HistoryView")]
    [InlineData("PersonalSyncSettingsView")]
    [InlineData("PriceHistoryView")]
    public void ViewMarkup_UsesOnlyDefinedTokens_NoRetiredKeys_AndNoLiteralColors(string view)
    {
        var markup = File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml"));
        var tokens = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));

        foreach (Match m in ResourceReference().Matches(markup))
        {
            Assert.Contains($"x:Key=\"{m.Groups[1].Value}\"", tokens);
        }

        foreach (var retired in new[] { "PassdownBandBrush", "PassdownSurfaceTintBrush", "PassdownSubtleSurfaceBrush", "PassdownComparePriceBrush", "TextControlPlaceholderForeground}", "SystemControlBackgroundAltHighBrush" })
        {
            Assert.DoesNotContain(retired, markup);
        }

        Assert.DoesNotMatch(LiteralColor(), markup);
    }

    [Theory]
    [InlineData("MainWindow")]
    [InlineData("DashboardView")]
    [InlineData("OperationsCalendarView")]
    [InlineData("AssociateView")]
    [InlineData("OperationalNotesView")]
    [InlineData("HistoryView")]
    [InlineData("PersonalSyncSettingsView")]
    [InlineData("PriceHistoryView")]
    public void ViewMarkup_DoesNotReDuplicateTheSharedButtonAndSurfaceStyles(string view)
    {
        var markup = File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml"));
        foreach (var shared in new[] { "Button.secondaryAction\"", "Border.panel\"", "Border.chip\"", "TextBlock.muted\"", "TextBlock.sectionTitle\"", "TextBlock.pageTitle\"" })
        {
            Assert.DoesNotContain($"<Style Selector=\"{shared}", markup);
        }
    }

    [Fact]
    public void SelectableLists_OptIntoTheSharedRowStates()
    {
        foreach (var view in new[] { "HistoryView", "OperationalNotesView", "PriceHistoryView" })
        {
            var markup = File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml"));
            Assert.Matches(new Regex("<ListBox [^>]*Classes=\"selectableRows\""), markup);
        }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ParkHoursDot_UsesTheSharedStatusTokens_ForTheBoundTheme(string theme)
    {
        var converter = ParkHoursBadgeBrushConverter.Instance;
        var variant = Variant(theme);
        object? Dot(ParkHoursBadgeKind kind) => converter.Convert([kind, variant], typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.Same(Resolve<IBrush>("PassdownSuccessBrush", variant), Dot(ParkHoursBadgeKind.Open));
        Assert.Same(Resolve<IBrush>("PassdownUnavailableBrush", variant), Dot(ParkHoursBadgeKind.Closed));
        Assert.Same(Resolve<IBrush>("PassdownWarningBrush", variant), Dot(ParkHoursBadgeKind.ManualOverride));
        Assert.Same(Resolve<IBrush>("PassdownWarningBrush", variant), Dot(ParkHoursBadgeKind.PrivateBuyout));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void SaveStatusChip_UsesTheSharedChipTokens_ForTheBoundTheme(string theme)
    {
        var converter = SaveStatusBrushConverter.Instance;
        var variant = Variant(theme);
        Assert.Same(Resolve<IBrush>("PassdownChipAccentBrush", variant),
            converter.Convert([AssociateAutosaveState.Saved, variant], typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.Same(Resolve<IBrush>("PassdownChipBrush", variant),
            converter.Convert([AssociateAutosaveState.Saving, variant], typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.Same(AvaloniaProperty.UnsetValue,
            converter.Convert([null, variant], typeof(IBrush), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AssociateMarkup_BindsStatusBrushesToTheElementTheme_SoLiveSwitchingUpdatesThem()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));
        Assert.Contains("<Binding Path=\"ActualThemeVariant\" ElementName=\"SaveStatusChip\" />", markup);
        Assert.Contains("<Binding Path=\"ActualThemeVariant\" ElementName=\"ParkHoursStatusDot\" />", markup);
        // Save status text remains the primary signal (never color-only).
        Assert.Contains("Text=\"{Binding SaveStatusText, FallbackValue='Not saved yet'}\"", markup);
    }

    [GeneratedRegex(@"<SolidColorBrush x:Key=""([A-Za-z0-9]+)"" Color=""(#[0-9A-Fa-f]{6,8})""")]
    private static partial Regex BrushColor();

    [GeneratedRegex(@"StaticResource ([A-Za-z0-9]+)")]
    private static partial Regex StaticReference();

    [GeneratedRegex(@"(?:Dynamic|Static)Resource (Passdown[A-Za-z0-9]+)")]
    private static partial Regex ResourceReference();

    [GeneratedRegex("=\"#[0-9A-Fa-f]{3,8}\"")]
    private static partial Regex LiteralColor();
}
