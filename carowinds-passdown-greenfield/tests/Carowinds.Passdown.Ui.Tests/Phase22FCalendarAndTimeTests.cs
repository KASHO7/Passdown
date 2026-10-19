using System.Globalization;
using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 22F: the Calendar's selected-date panel never shows a previous date's details while (or after) a
/// newly selected date loads, and every operational timestamp is the Eastern wall clock with an "ET" label
/// whatever the host's zone or culture. View-model and presentation level only (mixed partition).
/// </summary>
public class Phase22FCalendarAndTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 15, 0, 0, TimeSpan.Zero); // 11:00 AM EDT
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateOnly Oct16 = new(2026, 10, 16);
    private static readonly DateOnly Oct17 = new(2026, 10, 17);
    private static readonly DateOnly Dec5 = new(2026, 12, 5);

    private static async Task<(OperationsCalendarViewModel ViewModel, FakeOperationsCalendarService Service)> CreateAsync()
    {
        var service = new FakeOperationsCalendarService();
        var viewModel = new OperationsCalendarViewModel(service, new FakeClock(Now));
        await viewModel.PendingLoad;
        return (viewModel, service);
    }

    // ---------------------------------------------------------------- date changes

    [Fact]
    public async Task ChoosingADate_ChangesTheHeadingAtOnce_AndHidesThePreviousDatesDetails()
    {
        var (viewModel, service) = await CreateAsync();
        Assert.True(viewModel.IsSelectedDetailCurrent);

        service.Gate = new TaskCompletionSource<bool>();
        viewModel.SelectDateCommand.Execute(Oct16);

        Assert.Equal("Friday, October 16, 2026", viewModel.SelectedDateHeadingText);
        Assert.False(viewModel.SelectedDateIsToday);
        Assert.False(viewModel.IsSelectedDetailCurrent);
        Assert.Equal("Loading details…", viewModel.SelectedDetailPlaceholderText);
        Assert.Null(viewModel.CalendarShows);
        Assert.Equal(Today, viewModel.Snapshot!.Selected.Date); // the old snapshot is still loaded — and hidden

        service.Gate.SetResult(true);
        await viewModel.PendingLoad;

        Assert.True(viewModel.IsSelectedDetailCurrent);
        Assert.Null(viewModel.SelectedDetailPlaceholderText);
        Assert.Equal(Oct16, viewModel.Snapshot!.Selected.Date);
    }

    [Fact]
    public async Task TheNewDayCell_IsHighlightedAndAnnouncedAsSelected_BeforeItsLoadCompletes()
    {
        var (viewModel, service) = await CreateAsync();
        service.Gate = new TaskCompletionSource<bool>();

        viewModel.SelectDateCommand.Execute(Oct16);

        var oldCell = viewModel.Days.Single(d => d.Date == Today);
        var newCell = viewModel.Days.Single(d => d.Date == Oct16);
        Assert.False(oldCell.IsSelected);
        Assert.True(newCell.IsSelected);
        Assert.Contains("Selected", newCell.AutomationName, StringComparison.Ordinal);
        Assert.DoesNotContain("Selected", oldCell.AutomationName, StringComparison.Ordinal);

        service.Gate.SetResult(true);
        await viewModel.PendingLoad;
        Assert.Single(viewModel.Days, d => d.IsSelected);
        Assert.True(viewModel.Days.Single(d => d.IsSelected).Date == Oct16);
    }

    [Fact]
    public async Task OpenHistory_IsNotOfferedFromThePreviousDatesFinalizedStatus_WhileTheNewDateLoads()
    {
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) =>
            {
                var s = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                return selected == Oct16 ? s with { Selected = s.Selected with { PassdownStatus = OperationsCalendarPassdownStatus.Finalized } } : s;
            },
        };
        var viewModel = new OperationsCalendarViewModel(service, new FakeClock(Now));
        viewModel.SelectDateCommand.Execute(Oct16);
        await viewModel.PendingLoad;
        Assert.True(viewModel.OpenHistoryCommand.CanExecute(null));

        service.Gate = new TaskCompletionSource<bool>();
        viewModel.SelectDateCommand.Execute(Oct17);

        Assert.False(viewModel.OpenHistoryCommand.CanExecute(null));
        service.Gate.SetResult(true);
        await viewModel.PendingLoad;
        Assert.False(viewModel.OpenHistoryCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheMonthTitle_FollowsMonthNavigation_Immediately()
    {
        var (viewModel, service) = await CreateAsync();
        service.Gate = new TaskCompletionSource<bool>();

        viewModel.NextMonthCommand.Execute(null);

        Assert.Equal("November 2026", viewModel.DisplayedMonthText);
        Assert.Equal("Tuesday, November 17, 2026", viewModel.SelectedDateHeadingText);
        Assert.False(viewModel.IsSelectedDetailCurrent);
        service.Gate.SetResult(true);
        await viewModel.PendingLoad;
    }

    [Fact]
    public void RapidSwitching_OnlyTheLatestSelectionBecomesVisible_EvenWhenOlderLoadsFinishLast()
    {
        var service = new HeldCalendarService();
        var viewModel = new OperationsCalendarViewModel(service, new FakeClock(Now));
        service.Complete(0); // the initial load (today)

        viewModel.SelectDateCommand.Execute(Oct16);
        viewModel.SelectDateCommand.Execute(Oct17);
        viewModel.SelectDateCommand.Execute(Dec5);
        Assert.Equal("Saturday, December 5, 2026", viewModel.SelectedDateHeadingText);

        // Completion order: Dec 5 first, then the stale Oct 16 and Oct 17 loads.
        service.Complete(3);
        Assert.Equal(Dec5, viewModel.Snapshot!.Selected.Date);
        Assert.True(viewModel.IsSelectedDetailCurrent);

        service.Complete(1);
        service.Complete(2);
        Assert.Equal(Dec5, viewModel.Snapshot!.Selected.Date);
        Assert.Equal(new DateOnly(2026, 12, 1), viewModel.Snapshot.DisplayedMonth);
        Assert.True(viewModel.IsSelectedDetailCurrent);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task RepeatedDayStepping_EndsOnTheLastDate_WithOneLocalLoadPerStep_AndNoOtherCall()
    {
        var (viewModel, service) = await CreateAsync();
        var before = service.LoadCalls.Count;

        for (var i = 0; i < 5; i++) viewModel.NextDayCommand.Execute(null);
        for (var i = 0; i < 2; i++) viewModel.PreviousDayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Equal(new DateOnly(2026, 10, 20), viewModel.SelectedDate);
        Assert.Equal(new DateOnly(2026, 10, 20), viewModel.Snapshot!.Selected.Date);
        Assert.Equal(7, service.LoadCalls.Count - before); // local reads only; the Calendar has no online dependency
    }

    [Fact]
    public async Task ExpandedShows_StayExpandedAcrossAHeldDateChange()
    {
        var (viewModel, service) = await CreateAsync();
        viewModel.IsShowScheduleExpanded = true;
        service.Gate = new TaskCompletionSource<bool>();

        viewModel.SelectDateCommand.Execute(Oct16);
        Assert.True(viewModel.IsShowScheduleExpanded);
        Assert.Null(viewModel.CalendarShows);

        service.Gate.SetResult(true);
        await viewModel.PendingLoad;
        Assert.True(viewModel.IsShowScheduleExpanded);
        Assert.NotNull(viewModel.CalendarShows);
    }

    [Fact]
    public async Task AFailedLocalRead_InTheSameMonth_KeepsTheGridAndTheChosenDate_AndRestoresNoOtherDatesDetails()
    {
        var (viewModel, service) = await CreateAsync();
        service.Exception = new InvalidOperationException("simulated SQLite read failure");

        viewModel.SelectDateCommand.Execute(Oct16);
        await viewModel.PendingLoad;

        Assert.Equal(Oct16, viewModel.SelectedDate);
        Assert.Equal("Friday, October 16, 2026", viewModel.SelectedDateHeadingText);
        Assert.NotNull(viewModel.Snapshot); // October's grid is still October's data
        Assert.False(viewModel.IsSelectedDetailCurrent);
        Assert.Equal("Details for this date are unavailable.", viewModel.SelectedDetailPlaceholderText);
        Assert.Equal("Couldn't load October 16, 2026 from this computer's saved data. Choose the date again to retry.", viewModel.ErrorMessage);
        Assert.True(viewModel.Days.Single(d => d.Date == Oct16).IsSelected);

        // Retry by choosing the date again.
        service.Exception = null;
        viewModel.SelectDateCommand.Execute(Oct16);
        await viewModel.PendingLoad;
        Assert.True(viewModel.IsSelectedDetailCurrent);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AFailedLocalRead_ForAnotherMonth_ClearsTheGrid_AndNeverInfersAParkState()
    {
        var (viewModel, service) = await CreateAsync();
        service.Exception = new InvalidOperationException("simulated SQLite read failure");

        viewModel.SelectDateCommand.Execute(Dec5);
        await viewModel.PendingLoad;

        Assert.Null(viewModel.Snapshot);
        Assert.Null(viewModel.Presentation);
        Assert.Equal("December 2026", viewModel.DisplayedMonthText);
        Assert.Equal(Dec5, viewModel.SelectedDate);
        Assert.Equal("Unable to load the operations calendar right now.", viewModel.ErrorMessage);
    }

    [Fact]
    public void CalendarMarkup_HeadingBindsTheSelectedDate_AndDetailsAreGatedOnTheCurrentFlag()
    {
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml"));

        Assert.Contains("Text=\"{Binding SelectedDateHeadingText}\"", markup);
        Assert.Contains("Text=\"{Binding DisplayedMonthText}\"", markup);
        Assert.Contains("x:Name=\"SelectedDateDetails\" Spacing=\"14\" IsVisible=\"{Binding IsSelectedDetailCurrent}\"", markup);
        Assert.Contains("IsVisible=\"{Binding !IsSelectedDetailCurrent}\"", markup);
        Assert.DoesNotContain("Presentation.SelectedDateText", markup);

        // Every Presentation.* detail binding sits inside the gated details panel.
        var start = markup.IndexOf("x:Name=\"SelectedDateDetails\"", StringComparison.Ordinal);
        var end = markup.IndexOf("Open Passdown", start, StringComparison.Ordinal);
        var outside = markup[..start] + markup[end..];
        Assert.DoesNotMatch(new Regex(@"Binding Presentation\.(?!DisplayedMonthText)"), outside);
        Assert.DoesNotContain("Binding CalendarShows", outside);
    }

    // ---------------------------------------------------------------- Eastern timestamps

    [Theory]
    [InlineData("2026-10-17T10:05:00Z", "Oct 17 at 6:05 AM ET")]   // EDT (UTC−4)
    [InlineData("2026-01-15T11:05:00Z", "Jan 15 at 6:05 AM ET")]   // EST (UTC−5)
    [InlineData("2026-03-08T06:59:00Z", "Mar 8 at 1:59 AM ET")]    // last minute of EST
    [InlineData("2026-03-08T07:00:00Z", "Mar 8 at 3:00 AM ET")]    // spring forward: 2 AM never happens
    [InlineData("2026-11-01T05:30:00Z", "Nov 1 at 1:30 AM ET")]    // first 1:30 AM (EDT)
    [InlineData("2026-11-01T06:30:00Z", "Nov 1 at 1:30 AM ET")]    // second 1:30 AM (EST)
    [InlineData("2026-10-18T00:30:00Z", "Oct 17 at 8:30 PM ET")]   // after midnight UTC, still the 17th in Eastern
    [InlineData("2027-01-01T04:30:00Z", "Dec 31 at 11:30 PM ET")]  // year boundary in UTC only
    [InlineData("2026-10-18T05:10:00Z", "Oct 18 at 1:10 AM ET")]   // a late-night operating day's after-midnight close
    public void DateAndTime_IsTheEasternWallClock_WithItsZoneLabel(string utc, string expected)
    {
        var instant = DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);
        Assert.Equal(expected, EasternTimestampFormat.DateAndTime(instant));
    }

    [Fact]
    public void TheInstantsOwnOffset_NeverChangesTheText()
    {
        var utc = new DateTimeOffset(2026, 10, 17, 10, 5, 0, TimeSpan.Zero);
        var tokyo = utc.ToOffset(TimeSpan.FromHours(9));
        var pacific = utc.ToOffset(TimeSpan.FromHours(-7));

        Assert.Equal("Oct 17 at 6:05 AM ET", EasternTimestampFormat.DateAndTime(tokyo));
        Assert.Equal("Oct 17 at 6:05 AM ET", EasternTimestampFormat.DateAndTime(pacific));
        Assert.Equal("6:05 AM ET", EasternTimestampFormat.Time(tokyo));
        Assert.Equal("Oct 17, 2026 at 6:05 AM ET", EasternTimestampFormat.DateYearAndTime(pacific));
    }

    [Theory]
    [InlineData("2026-10-17T10:05:00Z", "2026-10-17T15:00:00Z", "6:05 AM ET")]          // same Eastern day
    [InlineData("2026-10-17T03:59:00Z", "2026-10-17T15:00:00Z", "Oct 16, 11:59 PM ET")] // earlier Eastern day, same UTC day
    [InlineData("2026-10-18T01:00:00Z", "2026-10-18T03:00:00Z", "9:00 PM ET")]           // both after midnight UTC, same Eastern day
    public void Relative_OmitsTheDate_OnlyForTheSameEasternDay(string instantUtc, string nowUtc, string expected)
    {
        Assert.Equal(expected, EasternTimestampFormat.Relative(DateTimeOffset.Parse(instantUtc, CultureInfo.InvariantCulture), DateTimeOffset.Parse(nowUtc, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    [InlineData("ar-SA")]
    public void Timestamps_StayUsFormatted_UnderAnyHostCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("Oct 17 at 6:05 AM ET", EasternTimestampFormat.DateAndTime(new DateTimeOffset(2026, 10, 17, 10, 5, 0, TimeSpan.Zero)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void NoDisplayCode_ReadsTheHostTimeZone()
    {
        // Host-zone independence by construction: presentation and document code never converts with the
        // PC's zone. The one allowed use is Associate's "is it today on this PC?" highlight (Phase 21B).
        var root = Path.GetDirectoryName(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj"))!;
        var documents = Path.Combine(Path.GetDirectoryName(root)!, "Carowinds.Passdown.Documents");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(documents, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(l => l.Text.Contains("ToLocalTime(", StringComparison.Ordinal) || l.Text.Contains(".LocalDateTime", StringComparison.Ordinal)
                || (l.Text.Contains("TimeZoneInfo.Local", StringComparison.Ordinal) && !l.Text.Contains("ShouldHighlightHostToday", StringComparison.Ordinal)))
            .Select(l => $"{l.File}:{l.Line}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void FinalizedTimes_UseTheEasternDate_AcrossAUtcYearBoundary_AndLeaveTheRecordUntouched()
    {
        var finalizedUtc = new DateTimeOffset(2027, 1, 1, 4, 30, 0, TimeSpan.Zero); // Dec 31, 11:30 PM EST
        var summary = new FinalizedPassdownSummary(new DateOnly(2026, 12, 31), finalizedUtc, FinalizationOrigin.Manual, true, 1000);

        var row = PassdownHistorySummaryPresentation.Build(summary);

        Assert.Equal("Finalized Dec 31, 2026 at 11:30 PM ET", row.FinalizedText);
        Assert.Equal("Finalized Dec 31, 11:30 PM ET · Manual", row.ListDetailText);
        Assert.Equal(finalizedUtc, summary.FinalizedAtUtc);
    }

    [Fact]
    public void ScheduleTimesAndDates_NeverGetAZoneLabel()
    {
        // Park hours and show times are operating-day schedule values; price-history and note dates are dates.
        var calendar = OperationsCalendarPresentation.Build(FakeOperationsCalendarService.DefaultSnapshot(new DateOnly(2026, 10, 1), Today));
        Assert.DoesNotContain(" ET", calendar.SelectedDateText, StringComparison.Ordinal);
        Assert.DoesNotContain(" ET", calendar.DisplayedMonthText, StringComparison.Ordinal);

        var compact = OperationsCalendarOperatingStatePresentation.Build(OperationsCalendarOperatingState.Open, OpenHours(10, 22));
        Assert.Equal("10 AM–10 PM", compact.CompactHoursLine);
    }

    [Fact]
    public void LastSynced_CarriesItsDate_AndTheZone()
    {
        Assert.Equal("Last synced: Oct 17 at 10:48 AM ET", PersonalSyncAutomationStatusPresentation.BuildLastSyncedText(new DateTimeOffset(2026, 10, 17, 14, 48, 0, TimeSpan.Zero)));
        Assert.Null(PersonalSyncAutomationStatusPresentation.BuildLastSyncedText(null));
    }

    private static AssociateParkHoursView OpenHours(int open, int close) => new(
        ParkHoursCalculator.Calculate(false, new TimeOnly(open, 0), new TimeOnly(close, 0), false, null, null, false, null, null),
        new AssociateVerificationInfo(AssociateVerificationState.Verified, Now, null, null),
        OfficialIsClosed: false, OfficialOpening: new TimeOnly(open, 0), OfficialClosing: new TimeOnly(close, 0));

    /// <summary>Holds every load until the test completes it by index — for out-of-order completion.</summary>
    private sealed class HeldCalendarService : IOperationsCalendarService
    {
        private readonly List<(DateOnly Month, DateOnly Selected, TaskCompletionSource<OperationsCalendarSnapshot> Completion)> _calls = [];

        public Task<OperationsCalendarSnapshot> LoadAsync(DateOnly displayedMonth, DateOnly selectedDate, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<OperationsCalendarSnapshot>();
            _calls.Add((displayedMonth, selectedDate, completion));
            return completion.Task;
        }

        public void Complete(int index)
        {
            var (month, selected, completion) = _calls[index];
            completion.SetResult(FakeOperationsCalendarService.DefaultSnapshot(month, selected));
        }
    }
}
