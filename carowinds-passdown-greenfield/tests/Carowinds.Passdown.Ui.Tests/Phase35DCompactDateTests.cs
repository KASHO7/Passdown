using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35D follow-up (docs/phase35d-shows-notes-shared-polish.md §15): the compact Passdown date selector — "Nov 26,
/// 2026" on the button, the full date in the tooltip and help text, no Today badge, and the Today button shown only
/// when another Eastern operating date is selected. Rollover comes from the existing OperatingDay change (no timer).
/// </summary>
public class Phase35DCompactDateTests
{
    /// <summary>2026-11-26 10:00 Eastern.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 26);

    private static string Markup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));

    private static async Task<(AssociateViewModel ViewModel, FakeAssociatePassdownService Service, FakeClock Clock, ManualDebounceScheduler Autosave)> CreateAsync(
        DateTimeOffset now, IDataChangeNotifier? notifier = null)
    {
        var service = new FakeAssociatePassdownService();
        var clock = new FakeClock(now);
        var autosave = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, clock, autosaveDelay: autosave.Delay, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        await viewModel.PendingLoad;
        return (viewModel, service, clock, autosave);
    }

    [Theory]
    [InlineData(2026, 11, 26, "Nov 26, 2026", "Thursday, November 26, 2026")]
    [InlineData(2026, 12, 31, "Dec 31, 2026", "Thursday, December 31, 2026")]
    [InlineData(2027, 1, 1, "Jan 1, 2027", "Friday, January 1, 2027")]
    [InlineData(2026, 9, 30, "Sep 30, 2026", "Wednesday, September 30, 2026")]
    [InlineData(2026, 5, 4, "May 4, 2026", "Monday, May 4, 2026")]
    public void CompactDate_IsShortUsFormat_AndTheFullDateStaysAvailable(int y, int m, int d, string compact, string full)
    {
        var presentation = AssociateDateNavigationPresentation.Build(new DateOnly(y, m, d), Today);

        Assert.Equal(compact, presentation.CompactDateText);
        Assert.Equal(full, presentation.DateText);
        Assert.StartsWith(full, presentation.ToolTipText, StringComparison.Ordinal);
        Assert.Contains(full, presentation.AutomationText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false, "Thursday, November 26, 2026 (Today)")]
    [InlineData(-1, true, "Wednesday, November 25, 2026 (Yesterday)")]
    [InlineData(1, true, "Friday, November 27, 2026 (Tomorrow)")]
    [InlineData(-45, true, "Monday, October 12, 2026")]
    [InlineData(400, true, "Friday, December 31, 2027")]
    public void TodayAction_ShowsOnlyForAnotherDate(int offset, bool showsToday, string toolTip)
    {
        var presentation = AssociateDateNavigationPresentation.Build(Today.AddDays(offset), Today);

        Assert.Equal(showsToday, presentation.ShowsTodayAction);
        Assert.Equal(toolTip, presentation.ToolTipText);
    }

    [Fact]
    public async Task SelectedToday_HidesToday_OtherDatesShowIt_AndTodayReturns()
    {
        var (viewModel, _, _, _) = await CreateAsync(Now);
        Assert.Equal(Today, viewModel.SelectedDate);
        Assert.Equal("Nov 26, 2026", viewModel.DateNavigation.CompactDateText);
        Assert.False(viewModel.DateNavigation.ShowsTodayAction);

        viewModel.PreviousDayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.True(viewModel.DateNavigation.ShowsTodayAction);
        Assert.Equal("Nov 25, 2026", viewModel.DateNavigation.CompactDateText);

        viewModel.NextDayCommand.Execute(null);
        viewModel.NextDayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.True(viewModel.DateNavigation.ShowsTodayAction);

        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal(Today, viewModel.SelectedDate);
        Assert.False(viewModel.DateNavigation.ShowsTodayAction);
    }

    [Fact]
    public async Task YearBoundary_DecemberThirtyFirst_ToJanuaryFirst()
    {
        // 00:30 Eastern, Jan 1 2027 (05:30 UTC).
        var (viewModel, _, _, _) = await CreateAsync(new DateTimeOffset(2027, 1, 1, 5, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2027, 1, 1), viewModel.SelectedDate);
        Assert.Equal("Jan 1, 2027", viewModel.DateNavigation.CompactDateText);
        Assert.False(viewModel.DateNavigation.ShowsTodayAction);

        viewModel.PreviousDayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal("Dec 31, 2026", viewModel.DateNavigation.CompactDateText);
        Assert.Equal("Yesterday", viewModel.DateNavigation.RelativeText);
        Assert.True(viewModel.DateNavigation.ShowsTodayAction);

        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal(new DateOnly(2027, 1, 1), viewModel.SelectedDate);
    }

    [Theory]
    // 22:30 Eastern Nov 26 = 03:30 UTC Nov 27 = 12:30 Tokyo Nov 27: still Nov 26 for Carowinds.
    [InlineData(2026, 11, 27, 3, 30, 2026, 11, 26)]
    // 23:30 Pacific Nov 26 = 02:30 Eastern Nov 27: already Nov 27 for Carowinds while a Pacific PC says Nov 26.
    [InlineData(2026, 11, 27, 7, 30, 2026, 11, 27)]
    // DST fall-back night: 01:30 EDT (first pass) Nov 1.
    [InlineData(2026, 11, 1, 5, 30, 2026, 11, 1)]
    public async Task Today_IsTheCarowindsEasternDate_NotTheComputersOrUtc(int uy, int um, int ud, int uh, int umin, int ey, int em, int ed)
    {
        var (viewModel, _, _, _) = await CreateAsync(new DateTimeOffset(uy, um, ud, uh, umin, 0, TimeSpan.Zero));
        var eastern = new DateOnly(ey, em, ed);

        Assert.Equal(eastern, viewModel.EasternToday);
        Assert.Equal(eastern, viewModel.SelectedDate);
        Assert.False(viewModel.DateNavigation.ShowsTodayAction);

        viewModel.SelectedDate = eastern.AddDays(1); // e.g. the computer's own (UTC / Tokyo) date
        await viewModel.PendingLoad;
        Assert.True(viewModel.DateNavigation.ShowsTodayAction);
        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal(eastern, viewModel.SelectedDate);
    }

    [Fact]
    public async Task EasternMidnight_OperatingDayChange_ShowsToday_WithoutChangingTheDateOrDraft()
    {
        var notifier = new DataChangeNotifier();
        // 23:50 Eastern Nov 26.
        var (viewModel, service, clock, autosave) = await CreateAsync(new DateTimeOffset(2026, 11, 27, 4, 50, 0, TimeSpan.Zero), notifier);
        Assert.False(viewModel.DateNavigation.ShowsTodayAction);
        viewModel.OperationalNotesText = "Unsaved edit on Nov 26";
        var loads = service.LoadCalls.Count;
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        clock.UtcNow = new DateTimeOffset(2026, 11, 27, 5, 10, 0, TimeSpan.Zero); // 00:10 Eastern Nov 27
        notifier.Publish(new DataChange(DataChangeKind.OperatingDay, new DateOnly(2026, 11, 27)));

        Assert.Contains(nameof(AssociateViewModel.DateNavigation), changed);
        Assert.True(viewModel.DateNavigation.ShowsTodayAction);
        Assert.Equal("Yesterday", viewModel.DateNavigation.RelativeText);
        Assert.Equal(Today, viewModel.SelectedDate); // never switched silently
        Assert.Equal("Unsaved edit on Nov 26", viewModel.OperationalNotesText);
        Assert.Equal(loads, service.LoadCalls.Count);
        Assert.Empty(service.CheckOnlineCalls);

        // Today then saves the Nov 26 edit to Nov 26 before loading Nov 27.
        viewModel.GoToTodayCommand.Execute(null);
        autosave.ElapseLatest();
        await viewModel.PendingLoad;
        Assert.Equal(new DateOnly(2026, 11, 27), viewModel.SelectedDate);
        var saved = Assert.Single(service.SaveCalls, c => c.OperatingDate == Today);
        Assert.Equal(["Unsaved edit on Nov 26"], saved.Draft.OperationalNotes);
        Assert.DoesNotContain(service.SaveCalls, c => c.OperatingDate == new DateOnly(2026, 11, 27) && c.Draft.OperationalNotes.Contains("Unsaved edit on Nov 26"));
    }

    [Fact]
    public async Task WindowActivation_RefreshesTheTodayButton_Only()
    {
        var (associate, service, clock, _) = await CreateAsync(new DateTimeOffset(2026, 11, 27, 4, 50, 0, TimeSpan.Zero));
        var main = new MainViewModel(
            new OperationsDashboardViewModel(new FakeOperationsDashboardService()),
            new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock),
            associate,
            new OperationalNotesViewModel(new FakeOperationalNotesService(), clock),
            new PassdownHistoryViewModel(new FakePassdownHistoryService { Summaries = [new FinalizedPassdownSummary(Today, Now, FinalizationOrigin.Scheduled, true, 100)] }, new FakeFinalizedPassdownPdfRetrievalService()),
            new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator()));
        var changed = new List<string?>();
        associate.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var loads = service.LoadCalls.Count;

        clock.UtcNow = new DateTimeOffset(2026, 11, 27, 13, 0, 0, TimeSpan.Zero); // the PC slept past midnight
        main.OnWindowActivated();

        Assert.Contains(nameof(AssociateViewModel.DateNavigation), changed);
        Assert.True(associate.DateNavigation.ShowsTodayAction);
        Assert.Equal(Today, associate.SelectedDate);
        Assert.Equal(loads, service.LoadCalls.Count);
    }

    [Fact]
    public void Markup_CompactDate_NoBadge_ConditionalToday_SameCommandsAndNames()
    {
        var markup = Markup;
        var dateButton = Regex.Match(markup, "<Button Grid.Column=\"1\" x:Name=\"DateButton\".*?</Button>", RegexOptions.Singleline).Value;

        Assert.Contains("Text=\"{Binding DateNavigation.CompactDateText}\"", dateButton);
        Assert.Contains("ToolTip.Tip=\"{Binding DateNavigation.ToolTipText}\"", dateButton);
        Assert.Contains("AutomationProperties.Name=\"Operating date\"", dateButton);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding DateNavigation.AutomationText}\"", dateButton);
        Assert.Contains("x:Name=\"DateCalendar\"", dateButton); // the date picker is unchanged
        Assert.Contains("x:Name=\"CalendarTodayButton\"", dateButton);
        Assert.DoesNotContain("relativeDayChip", markup);
        Assert.DoesNotContain("RelativeText}", dateButton);

        var today = Regex.Match(markup, "<Button Grid.Column=\"3\" x:Name=\"DateTodayButton\".*?/>", RegexOptions.Singleline).Value;
        Assert.Contains("Command=\"{Binding GoToTodayCommand}\"", today);
        Assert.Contains("IsVisible=\"{Binding DateNavigation.ShowsTodayAction}\"", today);
        Assert.Contains("AutomationProperties.Name=\"Today\"", today);
        Assert.Contains("Click=\"DateTodayButton_OnClick\"", today);

        Assert.Contains("Command=\"{Binding PreviousDayCommand}\"", markup);
        Assert.Contains("Command=\"{Binding NextDayCommand}\"", markup);
        Assert.DoesNotContain("<Setter Property=\"MinWidth\" Value=\"240\" />", markup);
    }
}
