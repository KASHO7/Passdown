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

    // Phase 22E: the Calendar moved to its own compact CalendarShowsPresentation (summary + disclosure,
    // covered by Phase22ECalendarShowsTests); the Dashboard keeps the shared Phase 21C presentation
    // (Phase 23B wraps its rows in DashboardShowRowViewModel).
    [Fact]
    public void Dashboard_BindsTheSharedShowsPresentation_AndCalendarItsCompactSummary()
    {
        var dashboard = Markup("src/Carowinds.Passdown.Desktop/Views/DashboardView.axaml");
        Assert.Contains("{Binding ShowsPresentation.Heading}", dashboard);
        Assert.Contains("{Binding ShowsPresentation.StatusText}", dashboard);
        // Phase 23B: the Dashboard rows are view models over ShowsPresentation.Rows (in-memory expansion state).
        Assert.Contains("ItemsSource=\"{Binding ShowRows}\"", dashboard);
        Assert.Contains("IsVisible=\"{Binding ShowsPresentation.HasRows}\"", dashboard);

        var calendar = Markup("src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml");
        Assert.Contains("{Binding CalendarShows.StatusText}", calendar);
        Assert.Contains("ItemsSource=\"{Binding CalendarShows.Rows}\"", calendar);
    }

    [Fact]
    public void HistoryView_ShowsFrozenShowsFromTheSnapshotOnly()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/HistoryView.axaml");

        Assert.Contains("ItemsSource=\"{Binding SelectedDetail.Snapshot.Shows}\"", markup);
    }
}
