using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 9D Part O/P/T (extended Phase 10A Part P/X): proves Dashboard/History are additive and
/// never disturb the Associate workflow — all three child view models are constructed once and kept
/// alive for the app's lifetime, so switching <see cref="MainViewModel.SelectedSection"/> only
/// toggles which one is visible.
/// </summary>
public class MainViewModelNavigationTests
{
    private static readonly DateOnly AssociateDate = new(2026, 8, 26);
    private static readonly DateOnly HistoryDate = new(2026, 8, 25);

    private static MainViewModel CreateMainViewModel(out OperationsDashboardViewModel dashboard, out AssociateViewModel associate, out PassdownHistoryViewModel history)
    {
        dashboard = new OperationsDashboardViewModel(new FakeOperationsDashboardService());
        associate = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(new DateTimeOffset(AssociateDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)));
        var historyService = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(HistoryDate, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        history = new PassdownHistoryViewModel(historyService, new FakeFinalizedPassdownPdfRetrievalService());
        return new MainViewModel(dashboard, associate, history);
    }

    // 2: top-level nav defaults to Dashboard (spec Phase 10A Part Q).
    [Fact]
    public void SelectedSection_DefaultsToDashboard()
    {
        var main = CreateMainViewModel(out _, out _, out _);

        Assert.Equal(AppSection.Dashboard, main.SelectedSection);
        Assert.True(main.IsDashboardSectionSelected);
        Assert.False(main.IsAssociateSectionSelected);
        Assert.False(main.IsHistorySectionSelected);
    }

    [Fact]
    public void ShowAssociateSectionCommand_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _, out _);

        main.ShowAssociateSectionCommand.Execute(null);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.True(main.IsAssociateSectionSelected);
        Assert.False(main.IsDashboardSectionSelected);
    }

    [Fact]
    public void ShowHistorySectionCommand_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _, out _);

        main.ShowHistorySectionCommand.Execute(null);

        Assert.Equal(AppSection.History, main.SelectedSection);
        Assert.True(main.IsHistorySectionSelected);
        Assert.False(main.IsAssociateSectionSelected);
        Assert.False(main.IsDashboardSectionSelected);
    }

    [Fact]
    public void ShowDashboardSectionCommand_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _, out _);

        main.ShowHistorySectionCommand.Execute(null);
        main.ShowDashboardSectionCommand.Execute(null);

        Assert.Equal(AppSection.Dashboard, main.SelectedSection);
        Assert.True(main.IsDashboardSectionSelected);
    }

    // 15: Dashboard -> Associate navigation (via the Dashboard's own "Open Passdown" action).
    [Fact]
    public async Task DashboardOpenPassdownCommand_NavigatesToAssociate()
    {
        var main = CreateMainViewModel(out var dashboard, out _, out _);
        await dashboard.PendingLoad;

        dashboard.OpenPassdownCommand.Execute(null);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
    }

    // 16: Dashboard -> History navigation (via the Dashboard's own "Open History" action).
    [Fact]
    public void DashboardOpenHistoryCommand_NavigatesToHistory()
    {
        var main = CreateMainViewModel(out var dashboard, out _, out _);

        dashboard.OpenHistoryCommand.Execute(null);

        Assert.Equal(AppSection.History, main.SelectedSection);
    }

    // 23: selecting a History record never changes AssociateViewModel.SelectedDate.
    [Fact]
    public async Task SelectingHistoryRecord_NeverChangesAssociateSelectedDate()
    {
        var main = CreateMainViewModel(out _, out var associate, out var history);
        await associate.PendingLoad;
        await history.PendingLoad;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        Assert.Equal(AssociateDate, associate.SelectedDate);
    }

    // 17/24: navigating between Dashboard/Associate/History preserves Associate's draft/date state —
    // nothing is recreated.
    [Fact]
    public async Task NavigatingBetweenSections_PreservesAssociateState()
    {
        var main = CreateMainViewModel(out _, out var associate, out var history);
        await associate.PendingLoad;
        await history.PendingLoad;

        associate.ProjectedAttendanceText = "9000";
        await associate.PendingAutosave;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        main.ShowDashboardSectionCommand.Execute(null);
        main.ShowAssociateSectionCommand.Execute(null);

        Assert.Same(associate, main.Associate);
        Assert.Equal(AssociateDate, associate.SelectedDate);
        Assert.Equal("9000", associate.ProjectedAttendanceText);
    }

    // Phase 10C Part G: "Open Passdown" routes to Associate's Overview section specifically.
    [Fact]
    public async Task DashboardOpenPassdownCommand_RoutesToAssociateOverview()
    {
        var main = CreateMainViewModel(out var dashboard, out var associate, out _);
        await dashboard.PendingLoad;
        associate.SelectedSection = AssociateSection.Notes;

        dashboard.OpenPassdownCommand.Execute(null);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(AssociateSection.Overview, associate.SelectedSection);
    }

    // Phase 10C Part F: "Open Notes" is a true deep link to Associate's Notes section.
    [Fact]
    public async Task DashboardOpenNotesCommand_RoutesToAssociateNotes()
    {
        var main = CreateMainViewModel(out var dashboard, out var associate, out _);
        await dashboard.PendingLoad;

        dashboard.OpenNotesCommand.Execute(null);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.Equal(AssociateSection.Notes, associate.SelectedSection);
    }

    // Phase 10C Part H: neither deep link ever changes AssociateViewModel.SelectedDate — an associate
    // mid-edit on another date is never silently switched to today's record.
    [Fact]
    public async Task DashboardDeepLinks_NeverChangeAssociateSelectedDate()
    {
        var main = CreateMainViewModel(out var dashboard, out var associate, out _);
        await dashboard.PendingLoad;
        await associate.PendingLoad;

        dashboard.OpenNotesCommand.Execute(null);
        Assert.Equal(AssociateDate, associate.SelectedDate);

        dashboard.OpenPassdownCommand.Execute(null);
        Assert.Equal(AssociateDate, associate.SelectedDate);
    }

    // Phase 10C Part H: merely viewing/switching top-level sections never touches Associate's internal
    // section on its own — only the Dashboard's explicit "Open Passdown"/"Open Notes" actions do.
    [Fact]
    public void SwitchingTopLevelSections_NeverChangesAssociateInternalSection()
    {
        var main = CreateMainViewModel(out _, out var associate, out _);

        main.ShowAssociateSectionCommand.Execute(null);
        main.ShowHistorySectionCommand.Execute(null);
        main.ShowDashboardSectionCommand.Execute(null);

        Assert.Equal(AssociateSection.Overview, associate.SelectedSection);
    }

    // Phase 10C Part I: History's own selection state survives a Dashboard round trip — History is
    // constructed once and kept alive, so navigating away and back never resets SelectedItem.
    [Fact]
    public async Task NavigatingHistoryThenDashboardThenHistory_PreservesHistorySelection()
    {
        var main = CreateMainViewModel(out _, out _, out var history);
        await history.PendingLoad;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        main.ShowDashboardSectionCommand.Execute(null);
        main.ShowHistorySectionCommand.Execute(null);

        Assert.Equal(HistoryDate, history.SelectedItem?.PassdownDate);
    }
}
