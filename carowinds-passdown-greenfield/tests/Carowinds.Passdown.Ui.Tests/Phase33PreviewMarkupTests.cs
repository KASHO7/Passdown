using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 33: markup-level guarantees for the preview pane — placement outside the section bodies (so it
/// persists across Overview/Products/Shows/Notes), one shared Print command for both entry points,
/// accessible names on every control, theme tokens in both themes, a white page in both themes, and no
/// literal colors. Reads markup text only (mixed-partition safe: no bound values, no windows).
/// </summary>
public partial class Phase33PreviewMarkupTests
{
    private static string AssociateMarkup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));

    private static string PreviewMarkup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/PassdownPdfPreviewView.axaml"));

    private static string Tokens => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));

    [Fact]
    public void PreviewPane_SitsBesideTheEditor_NotInsideAnySection()
    {
        var markup = AssociateMarkup;
        var editorStart = markup.IndexOf("x:Name=\"AssociateEditorPane\"", StringComparison.Ordinal);
        var lastSection = markup.LastIndexOf("IsVisible=\"{Binding Is", StringComparison.Ordinal);
        var splitter = markup.IndexOf("x:Name=\"PreviewSplitter\"", StringComparison.Ordinal);
        var host = markup.IndexOf("x:Name=\"PreviewPaneHost\"", StringComparison.Ordinal);

        Assert.True(editorStart > 0 && splitter > editorStart && host > splitter);
        Assert.True(markup.LastIndexOf("SectionSelected}\"", StringComparison.Ordinal) < splitter, "a section body appears after the preview");
        Assert.True(lastSection < host);
        // Phase 35B: section tabs over the section bodies, inside the editor pane only (the preview keeps its own column).
        Assert.Contains("x:Name=\"AssociateEditorPane\" RowDefinitions=\"Auto,*\"", markup);
        Assert.True(markup.IndexOf("x:Name=\"SectionTabsBar\"", StringComparison.Ordinal) is var tabs && tabs > editorStart && tabs < splitter);
        Assert.Contains("<GridSplitter Grid.Column=\"1\" x:Name=\"PreviewSplitter\"", markup);
        Assert.Contains("ResizeDirection=\"Columns\"", markup);
    }

    [Fact]
    public void BothPrintButtons_UseTheOneSharedPrintCommand()
    {
        Assert.Contains("x:Name=\"AssociatePrintMenuItem\"", AssociateMarkup); // Phase 35B: Actions → Print (also Ctrl+P)
        Assert.Contains("<KeyBinding Gesture=\"Ctrl+P\" Command=\"{Binding Preview.PrintCommand}\" />", AssociateMarkup);
        Assert.Contains("Command=\"{Binding Preview.PrintCommand}\"", AssociateMarkup);
        Assert.Contains("Command=\"{Binding PrintCommand}\"", PreviewMarkup); // DataContext = the same Preview
        Assert.Contains("<views:PassdownPdfPreviewView x:Name=\"PreviewPane\" DataContext=\"{Binding Preview}\" />", AssociateMarkup);
        Assert.Contains("Command=\"{Binding CreatePdfCommand}\"", AssociateMarkup); // Create PDF is kept
    }

    [Fact]
    public void Toolbar_HasEveryRequiredControl()
    {
        var markup = PreviewMarkup;
        foreach (var command in new[] { "PrintCommand", "SavePdfCommand", "PreviousPageCommand", "NextPageCommand", "ZoomInCommand", "ZoomOutCommand", "FitWidthCommand", "FitPageCommand", "RefreshCommand", "HidePreviewCommand" })
        {
            Assert.Contains($"Command=\"{{Binding {command}}}\"", markup);
        }

        Assert.Contains("Text=\"{Binding PageIndicatorText}\"", markup);
        Assert.Contains("<WrapPanel x:Name=\"PreviewToolbar\"", markup); // wraps rather than overflowing
        Assert.Contains("x:Name=\"TogglePreviewButton\"", AssociateMarkup);
    }

    [Fact]
    public void EveryPreviewButton_HasAnAccessibleName_AndZoomIsKeyboardReachable()
    {
        foreach (Match button in ButtonTag().Matches(PreviewMarkup))
        {
            Assert.Contains("AutomationProperties.Name=", button.Value);
        }

        foreach (var gesture in new[] { "Ctrl+OemPlus", "Ctrl+OemMinus", "Ctrl+D0", "PageDown", "PageUp" })
        {
            Assert.Contains($"Gesture=\"{gesture}\"", PreviewMarkup);
        }

        Assert.Contains("AutomationProperties.Name=\"Resize the PDF preview\"", AssociateMarkup);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", PreviewMarkup); // status changes are announced
    }

    [Fact]
    public void PreviewMarkup_UsesOnlyDefinedTokens_AndNoLiteralColors()
    {
        foreach (Match reference in ResourceReference().Matches(PreviewMarkup))
        {
            Assert.Contains($"x:Key=\"{reference.Groups[1].Value}\"", Tokens);
        }

        Assert.DoesNotMatch(LiteralColor(), PreviewMarkup);
        Assert.DoesNotMatch(LiteralColor(), AssociateMarkup);
    }

    [Fact]
    public void PaperStaysWhite_InBothThemes_AndTheDeskFollowsTheTheme()
    {
        var tokens = Tokens;
        var light = tokens.IndexOf("<ResourceDictionary x:Key=\"Light\">", StringComparison.Ordinal);
        var dark = tokens.IndexOf("<ResourceDictionary x:Key=\"Dark\">", StringComparison.Ordinal);

        var paper = tokens.IndexOf("x:Key=\"PassdownPdfPaperBrush\" Color=\"#FFFFFF\"", StringComparison.Ordinal);
        Assert.True(paper > 0 && paper < light, "the paper brush must be theme-invariant white");
        foreach (var key in new[] { "PassdownPdfCanvasBrush", "PassdownPdfPaperEdgeBrush" })
        {
            var first = tokens.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
            var second = tokens.IndexOf($"x:Key=\"{key}\"", first + 1, StringComparison.Ordinal);
            Assert.True(first > light && first < dark && second > dark, $"{key} must be defined for Light and Dark");
        }

        Assert.Contains("Background=\"{DynamicResource PassdownPdfPaperBrush}\"", PreviewMarkup);
    }

    [GeneratedRegex("<Button\\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex ButtonTag();

    [GeneratedRegex(@"(?:Dynamic|Static)Resource (Passdown[A-Za-z0-9]+)")]
    private static partial Regex ResourceReference();

    [GeneratedRegex("=\"#[0-9A-Fa-f]{3,8}\"")]
    private static partial Regex LiteralColor();
}
