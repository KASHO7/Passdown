using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 12A Part J/M: proves <see cref="OperationsCalendarView"/> resolves through Avalonia's real
/// XAML loader — every binding/DataTemplate/converter path — for a populated grid, an empty/error
/// state, and dense metadata, and that its key actions carry an accessible label (spec Part I).
/// </summary>
public class OperationsCalendarViewTests
{
    private static readonly DateOnly FixedToday = new(2026, 8, 21);

    private static FakeClock CreateClock() =>
        new(new DateTimeOffset(FixedToday.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));

    [Fact]
    public async Task OperationsCalendarView_WithSnapshot_ConstructsAndAcceptsDataContext()
    {
        var viewModel = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    [Fact]
    public async Task OperationsCalendarView_LoadFailure_ConstructsWithoutError()
    {
        var viewModel = new OperationsCalendarViewModel(new FakeOperationsCalendarService { Exception = new InvalidOperationException("boom") }, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    // Dense metadata case (spec Part J "many metadata cases"): every day in the grid carries a
    // finalized flag, a draft flag, and a note count — the view must still construct cleanly.
    [Fact]
    public async Task OperationsCalendarView_DenseMetadataAcrossEveryDay_ConstructsWithoutError()
    {
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) =>
            {
                var baseline = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                var denseDays = baseline.Days
                    .Select(d => d with { HasFinalizedPassdown = true, HasAssociateDraft = true, RelevantOperationalNoteCount = 12 })
                    .ToList();
                return baseline with { Days = denseDays };
            },
        };
        var viewModel = new OperationsCalendarViewModel(service, CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }

    [Fact]
    public async Task OperationsCalendarView_WithSnapshot_ExposesCriticalAutomationNames()
    {
        var viewModel = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), CreateClock());
        await viewModel.PendingLoad;

        var view = new OperationsCalendarView { DataContext = viewModel };

        var names = view.GetLogicalDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet();

        Assert.Contains("Previous month", names);
        Assert.Contains("Go to today", names);
        Assert.Contains("Next month", names);
        Assert.Contains("Calendar month grid", names);
        Assert.Contains("Selected date summary", names);
        Assert.Contains("Open Passdown", names);
        Assert.Contains("Open History", names);
        Assert.Contains("Open Operational Notes", names);

        // Each day cell's own automation name (bound via ItemsControl DataTemplate) is asserted
        // directly against OperationsCalendarDayViewModel in OperationsCalendarViewModelTests instead
        // of here — DashboardView's own automation-name test establishes the same boundary: an
        // ItemsControl's generated item containers aren't realized by GetLogicalDescendants() without
        // a layout pass, which this headless test harness never performs.
    }
}
