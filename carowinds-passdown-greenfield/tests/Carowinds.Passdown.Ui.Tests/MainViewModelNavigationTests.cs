using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 9D Part O/P/T: proves History is additive and never disturbs the Associate workflow — the
/// two child view models are constructed once and kept alive for the app's lifetime, so switching
/// <see cref="MainViewModel.SelectedSection"/> only toggles which one is visible.
/// </summary>
public class MainViewModelNavigationTests
{
    private static readonly DateOnly AssociateDate = new(2026, 8, 26);
    private static readonly DateOnly HistoryDate = new(2026, 8, 25);

    private static MainViewModel CreateMainViewModel(out AssociateViewModel associate, out PassdownHistoryViewModel history)
    {
        associate = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(new DateTimeOffset(AssociateDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)));
        var historyService = new FakePassdownHistoryService
        {
            Summaries = [new FinalizedPassdownSummary(HistoryDate, DateTimeOffset.UtcNow, FinalizationOrigin.Scheduled, true, 100)],
        };
        history = new PassdownHistoryViewModel(historyService, new FakeFinalizedPassdownPdfRetrievalService());
        return new MainViewModel(associate, history);
    }

    [Fact]
    public void SelectedSection_DefaultsToAssociatePassdown()
    {
        var main = CreateMainViewModel(out _, out _);

        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);
        Assert.True(main.IsAssociateSectionSelected);
        Assert.False(main.IsHistorySectionSelected);
    }

    [Fact]
    public void ShowHistorySectionCommand_SwitchesSection()
    {
        var main = CreateMainViewModel(out _, out _);

        main.ShowHistorySectionCommand.Execute(null);

        Assert.Equal(AppSection.History, main.SelectedSection);
        Assert.True(main.IsHistorySectionSelected);
        Assert.False(main.IsAssociateSectionSelected);
    }

    // 23: selecting a History record never changes AssociateViewModel.SelectedDate.
    [Fact]
    public async Task SelectingHistoryRecord_NeverChangesAssociateSelectedDate()
    {
        var main = CreateMainViewModel(out var associate, out var history);
        await associate.PendingLoad;
        await history.PendingLoad;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        Assert.Equal(AssociateDate, associate.SelectedDate);
    }

    // 24: navigating back to Associate preserves its draft/date state — nothing is recreated.
    [Fact]
    public async Task NavigatingToHistoryAndBack_PreservesAssociateState()
    {
        var main = CreateMainViewModel(out var associate, out var history);
        await associate.PendingLoad;
        await history.PendingLoad;

        associate.ProjectedAttendanceText = "9000";
        await associate.PendingAutosave;

        main.ShowHistorySectionCommand.Execute(null);
        history.SelectedItem = history.HistoryItems[0];
        await history.PendingDetailLoad;

        main.ShowAssociateSectionCommand.Execute(null);

        Assert.Same(associate, main.Associate);
        Assert.Equal(AssociateDate, associate.SelectedDate);
        Assert.Equal("9000", associate.ProjectedAttendanceText);
    }
}
