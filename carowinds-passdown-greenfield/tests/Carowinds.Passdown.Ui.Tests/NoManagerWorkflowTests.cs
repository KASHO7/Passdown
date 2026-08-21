using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 4B Part I #15 / CLAUDE.md "current scope decision": the Manager workflow is deferred and
/// must not appear anywhere in the Desktop shell yet — not as a type, and not as a disabled
/// placeholder button/label in the actual rendered shell.
/// </summary>
public class NoManagerWorkflowTests
{
    [Fact]
    public void DesktopAssembly_DefinesNoManagerType()
    {
        var desktopAssembly = Assembly.Load("Carowinds.Passdown.Desktop");

        var managerTypes = desktopAssembly.GetTypes()
            .Where(t => t.Name.Contains("Manager", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.FullName)
            .ToArray();

        Assert.True(managerTypes.Length == 0, $"Expected no Manager-related types in the Desktop shell yet; found: {string.Join(", ", managerTypes)}");
    }

    [Fact]
    public void AssociateView_LogicalTree_ContainsNoManagerLabeledControl()
    {
        var viewModel = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(DateTimeOffset.UtcNow));
        var view = new AssociateView { DataContext = viewModel };

        var offendingTexts = view.GetLogicalDescendants()
            .SelectMany(DescribeStaticText)
            .Where(text => text.Contains("Manager", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(offendingTexts.Length == 0, $"Expected no 'Manager' text in the Associate screen's logical tree; found: {string.Join(", ", offendingTexts)}");
    }

    /// <summary>
    /// The app shell's own header chrome lives in <c>MainWindow.axaml</c>, which — unlike
    /// <see cref="AssociateView"/> — cannot safely be instantiated here (a <c>Window</c> attaches
    /// to Avalonia's compositor on construction, which requires the headless platform's own
    /// dispatcher thread; see <see cref="AssociateViewTests"/>'s remarks). Scanning its source
    /// directly is a simpler, equally direct way to prove the shell chrome itself never mentions
    /// "Manager" without needing a live Window instance. XML comments are stripped first — a
    /// developer comment explaining that Manager navigation was deliberately left out is legitimate
    /// source content, not user-facing markup.
    /// </summary>
    [Fact]
    public void MainWindowMarkup_ContainsNoManagerText()
    {
        var xamlPath = FindRepoFile("src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml");

        var markup = Regex.Replace(File.ReadAllText(xamlPath), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        Assert.DoesNotContain("Manager", markup, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> DescribeStaticText(ILogical node) => node switch
    {
        TextBlock textBlock when textBlock.Text is { } text => [text],
        Button { Content: string content } => [content],
        _ => [],
    };

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' above {AppContext.BaseDirectory}.");
    }
}
