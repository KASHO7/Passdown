using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35E (docs/phase35e-dashboard-calendar-redesign.md): the redesigned Operations Calendar — the day cell's
/// quiet passdown line, five- and six-row months, Eastern operating dates across daylight-saving and year boundaries,
/// month navigation, routing with the selected date, and the markup contracts of the new layout. View-model
/// assertions and markup binding strings only (this assembly's thread-affinity rule); realized geometry lives in the
/// run-alone <see cref="OperationsCalendarViewTests"/> partial.
/// </summary>
public class Phase35ECalendarTests
{
    private static string Markup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/OperationsCalendarView.axaml"));

    private static string Tokens => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));

    private static async Task<(OperationsCalendarViewModel ViewModel, FakeOperationsCalendarService Service)> CalendarAt(DateTimeOffset utcNow)
    {
        var service = new FakeOperationsCalendarService();
        var viewModel = new OperationsCalendarViewModel(service, new FakeClock(utcNow));
        await viewModel.PendingLoad;
        return (viewModel, service);
    }

    private static OperationsCalendarDayViewModel Cell(bool finalized, bool draft)
    {
        var day = FakeOperationsCalendarService.DefaultSnapshot(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 22)).Days
            .Single(d => d.Date == new DateOnly(2026, 11, 21)) with { HasFinalizedPassdown = finalized, HasAssociateDraft = draft };
        return new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });
    }

    // ------------------------------------------------------------------ day cell

    [Fact]
    public void DayCell_ShowsAPassdownLineOnlyForADraftOrAFinalizedDate()
    {
        var none = Cell(finalized: false, draft: false);
        var draft = Cell(finalized: false, draft: true);
        var finalized = Cell(finalized: true, draft: true);

        Assert.Null(none.PassdownCellText);
        Assert.False(none.HasPassdownCellText);
        Assert.Contains("No draft", none.AutomationName, StringComparison.Ordinal); // still announced, just not printed in the cell

        Assert.Equal("Draft in progress", draft.PassdownCellText);
        Assert.Equal("Finalized", finalized.PassdownCellText); // Finalized wins over a lingering draft row
        Assert.DoesNotContain("Draft in progress", finalized.AutomationName, StringComparison.Ordinal);

        // The Phase 12C labels are unchanged for every other consumer.
        Assert.Equal("No Draft", none.PassdownStatusLabel);
        Assert.Equal("Draft", draft.PassdownStatusLabel);
        Assert.Equal("Finalized", finalized.PassdownStatusLabel);
    }

    // ------------------------------------------------------------------ month shapes

    [Theory]
    [InlineData(2026, 11, 5)] // starts Sunday, 30 days
    [InlineData(2026, 8, 6)] // starts Saturday, 31 days
    [InlineData(2026, 2, 4)] // starts Sunday, 28 days — no fabricated rows
    [InlineData(2027, 1, 6)] // starts Friday, 31 days
    public async Task Weeks_AreCompleteSundayToSaturdayRows_FourToSix(int year, int month, int expectedWeeks)
    {
        var (viewModel, _) = await CalendarAt(new DateTimeOffset(year, month, 15, 15, 0, 0, TimeSpan.Zero));

        Assert.Equal(expectedWeeks, viewModel.Weeks.Count);
        Assert.All(viewModel.Weeks, week =>
        {
            Assert.Equal(7, week.Days.Count);
            Assert.Equal(Enumerable.Range(0, 7), week.Days.Select(d => d.ColumnIndex));
        });
        Assert.Equal(DateTime.DaysInMonth(year, month), viewModel.Days.Count(d => d.IsInDisplayedMonth));
    }

    // ------------------------------------------------------------------ Eastern operating date

    [Theory]
    [InlineData("2026-11-01T03:30:00Z", 2026, 10, 31)] // 11:30 PM EDT on Oct 31, the night before daylight saving ends
    [InlineData("2026-11-01T06:30:00Z", 2026, 11, 1)] // 1:30 AM EST after the fall-back hour
    [InlineData("2026-03-08T04:30:00Z", 2026, 3, 7)] // 11:30 PM EST, the night before daylight saving starts
    [InlineData("2026-03-08T07:30:00Z", 2026, 3, 8)] // 3:30 AM EDT
    [InlineData("2027-01-01T04:59:00Z", 2026, 12, 31)] // 11:59 PM EST on New Year's Eve — still December 2026
    [InlineData("2027-01-01T05:00:00Z", 2027, 1, 1)]
    public async Task TodayIsTheCarowindsEasternDate_NotUtcOrTheComputersZone(string utc, int year, int month, int day)
    {
        var (viewModel, service) = await CalendarAt(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));
        var expected = new DateOnly(year, month, day);

        Assert.Equal(expected, viewModel.SelectedDate);
        Assert.Equal(new DateOnly(year, month, 1), viewModel.DisplayedMonth);
        Assert.True(viewModel.SelectedDateIsToday);
        Assert.Equal((new DateOnly(year, month, 1), expected), Assert.Single(service.LoadCalls));
    }

    // ------------------------------------------------------------------ navigation

    [Fact]
    public async Task MonthNavigation_CrossesTheYearBoundary_AndTodayReturns_OneLoadPerAction()
    {
        var (viewModel, service) = await CalendarAt(new DateTimeOffset(2026, 12, 31, 17, 0, 0, TimeSpan.Zero));

        viewModel.NextMonthCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal("January 2027", viewModel.DisplayedMonthText);
        Assert.Equal(new DateOnly(2027, 1, 31), viewModel.SelectedDate);

        viewModel.NextMonthCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal(new DateOnly(2027, 2, 28), viewModel.SelectedDate); // clamped, never a silent other date

        viewModel.PreviousMonthCommand.Execute(null);
        viewModel.PreviousMonthCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal("December 2026", viewModel.DisplayedMonthText);
        Assert.Equal(new DateOnly(2026, 12, 28), viewModel.SelectedDate);

        viewModel.GoToTodayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal(new DateOnly(2026, 12, 31), viewModel.SelectedDate);
        Assert.Equal(6, service.LoadCalls.Count); // construction + five actions, each exactly one local read
    }

    [Fact]
    public async Task ChoosingAnAdjacentMonthCell_MovesToItsMonth_AndRoutesCarryThatDate()
    {
        var (viewModel, _) = await CalendarAt(new DateTimeOffset(2026, 11, 22, 15, 0, 0, TimeSpan.Zero));
        DateOnly? passdown = null, pricing = null;
        viewModel.OpenAssociateRequested += (_, d) => passdown = d;
        viewModel.ViewDatePricingRequested += (_, d) => pricing = d;

        var december5 = viewModel.Days.Single(d => d.Date == new DateOnly(2026, 12, 5));
        Assert.False(december5.IsInDisplayedMonth);
        december5.SelectCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Equal("December 2026", viewModel.DisplayedMonthText);
        Assert.True(viewModel.Days.Single(d => d.Date == new DateOnly(2026, 12, 5)).IsSelected);

        viewModel.OpenAssociateCommand.Execute(null);
        viewModel.ViewDatePricingCommand.Execute(null);
        Assert.Equal(new DateOnly(2026, 12, 5), passdown);
        Assert.Equal(new DateOnly(2026, 12, 5), pricing);
    }

    [Fact]
    public async Task FinalizedHistory_IsOfferedOnlyForAFinalizedDate()
    {
        var service = new FakeOperationsCalendarService
        {
            SnapshotFactory = (month, selected) =>
            {
                var snapshot = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
                return selected == new DateOnly(2026, 11, 21)
                    ? snapshot with { Selected = snapshot.Selected with { PassdownStatus = OperationsCalendarPassdownStatus.Finalized } }
                    : snapshot;
            },
        };
        var viewModel = new OperationsCalendarViewModel(service, new FakeClock(new DateTimeOffset(2026, 11, 22, 15, 0, 0, TimeSpan.Zero)));
        await viewModel.PendingLoad;
        DateOnly? history = null;
        viewModel.OpenHistoryRequested += (_, d) => history = d;

        Assert.False(viewModel.OpenHistoryCommand.CanExecute(null));

        viewModel.PreviousDayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.True(viewModel.OpenHistoryCommand.CanExecute(null));
        viewModel.OpenHistoryCommand.Execute(null);
        Assert.Equal(new DateOnly(2026, 11, 21), history);
    }

    [Fact]
    public async Task ExpandingTheShowSchedule_NeverReadsAgain()
    {
        var (viewModel, service) = await CalendarAt(new DateTimeOffset(2026, 11, 22, 15, 0, 0, TimeSpan.Zero));
        var loads = service.LoadCalls.Count;

        viewModel.IsShowScheduleExpanded = true;
        viewModel.IsShowScheduleExpanded = false;

        Assert.Equal(loads, service.LoadCalls.Count);
    }

    // ------------------------------------------------------------------ markup

    [Theory]
    [InlineData("Text=\"{Binding PassdownCellText}\" Classes=\"dayPassdownLine\" IsVisible=\"{Binding HasPassdownCellText}\"")]
    [InlineData("<PathIcon Classes=\"passPerkMarker redemption\" Data=\"{StaticResource PassdownGiftIconGeometry}\"")]
    [InlineData("<PathIcon Classes=\"passPerkMarker deadline\" Data=\"{StaticResource PassdownClockIconGeometry}\"")]
    [InlineData("<PathIcon Classes=\"passPerkLegendIcon redemption\" Data=\"{StaticResource PassdownGiftIconGeometry}\"")]
    [InlineData("<PathIcon Classes=\"passPerkLegendIcon deadline\" Data=\"{StaticResource PassdownClockIconGeometry}\"")]
    [InlineData("IsChecked=\"{Binding CalendarDisplay.ShowPassPerks, FallbackValue=False}\"")]
    [InlineData("IsVisible=\"{Binding CanTogglePassPerks}\"")]
    [InlineData("AutomationProperties.Name=\"Show Pass Perks on Calendar\"")]
    [InlineData("<ScrollViewer Grid.Column=\"0\" x:Name=\"CalendarScroll\" HorizontalScrollBarVisibility=\"Disabled\">")]
    [InlineData("<ScrollViewer Grid.Row=\"1\" x:Name=\"SelectedDateScroll\" HorizontalScrollBarVisibility=\"Disabled\">")]
    [InlineData("<Setter Property=\"Width\" Value=\"290\" />")]
    [InlineData("not rewards earned by an individual guest.")]
    [InlineData("Content=\"View Finalized History\"")]
    [InlineData("Classes.dated=\"{Binding MarkerKind, Converter={x:Static ObjectConverters.IsNotNull}}\"")]
    public void CalendarMarkup_HasThePhase35ELayoutContracts(string expected) =>
        Assert.Contains(expected, Markup, StringComparison.Ordinal);

    [Fact]
    public void CalendarMarkup_StartsNoRetrieval_PrintsNoNoDraftLabel_AndHasNoEmoji()
    {
        var markup = Markup;

        // Presentation only: the Calendar owns no refresh, so there is no Check Online / refresh control to fire one.
        Assert.DoesNotContain("CheckOnline", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Refresh", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding PassdownStatusLabel}", markup, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"[\uD800-\uDBFF][\uDC00-\uDFFF]|[☀-➿]"), markup);
    }

    [Theory]
    [InlineData("PassdownPerkQualifyingBrush")]
    [InlineData("PassdownPerkRedemptionBrush")]
    [InlineData("PassdownPerkDeadlineBrush")]
    public void PerkMarkerColours_AreDefinedForLightAndDark(string key) =>
        Assert.Equal(2, Regex.Matches(Tokens, $"x:Key=\"{key}\"").Count);
}
