using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35E real-window geometry for the redesigned Calendar: the selected-date panel never outgrows the month column
/// (its body scrolls and its actions stay inside it), the month card has no gaps between cells, and a day cell is
/// chosen from the keyboard. A partial of the RUN-ALONE <see cref="OperationsCalendarViewTests"/> class.
/// </summary>
public partial class OperationsCalendarViewTests
{
    private static Window Phase35ERealize(OperationsCalendarViewModel viewModel, double width, double height, bool dark)
    {
        var window = new Window { Width = width, Height = height, Content = new OperationsCalendarView { DataContext = viewModel } };
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Rect Phase35EBounds(Visual control, Window window) =>
        new(control.TranslatePoint(new Point(0, 0), window) ?? default, control.Bounds.Size);

    /// <summary>A six-row month whose selected date has a long Passdown note and several notes.</summary>
    private static OperationsCalendarSnapshot Phase35EBusySixRowMonth(DateOnly month, DateOnly selected)
    {
        var snapshot = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
        return snapshot with
        {
            Days = snapshot.Days.Select(d => d with { HasAssociateDraft = d.Date.Day % 3 == 0, RelevantOperationalNoteCount = d.Date.Day % 4 }).ToList(),
            Selected = snapshot.Selected with { PassdownStatus = OperationsCalendarPassdownStatus.DraftInProgress, LastSavedAt = DateTimeOffset.UtcNow, RelevantOperationalNoteCount = 3, ImportantOperationalNoteCount = 1 },
        };
    }

    [Theory]
    [InlineData(1920d, 1080d, false)]
    [InlineData(1280d, 720d, true)]
    [InlineData(960d, 640d, false)]
    public async Task Phase35E_SelectedDatePanel_NeverOutgrowsTheMonth_AndKeepsItsActionsInside(double width, double height, bool dark)
    {
        // August 2026 starts on a Saturday: a six-row month.
        var service = new FakeOperationsCalendarService { SnapshotFactory = Phase35EBusySixRowMonth };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;
        Assert.Equal(6, viewModel.Weeks.Count);
        var window = Phase35ERealize(viewModel, width, height, dark);

        var panel = window.GetVisualDescendants().OfType<Border>().Single(b => AutomationProperties.GetName(b) == "Selected date summary");
        var panelBounds = Phase35EBounds(panel, window);
        var monthScroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "CalendarScroll");
        // The panel fills the workspace height but never runs past the window: its body scrolls instead.
        Assert.True(panelBounds.Bottom <= window.ClientSize.Height + 0.5, $"panel bottom {panelBounds.Bottom} is below the window ({window.ClientSize.Height}).");
        Assert.True(panelBounds.Right <= window.ClientSize.Width + 0.5);
        Assert.True(Math.Abs(panelBounds.Bottom - Phase35EBounds(monthScroll, window).Bottom) <= 1.0, "panel and month column end together.");

        foreach (var name in new[] { "Open Passdown", "View Pricing", "Open Operational Notes", "View Finalized History" })
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
            var bounds = Phase35EBounds(button, window);
            Assert.True(button.IsEffectivelyVisible && bounds.Width > 40, $"'{name}' is not usable ({bounds.Width}px).");
            Assert.True(bounds.Left >= panelBounds.Left - 0.5 && bounds.Right <= panelBounds.Right + 0.5 && bounds.Bottom <= panelBounds.Bottom + 0.5,
                $"'{name}' {bounds} escapes the panel {panelBounds}.");
        }

        // Six rows, each seven flush cells: consecutive cells in a row touch (no gaps), rows touch too.
        var cells = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("calendarDay"))
            .Select(b => Phase35EBounds(b, window)).ToList();
        Assert.Equal(42, cells.Count);
        foreach (var row in cells.Chunk(7))
        {
            for (var i = 1; i < row.Length; i++)
            {
                Assert.True(Math.Abs(row[i].Left - row[i - 1].Right) <= 1.0, $"gap between cells: {row[i - 1]} → {row[i]}.");
            }
        }

        Assert.True(Math.Abs(cells[7].Top - cells[0].Bottom) <= 1.0, "gap between week rows.");

        // The month heading and its navigation never scroll away with the grid.
        var next = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Next month");
        Assert.True(Phase35EBounds(next, window).Bottom <= Phase35EBounds(monthScroll, window).Top + 0.5);

        window.Close();
    }

    [Fact]
    public async Task Phase35E_DayCell_IsChosenFromTheKeyboard_AndTheHeadingFollowsAtOnce()
    {
        var service = new FakeOperationsCalendarService();
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;
        var window = Phase35ERealize(viewModel, 1280, 860, dark: false);
        var heading = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SelectedDateHeading");

        var target = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("calendarDay") && b.DataContext is OperationsCalendarDayViewModel { Date.Day: 12, IsInDisplayedMonth: true });
        Assert.True(target.Focusable && target.IsTabStop);
        Assert.True(target.Focus(NavigationMethod.Tab));
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = target });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(new DateOnly(2026, 8, 12), viewModel.SelectedDate);
        Assert.Equal("Wednesday, August 12, 2026", heading.Text); // immediately — no animation or debounce in between
        await viewModel.PendingLoad;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains("selected", window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("calendarDay") && b.DataContext is OperationsCalendarDayViewModel { Date.Day: 12, IsInDisplayedMonth: true }).Classes);

        window.Close();
    }
}
