using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 21C markup guards (binding strings, not bound values — see the deterministic-partition notes):
/// the Shows surfaces exist with accessible names, and no Teams Copy option for shows was introduced.
/// </summary>
public class Phase21CShowsMarkupTests
{
    private static string Markup(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return Regex.Replace(File.ReadAllText(candidate), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            }
        }

        throw new FileNotFoundException(relativePath);
    }

    [Fact]
    public void AssociateView_HasAnAccessibleOptionalShowsSection_WithPerShowCheckboxes()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml");

        Assert.Contains("IsChecked=\"{Binding IncludeShows}\"", markup);
        Assert.Contains("Include the Shows section in the Passdown", markup);
        Assert.Contains("ItemsSource=\"{Binding ShowRows}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibleName}\"", markup);
        Assert.Contains("Command=\"{Binding SelectAllShowsCommand}\"", markup);
        Assert.Contains("Command=\"{Binding ClearShowsCommand}\"", markup);
    }

    [Fact]
    public void AssociateView_CopyMenu_HasNoShowsOption()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml");
        var copyCommands = Regex.Matches(markup, "Copy[A-Za-z]*Command").Select(m => m.Value);

        Assert.DoesNotContain(copyCommands, command => command.Contains("Show", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DashboardAndCalendar_BindTheSharedShowsPresentation()
    {
        foreach (var path in new[] { "src/Carowinds.Passdown.Desktop/Views/DashboardView.axaml", "src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml" })
        {
            var markup = Markup(path);
            Assert.Contains("{Binding ShowsPresentation.Heading}", markup);
            Assert.Contains("{Binding ShowsPresentation.StatusText}", markup);
            Assert.Contains("ItemsSource=\"{Binding ShowsPresentation.Rows}\"", markup);
        }
    }

    [Fact]
    public void HistoryView_ShowsFrozenShowsFromTheSnapshotOnly()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/HistoryView.axaml");

        Assert.Contains("ItemsSource=\"{Binding SelectedDetail.Snapshot.Shows}\"", markup);
    }
}
