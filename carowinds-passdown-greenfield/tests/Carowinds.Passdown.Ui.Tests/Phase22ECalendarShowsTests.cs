using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 22E: the Operations Calendar's compact Shows section — counts, wording for every schedule state,
/// the full expanded schedule, and the session-scoped expansion state (collapsed by default, kept across
/// date changes and refreshes, never a network call, never another date's lineup). Presentation, view-model
/// and markup only; the real-window keyboard/automation checks live in <c>Phase15ERealWindowLayoutTests</c>.
/// </summary>
public sealed class Phase22ECalendarShowsTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 15, 0, 0, TimeSpan.Zero); // 11:00 AM ET
    private static readonly DateTimeOffset Checked = new(2026, 10, 17, 14, 32, 0, TimeSpan.Zero); // 10:32 AM ET
    private static readonly DateOnly Busy = new(2026, 10, 24);
    private static readonly DateOnly Other = new(2026, 10, 25);

    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    private static ScheduledShow Show(string id, string name, string? venue, params TimeOnly[] times) => new(id, name, venue, times);

    private static ShowScheduleView Scheduled(DateOnly date, params ScheduledShow[] shows) =>
        new(date, ShowScheduleState.Scheduled, shows, false, Checked, Checked);

    private static readonly ScheduledShow[] SixByFour =
    [
        Show("a", "Show A", "Plaza Stage", T(19), T(20), T(21, 30), T(22, 30)),
        Show("b", "Show B", "Carowinds Theatre", T(12), T(14), T(16), T(18)),
        Show("c", "Show C", null, T(13, 15), T(15, 15), T(17, 15), T(19, 15)),
        Show("d", "Show D", "Harmony Hall", T(19, 30), T(20, 30), T(21, 30), T(22, 30)),
        Show("e", "Show E", "County Fair Stage", T(21), T(22, 15), T(23, 30), T(0, 30)),
        Show("f", "Show F", "Midway Stage", T(11), T(13), T(15), T(17)),
    ];

    // ---------------------------------------------------------------- counting

    [Fact]
    public void SixShowsWithFourPerformancesEach_CountsSixAndTwentyFour()
    {
        var shows = CalendarShowsPresentation.Build(Scheduled(Busy, SixByFour), Now);

        Assert.Equal(6, shows.ShowCount);
        Assert.Equal(24, shows.PerformanceCount);
        Assert.Equal("6 scheduled shows · 24 performances", shows.SummaryText);
        Assert.True(shows.HasSchedule);
        Assert.Null(shows.StatusText);
    }

    [Fact]
    public void DuplicateTimesWithinAShow_AreCountedOnce()
    {
        var shows = CalendarShowsPresentation.Build(Scheduled(Busy, Show("a", "Show A", null, T(19), T(19), T(20), T(20), T(21))), Now);

        Assert.Equal(1, shows.ShowCount);
        Assert.Equal(3, shows.PerformanceCount);
        Assert.Equal("7:00, 8:00, 9:00 PM", Assert.Single(shows.Rows).TimesText);
    }

    [Fact]
    public void DuplicateRecordsOfTheSameShow_MergeIntoOneShow_WithTheUnionOfDistinctTimes()
    {
        var shows = CalendarShowsPresentation.Build(Scheduled(Busy,
            Show("wake", "Wake the Dead", "Carowinds Theatre", T(19), T(20)),
            Show("wake", "Wake the Dead", null, T(20), T(21, 30))), Now);

        var row = Assert.Single(shows.Rows);
        Assert.Equal(3, row.PerformanceCount);
        Assert.Equal("Carowinds Theatre", row.Venue);
        Assert.Equal("1 scheduled show · 3 performances", shows.SummaryText);
    }

    [Fact]
    public void DifferentShowIdentities_WithTheSameName_StayDistinct()
    {
        var shows = CalendarShowsPresentation.Build(Scheduled(Busy,
            Show("p1", "Parade", "Route A", T(14)),
            Show("p2", "Parade", "Route B", T(18))), Now);

        Assert.Equal(2, shows.ShowCount);
        Assert.Equal(2, shows.PerformanceCount);
    }

    [Fact]
    public void ShowsWithoutStartTimes_AreNeverCounted()
    {
        var shows = CalendarShowsPresentation.Build(Scheduled(Busy,
            Show("cat", "Catalog Only Show", "Somewhere"),
            Show("real", "Real Show", "Plaza", T(19))), Now);

        Assert.Equal(1, shows.ShowCount);
        Assert.Equal("Real Show", Assert.Single(shows.Rows).Name);
    }

    [Theory]
    [InlineData(1, 1, "1 scheduled show · 1 performance")]
    [InlineData(1, 2, "1 scheduled show · 2 performances")]
    [InlineData(2, 1, "2 scheduled shows · 2 performances")]
    [InlineData(2, 3, "2 scheduled shows · 6 performances")]
    public void SummaryWording_IsSingularOrPlural(int showCount, int timesPerShow, string expected)
    {
        var shows = Enumerable.Range(0, showCount)
            .Select(i => Show($"s{i}", $"Show {i}", null, Enumerable.Range(0, timesPerShow).Select(t => T(12 + t, i * 5)).ToArray()))
            .ToArray();

        Assert.Equal(expected, CalendarShowsPresentation.Build(Scheduled(Busy, shows), Now).SummaryText);
    }

    [Fact]
    public void LateNightTimes_BelongToTheEndOfTheOperatingDay()
    {
        var shows = CalendarShowsPresentation.Build(Scheduled(Busy,
            Show("late", "After Midnight", null, T(0, 30), T(21)),
            Show("eve", "Evening", null, T(23))), Now);

        Assert.Equal(["After Midnight", "Evening"], shows.Rows.Select(r => r.Name));
        Assert.Equal("9:00 PM, 12:30 AM", shows.Rows[0].TimesText);
        Assert.Equal(3, shows.PerformanceCount);
    }

    [Fact]
    public void Rows_AreOrderedByFirstPerformance_ThenName()
    {
        var shows = CalendarShowsPresentation.Build(Scheduled(Busy, SixByFour), Now);

        Assert.Equal(["Show F", "Show B", "Show C", "Show A", "Show D", "Show E"], shows.Rows.Select(r => r.Name));
    }

    // ---------------------------------------------------------------- expanded rows

    [Fact]
    public void ExpandedRows_ListEveryVerifiedTime_AndTheOfficialVenue()
    {
        var shows = CalendarShowsPresentation.Build(Scheduled(Busy, Show("w", "Wake the Dead", "Carowinds Theatre", T(19), T(20), T(21, 30), T(22, 30))), Now);
        var row = Assert.Single(shows.Rows);

        Assert.Equal("Wake the Dead", row.Name);
        Assert.Equal("7:00, 8:00, 9:30, 10:30 PM", row.TimesText);
        Assert.Equal(" · Carowinds Theatre", row.VenueSuffix);
        Assert.Equal("Wake the Dead, 4 performances: 7:00, 8:00, 9:30, 10:30 PM, Carowinds Theatre", row.AccessibleText);
    }

    [Fact]
    public void ExpandedRows_WithoutAVenue_InventNone()
    {
        var row = Assert.Single(CalendarShowsPresentation.Build(Scheduled(Busy, Show("d", "Drumline", null, T(13, 15))), Now).Rows);

        Assert.False(row.HasVenue);
        Assert.Equal(string.Empty, row.VenueSuffix);
        Assert.Equal("Drumline, 1 performance: 1:15 PM", row.AccessibleText);
    }

    [Fact]
    public void Today_ListsTheWholeSchedule_NotOnlyUpcomingPerformances()
    {
        // 11:00 AM ET: the 10:00 AM performance has started; the Calendar still lists it (planning view).
        var shows = CalendarShowsPresentation.Build(Scheduled(Today, Show("m", "Morning Show", null, T(10), T(14))), Now);

        Assert.Equal("10:00 AM, 2:00 PM", Assert.Single(shows.Rows).TimesText);
        Assert.Equal(2, shows.PerformanceCount);
    }

    // ---------------------------------------------------------------- states

    [Theory]
    [InlineData(ShowScheduleState.NoShows, "No shows scheduled for this date.", false)]
    [InlineData(ShowScheduleState.NotPublished, "Show schedule not published.", false)]
    [InlineData(ShowScheduleState.ParkClosed, "No shows — park closed.", false)]
    [InlineData(ShowScheduleState.CouldNotCheck, "Couldn't check the show schedule.", true)]
    [InlineData(ShowScheduleState.HistoricalNotAvailable, "Show schedule not available for this past date.", false)]
    [InlineData(ShowScheduleState.NotChecked, "Show schedule unavailable — not checked yet.", false)]
    public void NonScheduledStates_SayWhatIsKnown_WithNoCountAndNoDisclosure(ShowScheduleState state, string expected, bool isProblem)
    {
        var shows = CalendarShowsPresentation.Build(new ShowScheduleView(Busy, state, [], state == ShowScheduleState.CouldNotCheck, null, null), Now);

        Assert.Equal(expected, shows.StatusText);
        Assert.Null(shows.SummaryText);
        Assert.False(shows.HasSchedule);
        Assert.Empty(shows.Rows);
        Assert.Equal(0, shows.ShowCount);
        Assert.Equal(isProblem, shows.IsProblem);
    }

    [Fact]
    public void ZeroIsNeverPrinted_UnlessTheOfficialSourceConfirmedIt_AndThenOnlyInWords()
    {
        foreach (var state in Enum.GetValues<ShowScheduleState>())
        {
            var shows = CalendarShowsPresentation.Build(new ShowScheduleView(Busy, state, [], false, null, null), Now);
            Assert.DoesNotContain("0 scheduled", shows.SummaryText ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("0 scheduled", shows.StatusText ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VerifiedSchedule_ShowsWhenItWasChecked()
    {
        var shows = CalendarShowsPresentation.Build(Scheduled(Busy, SixByFour), Now);

        Assert.Equal("Official schedule checked 10:32 AM ET", shows.FreshnessText);
        Assert.False(shows.IsProblem);
    }

    [Fact]
    public void LastKnownGoodAfterAFailedCheck_KeepsTheSchedule_AndSaysTheLatestCheckFailed()
    {
        var view = new ShowScheduleView(Busy, ShowScheduleState.Scheduled, SixByFour, LastCheckFailed: true, Checked.AddDays(-2), Checked);
        var shows = CalendarShowsPresentation.Build(view, Now);

        Assert.Equal("6 scheduled shows · 24 performances", shows.SummaryText);
        Assert.Equal("Official schedule checked Oct 15, 10:32 AM ET · latest check failed", shows.FreshnessText);
        Assert.True(shows.IsProblem);
    }

    [Fact]
    public void PastDate_WithASavedRecord_ShowsOnlyThatRecord()
    {
        var past = new DateOnly(2026, 10, 10);
        var shows = CalendarShowsPresentation.Build(Scheduled(past, Show("x", "Saved Show", null, T(19))), Now);

        Assert.Equal("1 scheduled show · 1 performance", shows.SummaryText);
        Assert.Equal(past, shows.Date);
    }

    [Fact]
    public void PastDate_WithoutARecord_ComposesAsHistoricalNotAvailable()
    {
        var past = new DateOnly(2026, 10, 10);
        var shows = CalendarShowsPresentation.Build(ShowScheduleView.Compose(past, null, Now), Now);

        Assert.Equal("Show schedule not available for this past date.", shows.StatusText);
        Assert.False(shows.HasSchedule);
    }

    // ---------------------------------------------------------------- view model

    private sealed class ScriptedCalendar
    {
        public Dictionary<DateOnly, ShowScheduleView> Shows { get; } = new();

        public FakeOperationsCalendarService Service { get; }

        public ScriptedCalendar()
        {
            Service = new FakeOperationsCalendarService();
            Service.SnapshotFactory = (month, selected) =>
            {
                var snapshot = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                return snapshot with
                {
                    Today = Today,
                    GeneratedAt = Now,
                    Selected = snapshot.Selected with { Shows = Shows.GetValueOrDefault(selected) ?? ShowScheduleView.NotCheckedFor(selected) },
                };
            };
        }
    }

    private static async Task<(OperationsCalendarViewModel ViewModel, ScriptedCalendar Calendar)> CreateAsync()
    {
        var calendar = new ScriptedCalendar();
        calendar.Shows[Busy] = Scheduled(Busy, SixByFour);
        calendar.Shows[Other] = Scheduled(Other, Show("o", "Sunday Brass", "Plaza Stage", T(13), T(15)));
        var viewModel = new OperationsCalendarViewModel(calendar.Service, new FakeClock(Now));
        await viewModel.PendingLoad;
        return (viewModel, calendar);
    }

    [Fact]
    public async Task Calendar_OpensCollapsed()
    {
        var (viewModel, _) = await CreateAsync();

        Assert.False(viewModel.IsShowScheduleExpanded);
        Assert.Equal("View schedule", viewModel.ShowScheduleToggleText);
    }

    [Fact]
    public async Task Toggling_ChangesOnlyPresentation_NeverLoads()
    {
        var (viewModel, calendar) = await CreateAsync();
        viewModel.SelectDateCommand.Execute(Busy);
        await viewModel.PendingLoad;
        var loads = calendar.Service.LoadCalls.Count;
        var shows = viewModel.CalendarShows;
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.IsShowScheduleExpanded = true;
        Assert.Equal("Hide schedule", viewModel.ShowScheduleToggleText);
        viewModel.IsShowScheduleExpanded = false;
        Assert.Equal("View schedule", viewModel.ShowScheduleToggleText);

        Assert.Equal(loads, calendar.Service.LoadCalls.Count);
        Assert.Same(shows, viewModel.CalendarShows);
        Assert.Equal(Busy, viewModel.SelectedDate);
        Assert.DoesNotContain(nameof(OperationsCalendarViewModel.Snapshot), changed);
        Assert.DoesNotContain(nameof(OperationsCalendarViewModel.SelectedDate), changed);
    }

    [Fact]
    public async Task SelectedBusyDate_SummarizesItsOwnSchedule()
    {
        var (viewModel, _) = await CreateAsync();
        viewModel.SelectDateCommand.Execute(Busy);
        await viewModel.PendingLoad;

        Assert.Equal(Busy, viewModel.CalendarShows!.Date);
        Assert.Equal("6 scheduled shows · 24 performances", viewModel.CalendarShows.SummaryText);
    }

    [Fact]
    public async Task ChangingDateWhileExpanded_KeepsExpanded_AndShowsOnlyTheNewDate()
    {
        var (viewModel, calendar) = await CreateAsync();
        viewModel.SelectDateCommand.Execute(Busy);
        await viewModel.PendingLoad;
        viewModel.IsShowScheduleExpanded = true;

        var gate = new TaskCompletionSource<bool>();
        calendar.Service.Gate = gate;
        viewModel.NextDayCommand.Execute(null);

        // While the new date loads, nothing from the previous date's lineup is presented.
        Assert.Equal(Other, viewModel.SelectedDate);
        Assert.Null(viewModel.CalendarShows);

        gate.SetResult(true);
        await viewModel.PendingLoad;

        Assert.True(viewModel.IsShowScheduleExpanded);
        Assert.Equal(Other, viewModel.CalendarShows!.Date);
        Assert.Equal(["Sunday Brass"], viewModel.CalendarShows.Rows.Select(r => r.Name));
        Assert.DoesNotContain(viewModel.CalendarShows.Rows, r => r.Name.StartsWith("Show ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChangingToADateWithoutShows_KeepsThePreference_ButHasNothingToExpand()
    {
        var (viewModel, calendar) = await CreateAsync();
        calendar.Shows[Busy.AddDays(-2)] = new ShowScheduleView(Busy.AddDays(-2), ShowScheduleState.NotPublished, [], false, Checked, Checked);
        viewModel.SelectDateCommand.Execute(Busy);
        await viewModel.PendingLoad;
        viewModel.IsShowScheduleExpanded = true;

        viewModel.SelectDateCommand.Execute(Busy.AddDays(-2));
        await viewModel.PendingLoad;

        Assert.True(viewModel.IsShowScheduleExpanded);
        Assert.False(viewModel.CalendarShows!.HasSchedule);
        Assert.Equal("Show schedule not published.", viewModel.CalendarShows.StatusText);

        viewModel.SelectDateCommand.Execute(Busy);
        await viewModel.PendingLoad;
        Assert.True(viewModel.CalendarShows!.HasSchedule);
    }

    [Fact]
    public async Task MonthNavigation_KeepsThePreference()
    {
        var (viewModel, _) = await CreateAsync();
        viewModel.IsShowScheduleExpanded = true;

        viewModel.NextMonthCommand.Execute(null);
        await viewModel.PendingLoad;
        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.True(viewModel.IsShowScheduleExpanded);
    }

    [Fact]
    public async Task ALocalRefresh_RecomputesTheSummary_KeepingDateAndExpansion()
    {
        var (viewModel, calendar) = await CreateAsync();
        viewModel.SelectDateCommand.Execute(Busy);
        await viewModel.PendingLoad;
        viewModel.IsShowScheduleExpanded = true;

        calendar.Shows[Busy] = Scheduled(Busy, SixByFour[0], SixByFour[1]);
        viewModel.HandleDataChange(new Carowinds.Passdown.Application.Changes.DataChange(Carowinds.Passdown.Application.Changes.DataChangeKind.OfficialData, Busy));
        await viewModel.PendingLoad;

        Assert.Equal(Busy, viewModel.SelectedDate);
        Assert.True(viewModel.IsShowScheduleExpanded);
        Assert.Equal("2 scheduled shows · 8 performances", viewModel.CalendarShows!.SummaryText);
        Assert.Equal(2, viewModel.CalendarShows.Rows.Count);
    }

    [Fact]
    public async Task ARefreshThatFails_KeepsTheLastKnownGoodScheduleWithItsFailureNote()
    {
        var (viewModel, calendar) = await CreateAsync();
        viewModel.SelectDateCommand.Execute(Busy);
        await viewModel.PendingLoad;

        calendar.Shows[Busy] = new ShowScheduleView(Busy, ShowScheduleState.Scheduled, SixByFour, LastCheckFailed: true, Checked, Now);
        viewModel.RequestQuietReload();
        await viewModel.PendingLoad;

        Assert.Equal("6 scheduled shows · 24 performances", viewModel.CalendarShows!.SummaryText);
        Assert.EndsWith("latest check failed", viewModel.CalendarShows.FreshnessText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALoadError_ShowsNoShowsSection_RatherThanAStaleOne()
    {
        var (viewModel, calendar) = await CreateAsync();
        calendar.Service.Exception = new InvalidOperationException("boom");

        viewModel.SelectDateCommand.Execute(Busy);
        await viewModel.PendingLoad;

        Assert.Null(viewModel.CalendarShows);
        Assert.NotNull(viewModel.ErrorMessage);
    }

    [Fact]
    public void ANewCalendarSession_StartsCollapsedAgain()
    {
        var first = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), new FakeClock(Now)) { IsShowScheduleExpanded = true };
        var second = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), new FakeClock(Now));

        Assert.True(first.IsShowScheduleExpanded);
        Assert.False(second.IsShowScheduleExpanded);
    }

    // ---------------------------------------------------------------- markup

    private static string CalendarMarkup() => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml"));

    [Fact]
    public void Markup_UsesTheSharedDisclosure_BoundToTheSessionPreference()
    {
        var markup = CalendarMarkup();

        Assert.Contains("<Expander x:Name=\"ShowScheduleExpander\" Classes=\"disclosure\"", markup);
        Assert.Contains("Header=\"{Binding ShowScheduleToggleText}\"", markup);
        Assert.Contains("IsExpanded=\"{Binding IsShowScheduleExpanded}\"", markup);
        Assert.Contains("IsVisible=\"{Binding CalendarShows.HasSchedule, FallbackValue=False}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Show schedule\"", markup);
    }

    [Fact]
    public void Markup_BindsSummaryStatusRowsAndFreshness_FromTheSelectedDateOnly()
    {
        var markup = CalendarMarkup();

        Assert.Contains("Text=\"{Binding CalendarShows.SummaryText}\"", markup);
        Assert.Contains("Text=\"{Binding CalendarShows.StatusText}\"", markup);
        Assert.Contains("ItemsSource=\"{Binding CalendarShows.Rows}\"", markup);
        Assert.Contains("Text=\"{Binding CalendarShows.FreshnessText}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibleText}\"", markup);
        Assert.DoesNotContain("ShowsPresentation", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Markup_ShowRows_AreDividedRows_NotCards_WithWrappingText()
    {
        var markup = CalendarMarkup();
        var rowTemplate = markup[markup.IndexOf("presentation:CalendarShowRowPresentation", StringComparison.Ordinal)..];
        rowTemplate = rowTemplate[..rowTemplate.IndexOf("</DataTemplate>", StringComparison.Ordinal)];

        Assert.Contains("Classes=\"calendarShowRow\"", rowTemplate);
        Assert.DoesNotContain("Classes=\"panel", rowTemplate, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(rowTemplate, "TextWrapping=\"Wrap\"").Count);
        Assert.Contains("<Style Selector=\"Border.calendarShowRow\">", markup);
    }

    [Fact]
    public void Markup_KeepsEveryExistingDetailAndRoute()
    {
        var markup = CalendarMarkup();

        foreach (var binding in new[]
        {
            // Phase 22F: the heading binds the view model's own SelectedDateHeadingText (it changes before the load).
            "SelectedDateHeadingText", "Presentation.ParkStatusText", "Presentation.EffectiveHoursText",
            "Presentation.ParkHoursVerificationText", "Presentation.ParkHoursStaleNoteText", "Presentation.WaterparkHoursText",
            "Presentation.DailyAdmissionPriceText", "Presentation.PassdownStatusText", "Presentation.PassdownDetailNoteText",
            "Presentation.OperationalNotesCountText", "Presentation.OperationalNotesImportantText", "HistoryStatus.Text",
            "OpenAssociateCommand", "OpenHistoryCommand", "ViewDatePricingCommand", "OpenOperationalNotesCommand",
            "PreviousDayCommand", "NextDayCommand",
        })
        {
            Assert.Contains(binding, markup);
        }
    }

    [Fact]
    public void Markup_DayCellsCarryNoShowTimes()
    {
        var markup = CalendarMarkup();
        var dayTemplate = markup[markup.IndexOf("<DataTemplate x:DataType=\"vm:OperationsCalendarDayViewModel\">", StringComparison.Ordinal)..];
        dayTemplate = dayTemplate[..dayTemplate.IndexOf("</DataTemplate>", StringComparison.Ordinal)];

        Assert.DoesNotContain("Show", dayTemplate.Replace("IsVisible", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
