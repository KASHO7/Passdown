using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 22B, updated for Phase 35A: the app-shell navigation (Views/MainWindow.axaml plus the shell section of
/// Themes/PassdownStyles.axaml). Phase 35A replaced the Fury photo banner and horizontal strip with a left sidebar
/// and the native title bar; every navigation contract is kept (destinations, order, commands, selection flags,
/// accessible names, one landmark, shared styles only, semantic tokens only, active state by shape + weight +
/// screen-reader status, Fluent focus). Banner-only assertions were replaced by sidebar equivalents — see
/// docs/phase35-unified-ui-modernization.md §14. Markup, resource and asset assertions only (no real Window, no
/// brush colour reads), so this class runs in the mixed Ui partition; realized geometry, keyboard, motion and live
/// theme checks are in <see cref="Phase15ERealWindowLayoutTests"/>.
/// </summary>
public sealed partial class Phase22BShellNavigationTests
{
    private const string BannerAvaresUri = "avares://Carowinds.Passdown.Desktop/Assets/Branding/fury325-banner.jpg";

    // (visible label, command, selection flag, accessible name) — the established destinations, in order.
    // Phase 35A: the sidebar shows a label for every item ("Operational Notes" in full, "Settings" beside the
    // gear). Every accessible name still contains its visible label, or equals it.
    public static readonly (string Label, string Command, string Flag, string AccessibleName)[] Destinations =
    [
        ("Dashboard", "ShowDashboardSectionCommand", "IsDashboardSectionSelected", "Dashboard section"),
        ("Calendar", "ShowOperationsCalendarSectionCommand", "IsOperationsCalendarSectionSelected", "Operations Calendar section"),
        ("Passdown", "ShowAssociateSectionCommand", "IsAssociateSectionSelected", "Associate Passdown section"),
        ("Operational Notes", "ShowOperationalNotesSectionCommand", "IsOperationalNotesSectionSelected", "Operational Notes section"),
        ("History", "ShowHistorySectionCommand", "IsHistorySectionSelected", "History section"),
        ("Settings", "ShowSettingsSectionCommand", "IsSettingsSectionSelected", "Settings"),
    ];

    private static string MainWindowMarkup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml")).Replace("\r\n", "\n");

    private static string StylesMarkup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml"));

    private static string TokensMarkup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));

    private static List<string> NavigationButtons() =>
        NavButton().Matches(MainWindowMarkup).Select(m => m.Value).ToList();

    /// <summary>The full element (open tag through its closing &lt;/Button&gt;) of each navigation item, in order.</summary>
    private static List<string> NavigationElements()
    {
        var markup = MainWindowMarkup;
        return NavButton().Matches(markup).Select(m => markup[m.Index..(markup.IndexOf("</Button>", m.Index, StringComparison.Ordinal))]).ToList();
    }

    // ------------------------------------------------------------------ destinations

    [Fact]
    public void Navigation_KeepsExactlyTheEstablishedDestinations_InOrder_WithNoDuplicates()
    {
        var buttons = NavigationButtons();
        Assert.Equal(Destinations.Length, buttons.Count);
        var commands = buttons.Select(b => Attribute(b, "Command")).ToList();
        Assert.Equal(Destinations.Select(d => $"{{Binding {d.Command}}}"), commands);
        Assert.Equal(commands.Count, commands.Distinct().Count());

        var labels = NavigationElements().Select(e => Regex.Match(e, "<TextBlock Classes=\"navLabel\" Text=\"([^\"]+)\"").Groups[1].Value).ToList();
        Assert.Equal(Destinations.Select(d => d.Label), labels);
        // No Manager/Alerts destination (CLAUDE.md "current scope decision") — checked on real attributes, not comments.
        Assert.DoesNotMatch(new Regex("(Text|Command|AutomationProperties.Name|ToolTip.Tip)=\"[^\"]*(Manager|Alerts)"), MainWindowMarkup);
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
            Assert.Equal("navItem", Attribute(buttons[i], "Classes"));
            Assert.Equal(accessibleName, Attribute(buttons[i], "AutomationProperties.Name"));
            Assert.Contains(label, accessibleName); // WCAG 2.5.3: the accessible name contains the visible label
            Assert.Equal(label, Attribute(buttons[i], "ToolTip.Tip")); // the collapsed sidebar's tooltip is the label
            Assert.Equal("{Binding IsNavigationShownCollapsed}", Attribute(buttons[i], "ToolTip.ServiceEnabled"));
        }

        // Each selection flag drives exactly one item, so at most one item can ever be active.
        foreach (var (_, _, flag, _) in Destinations)
        {
            Assert.Single(Regex.Matches(MainWindowMarkup, $"Classes.active=\"\\{{Binding {flag}\\}}\""));
        }
    }

    [Fact]
    public void EveryItem_HasADecorativeIcon_AnIndicatorSlot_AndOneLabel()
    {
        var geometries = new[] { "Dashboard", "Calendar", "Passdown", "Notes", "History", "Settings" };
        var elements = NavigationElements();
        for (var i = 0; i < elements.Count; i++)
        {
            Assert.Contains($"<PathIcon Data=\"{{StaticResource Passdown{geometries[i]}IconGeometry}}\" AutomationProperties.AccessibilityView=\"Raw\" />", elements[i]);
            Assert.Contains($"<StreamGeometry x:Key=\"Passdown{geometries[i]}IconGeometry\">", TokensMarkup);
            Assert.Single(Regex.Matches(elements[i], "<Border Classes=\"navIndicator\" />"));
            Assert.Single(Regex.Matches(elements[i], "<TextBlock Classes=\"navLabel\""));
        }
    }

    [Fact]
    public void Settings_IsPinnedToTheBottomRow_WithTheGearAndTheUpdateIndicator()
    {
        var settings = NavigationElements()[^1];
        Assert.Contains("x:Name=\"SettingsNavigationItem\"", settings);
        Assert.Equal("2", Attribute(NavigationButtons()[^1], "Grid.Row")); // the bottom Auto row; destinations fill the * row
        Assert.Matches(new Regex("<Grid x:Name=\"AppNavigationLayout\" RowDefinitions=\"Auto,\\*,Auto\""), MainWindowMarkup);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding SettingsNavigationHelpText}\"", settings);
        var dot = Regex.Match(settings, "<Ellipse x:Name=\"UpdateIndicatorDot\"[^>]*/>", RegexOptions.Singleline).Value;
        Assert.Contains("IsVisible=\"{Binding HasUpdateNotification}\"", dot);
        Assert.Contains("IsHitTestVisible=\"False\"", dot);
        Assert.Contains("AutomationProperties.AccessibilityView=\"Raw\"", dot);
    }

    [Fact]
    public void Toggle_IsAnAccessibleIconAction_FirstInTheLandmark()
    {
        var markup = MainWindowMarkup;
        var toggle = markup[markup.IndexOf("<Button x:Name=\"NavigationToggle\"", StringComparison.Ordinal)..];
        toggle = toggle[..toggle.IndexOf("</Button>", StringComparison.Ordinal)];
        Assert.Contains("Classes=\"iconAction\"", toggle);
        Assert.Contains("Command=\"{Binding ToggleNavigationCommand}\"", toggle);
        Assert.Contains("AutomationProperties.Name=\"{Binding NavigationToggleName}\"", toggle);
        Assert.Contains("ToolTip.Tip=\"{Binding NavigationToggleName}\"", toggle);
        Assert.Contains("<PathIcon Data=\"{StaticResource PassdownSidebarToggleIconGeometry}\" AutomationProperties.AccessibilityView=\"Raw\" />", toggle);
        Assert.True(markup.IndexOf("NavigationToggle", StringComparison.Ordinal) < markup.IndexOf("ShowDashboardSectionCommand", StringComparison.Ordinal), "the toggle comes first in Tab order");
        Assert.Contains("Classes.collapsed=\"{Binding IsNavigationShownCollapsed}\"", markup);
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
            Assert.Contains($"<Border IsVisible=\"{{Binding {flag}}}\">\n                <views:{view} ", MainWindowMarkup);
        }

        Assert.Contains("<Grid x:Name=\"SectionHost\" Grid.Column=\"1\">", MainWindowMarkup);
    }

    [Fact]
    public void Navigation_IsOneLandmark_WithNoScrollingOrDecorativeHeading()
    {
        Assert.Single(Regex.Matches(MainWindowMarkup, "AutomationProperties.LandmarkType=\"Navigation\""));
        Assert.Contains("AutomationProperties.Name=\"Application sections\" AutomationProperties.LandmarkType=\"Navigation\"", MainWindowMarkup);
        var shell = MainWindowMarkup[..MainWindowMarkup.IndexOf("<views:", StringComparison.Ordinal)];
        Assert.DoesNotContain("ScrollViewer", shell);
        Assert.Equal(Destinations.Length, Regex.Matches(shell, "<TextBlock ").Count); // the six labels — no heading, no role label
        foreach (var forbidden in new[] { "Admissions", "ADMISSIONS", "OPERATIONS", "Supervision" })
        {
            Assert.DoesNotContain(forbidden, shell);
        }
    }

    // ------------------------------------------------------------------ title area

    [Fact]
    public void TitleArea_IsTheNativeTitleBar_OriginalIconAndNameOnly_WithNoBannerOrRepeatedTitle()
    {
        var window = MainWindowMarkup[..MainWindowMarkup.IndexOf('>')];
        Assert.Contains("Icon=\"/Assets/logo.ico\"", window);
        Assert.Contains("Title=\"Carowinds Operations\"", window);
        Assert.DoesNotContain("ExtendClientAreaToDecorationsHint", MainWindowMarkup);
        Assert.DoesNotContain("SystemDecorations", MainWindowMarkup);
        foreach (var retired in new[] { "BrandBanner", "BrandWordmark", "fury325", "CAROWINDS OPERATIONS", "Text=\"Carowinds Operations\"" })
        {
            Assert.DoesNotContain(retired, MainWindowMarkup);
        }
    }

    [Fact]
    public void ShellMarkup_NeverReferencesAUrlOrDeveloperPath()
    {
        var withoutNamespaces = Regex.Replace(MainWindowMarkup, "xmlns(:\\w+)?=\"[^\"]*\"", string.Empty);
        foreach (var forbidden in new[] { "http:", "https:", "file:", "/Users/", "/home/", ":\\", "OneDrive" })
        {
            Assert.DoesNotContain(forbidden, withoutNamespaces, StringComparison.OrdinalIgnoreCase);
        }
    }

    // The Fury 325 photograph is kept in the repository, unreferenced, until the 35A Windows review accepts the new
    // shell (rollback is a revert); its removal is planned for 35G. Until then it stays a single valid asset.
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

        var src = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(path))))!;
        var copies = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f).Contains("fury", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(copies);
    }

    // ------------------------------------------------------------------ tokens

    [Fact]
    public void SidebarTokens_KeepTheShellCompact()
    {
        Assert.InRange(Resolve<double>("PassdownSidebarExpandedWidth"), 180, 232);
        Assert.InRange(Resolve<double>("PassdownSidebarCollapsedWidth"), 48, 64);
        Assert.InRange(Resolve<double>("PassdownNavigationItemHeight"), 32, 40); // compact, still a comfortable hit target
        Assert.InRange(Resolve<double>("PassdownNavigationIconSize"), 16, 20);
        var margin = Resolve<Avalonia.Thickness>("PassdownNavigationItemMargin");
        var inset = Resolve<Avalonia.Thickness>("PassdownNavigationContentInset");
        // The collapsed width centres the icon: margin + 3px indicator + inset + icon + same on the right.
        var iconCentre = margin.Left + 3 + inset.Left + Resolve<double>("PassdownNavigationIconSize") / 2;
        Assert.Equal(Resolve<double>("PassdownSidebarCollapsedWidth") / 2, iconCentre, 0.5);
    }

    [Fact]
    public void MotionTokens_StayWithinTheApprovedUpperBounds()
    {
        Assert.InRange(Resolve<TimeSpan>("PassdownMotionFast").TotalMilliseconds, 100, 140);
        Assert.InRange(Resolve<TimeSpan>("PassdownMotionPanel").TotalMilliseconds, 160, 180);
        Assert.InRange(Resolve<TimeSpan>("PassdownMotionOverlay").TotalMilliseconds, 120, 180);
        Assert.IsType<Avalonia.Animation.Easings.CubicEaseOut>(Resolve<object>("PassdownMotionEasing"));
    }

    // WCAG relative luminance from the token XAML (no brush reads in this partition).
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void NavigationColours_MeetContrastTargets(string theme)
    {
        Color Token(string key)
        {
            var dict = ThemeDictionary(theme);
            var m = Regex.Match(dict, $"<SolidColorBrush x:Key=\"{key}\" Color=\"(#[0-9A-Fa-f]{{6}})\" />");
            Assert.True(m.Success, $"{key} missing in {theme}");
            return Color.Parse(m.Groups[1].Value);
        }

        var background = Token("PassdownNavigationBackgroundBrush");
        var active = Token("PassdownNavigationActiveBackgroundBrush");
        Assert.True(Contrast(Token("PassdownTextSecondaryBrush"), background) >= 4.5, "inactive label");
        Assert.True(Contrast(Token("PassdownTextPrimaryBrush"), active) >= 4.5, "active label");
        Assert.True(Contrast(Token("PassdownTextPrimaryBrush"), Token("PassdownHoverBackgroundBrush")) >= 4.5, "hovered label");
        Assert.True(Contrast(Token("PassdownNavigationActiveIndicatorBrush"), active) >= 3, "active indicator (non-text, WCAG 1.4.11)");
        Assert.True(Contrast(Token("PassdownTextDisabledBrush"), background) >= 2.5, "disabled label stays discernible");
    }

    private static string ThemeDictionary(string theme)
    {
        var start = TokensMarkup.IndexOf($"<ResourceDictionary x:Key=\"{theme}\">", StringComparison.Ordinal);
        return TokensMarkup[start..TokensMarkup.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal)];
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Theory]
    [InlineData("PassdownNavigationBackgroundBrush")]
    [InlineData("PassdownNavigationActiveBackgroundBrush")]
    [InlineData("PassdownNavigationActiveIndicatorBrush")]
    [InlineData("PassdownTextSecondaryBrush")]
    [InlineData("PassdownTextPrimaryBrush")]
    [InlineData("PassdownHoverBackgroundBrush")]
    [InlineData("PassdownDividerBrush")]
    public void NavigationBrushes_ResolveSeparatelyPerTheme_SoALiveSwitchRestylesTheSidebar(string key) =>
        Assert.NotSame(Resolve<IBrush>(key, ThemeVariant.Light), Resolve<IBrush>(key, ThemeVariant.Dark));

    [Fact]
    public void BrandNavy_IsThemeInvariantIdentity()
    {
        Assert.Same(Resolve<IBrush>("PassdownBrandNavyBrush", ThemeVariant.Light), Resolve<IBrush>("PassdownBrandNavyBrush", ThemeVariant.Dark));
        Assert.Contains("<Color x:Key=\"PassdownBrandNavyColor\">#17324D</Color>", TokensMarkup);
        var darkStart = TokensMarkup.IndexOf("<ResourceDictionary x:Key=\"Dark\">", StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"PassdownBrandNavy", TokensMarkup[darkStart..]);
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
            "Border.navSidebar", "Border.navSidebar.collapsed", "Border.navSidebar.collapsed TextBlock.navLabel",
            "Button.navItem", "Button.navItem.active", "Button.navItem:pressed", "Button.navItem.active Border.navIndicator",
            "Button.navItem:pointerover /template/ ContentPresenter#PART_ContentPresenter",
            "Button.navItem:pressed /template/ ContentPresenter#PART_ContentPresenter",
            "Button.navItem:disabled /template/ ContentPresenter#PART_ContentPresenter",
            "Button.iconAction",
        })
        {
            Assert.Contains(expected, selectors);
        }

        Assert.DoesNotContain("<Window.Styles>", MainWindowMarkup);
        Assert.DoesNotContain("<Style Selector=", MainWindowMarkup);
        foreach (var retired in new[] { "Border.brandBanner", "TextBlock.brandWordmark", "Border.appNavigation", "Button.appSectionItem" })
        {
            Assert.DoesNotContain(retired, selectors);
        }
    }

    private static string ShellStylesSection()
    {
        var start = StylesMarkup.IndexOf("App shell: sidebar navigation", StringComparison.Ordinal);
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
            "PassdownNavigationBackgroundBrush", "PassdownNavigationActiveBackgroundBrush", "PassdownNavigationActiveIndicatorBrush",
            "PassdownDividerBrush", "PassdownTextSecondaryBrush", "PassdownTextPrimaryBrush", "PassdownHoverBackgroundBrush",
            "PassdownPressedBackgroundBrush", "PassdownTextDisabledBrush", "PassdownSidebarExpandedWidth", "PassdownSidebarCollapsedWidth",
            "PassdownNavigationItemHeight", "PassdownNavigationItemMargin", "PassdownNavigationContentInset", "PassdownFontSizeBody",
        })
        {
            Assert.Contains($"{{DynamicResource {token}}}", section);
        }

        // Accent fills (PassdownAccentBrush) are for primary actions, never a navigation surface.
        Assert.DoesNotContain("PassdownAccentBrush}", section);
    }

    [Fact]
    public void ActiveItem_IsIndicatedByShapeWeightAndScreenReaderStatus_NotColorAlone()
    {
        var section = ShellStylesSection();
        var item = Block(section, "Button.navItem");
        var active = Block(section, "Button.navItem.active");

        Assert.Contains("<Setter Property=\"FontWeight\" Value=\"Normal\" />", item);
        Assert.Contains("<Setter Property=\"FontWeight\" Value=\"SemiBold\" />", active);
        Assert.Contains("<Setter Property=\"AutomationProperties.ItemStatus\" Value=\"Current page\" />", active);
        Assert.Contains("<Setter Property=\"Background\" Value=\"{DynamicResource PassdownNavigationActiveBackgroundBrush}\" />", active);
        // The indicator bar: a 3px shape on every item, drawn only for the active one.
        var indicator = Block(section, "Button.navItem Border.navIndicator");
        Assert.Contains("<Setter Property=\"Width\" Value=\"3\" />", indicator);
        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", indicator);
        Assert.Contains("<Setter Property=\"Background\" Value=\"{DynamicResource PassdownNavigationActiveIndicatorBrush}\" />", Block(section, "Button.navItem.active Border.navIndicator"));

        // Hover is a neutral fill only; the active item keeps its own fill when hovered, so the two never look alike.
        var hover = Block(section, "Button.navItem:pointerover /template/ ContentPresenter#PART_ContentPresenter");
        Assert.Contains("PassdownHoverBackgroundBrush", hover);
        Assert.Contains("Button.navItem.active:pointerover /template/ ContentPresenter#PART_ContentPresenter", section);
    }

    [Fact]
    public void ItemLabels_TrimRatherThanWrap_SoItemsKeepTheirHeight()
    {
        // Phase 35A (replaces the strip's SemiBold-width reservation, which a vertical list does not need): a label
        // can never push its row taller or overflow the sidebar.
        var label = Block(ShellStylesSection(), "Button.navItem TextBlock.navLabel");
        Assert.Contains("<Setter Property=\"TextTrimming\" Value=\"CharacterEllipsis\" />", label);
        Assert.DoesNotContain("TextWrapping", label);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"{DynamicResource PassdownNavigationItemHeight}\" />", Block(ShellStylesSection(), "Button.navItem"));
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

    // ------------------------------------------------------------------ motion scoping

    private static string MotionSection()
    {
        var start = StylesMarkup.IndexOf("Motion (Phase 35A)", StringComparison.Ordinal);
        var end = StylesMarkup.IndexOf("Lists, popups and loading feedback", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        return StylesMarkup[start..end];
    }

    [Fact]
    public void EveryTransition_IsScopedUnderTheWindowMotionClass_AndUsesTheMotionTokens()
    {
        // Outside the motion section no style declares a transition, so Off / reduced motion means none exists.
        var outside = StylesMarkup.Replace(MotionSection(), string.Empty);
        Assert.DoesNotContain("Transition ", outside);
        Assert.DoesNotContain("Property=\"Transitions\"", outside);

        var motion = MotionSection();
        foreach (Match style in Regex.Matches(motion, "<Style Selector=\"([^\"]+)\">\\s*<Setter Property=\"Transitions\">"))
        {
            foreach (var selector in style.Groups[1].Value.Split(','))
            {
                Assert.StartsWith("Window.motion ", selector.Trim());
            }
        }

        foreach (Match duration in Regex.Matches(motion, "Duration=\"([^\"]+)\""))
        {
            Assert.Matches("^\\{StaticResource PassdownMotion(Fast|Panel|Overlay)\\}$", duration.Groups[1].Value);
        }

        Assert.DoesNotContain("KeyFrame", StylesMarkup);
        Assert.DoesNotContain("IterationCount", StylesMarkup); // no looping decorative animation
        Assert.DoesNotContain("ScaleTransform", motion); // no bouncing/scaling
        Assert.Contains("<Style Selector=\"Window.noMotion Button, Window.noMotion ToggleButton, Window.noMotion RepeatButton\">", motion);
    }

    [Fact]
    public void Sidebar_WidthIsNeverAnimated_SoPagesLayOutOncePerToggle()
    {
        Assert.DoesNotMatch(new Regex("Transition Property=\"(Width|MinWidth|MaxWidth|Margin|Padding)\""), StylesMarkup);
    }

    [Fact]
    public void Window_BindsTheMotionClasses_ToTheViewModel()
    {
        var window = MainWindowMarkup[..MainWindowMarkup.IndexOf('>')];
        Assert.Contains("Classes.motion=\"{Binding IsInterfaceMotionEnabled}\"", window);
        Assert.Contains("Classes.noMotion=\"{Binding IsInterfaceMotionDisabled}\"", window);
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

    [GeneratedRegex("<Button [^>]*Classes=\"navItem[^\"]*\"[^>]*>", RegexOptions.Singleline)]
    private static partial Regex NavButton();
}
