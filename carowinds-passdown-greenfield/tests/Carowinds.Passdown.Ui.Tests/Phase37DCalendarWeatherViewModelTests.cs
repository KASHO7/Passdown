using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37D: the Calendar view model's selected-date Weather group over the app's one weather monitor. It only reads
/// the monitor's cached snapshot: selecting dates and months never requests weather, subscribes once, never rebuilds
/// the month cells for a weather update, and never shows one date's forecast under another date's heading.
/// Synthetic data only (Saturday, October 17, 2026, 9:02 AM EDT).
/// </summary>
public class Phase37DCalendarWeatherViewModelTests
{
    private static readonly DateOnly Today = WeatherUiFixtures.Day;

    /// <summary>Every date open 10 AM–10 PM, except Sundays (closed).</summary>
    private static OperationsCalendarSnapshot OpenSnapshot(DateOnly month, DateOnly selected)
    {
        var snapshot = FakeOperationsCalendarService.DefaultSnapshot(month, selected);
        var closed = selected.DayOfWeek == DayOfWeek.Sunday;
        var hours = closed
            ? ParkHoursCalculator.Calculate(true, null, null, false, null, null, false, null, null)
            : ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(22, 0), false, null, null, false, null, null);
        return snapshot with
        {
            Selected = snapshot.Selected with
            {
                IsToday = selected == Today,
                OperatingState = closed ? OperationsCalendarOperatingState.Closed : OperationsCalendarOperatingState.Open,
                ParkHours = new AssociateParkHoursView(hours, AssociateVerificationInfo.NotCheckedYet, OfficialIsClosed: closed, OfficialOpening: hours.Opening, OfficialClosing: hours.Closing),
            },
        };
    }

    private sealed class QueueDispatcher : IUiDispatcher
    {
        public Queue<Action> Pending { get; } = new();

        public void Post(Action action) => Pending.Enqueue(action);

        public void RunAll()
        {
            while (Pending.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private static async Task<(OperationsCalendarViewModel Calendar, FakeOperationsCalendarService Service, FakeWeatherMonitor Monitor)> CreateAsync(IUiDispatcher? dispatcher = null)
    {
        var service = new FakeOperationsCalendarService { SnapshotFactory = OpenSnapshot };
        var monitor = new FakeWeatherMonitor { Current = WeatherUiFixtures.Normal() };
        var calendar = new OperationsCalendarViewModel(service, new FakeClock(WeatherUiFixtures.Morning), uiDispatcher: dispatcher ?? new ImmediateUiDispatcher(), weatherMonitor: monitor);
        await calendar.PendingLoad;
        return (calendar, service, monitor);
    }

    [Fact]
    public async Task SelectingToday_ShowsTodaysForecastForTheParkHours()
    {
        var (calendar, _, _) = await CreateAsync();

        Assert.True(calendar.HasWeather);
        Assert.Equal(Today, calendar.SelectedDate);
        Assert.NotNull(calendar.Weather);
        Assert.Equal(CalendarWeatherState.Forecast, calendar.Weather!.State);
        Assert.Equal("Park hours, 10 AM–10 PM", calendar.Weather.ScopeText);
        Assert.Contains(calendar.Weather.Rows, row => row is { Label: "High / low", Value: "74° / 55°" });
    }

    [Fact]
    public async Task SelectingTomorrow_UpcomingAndPastDates_EachShowTheirOwnState()
    {
        var (calendar, _, _) = await CreateAsync();

        calendar.NextDayCommand.Execute(null); // Sunday the 18th: closed in this fixture
        await calendar.PendingLoad;
        Assert.Equal(Today.AddDays(1), calendar.SelectedDate);
        Assert.Equal(CalendarWeatherState.Forecast, calendar.Weather!.State);
        Assert.Equal("Park closed · general forecast for the day", calendar.Weather.ScopeText);

        calendar.SelectDateCommand.Execute(Today.AddDays(5));
        await calendar.PendingLoad;
        Assert.Equal(CalendarWeatherState.BeyondHorizon, calendar.Weather!.State);

        calendar.SelectDateCommand.Execute(Today.AddDays(-3));
        await calendar.PendingLoad;
        Assert.Equal(CalendarWeatherState.PastDate, calendar.Weather!.State);
        Assert.Empty(calendar.Weather.Rows);
    }

    [Fact]
    public async Task DateChange_HidesThePreviousDatesWeather_UntilTheNewDatesDetailsLoad()
    {
        var (calendar, service, _) = await CreateAsync();
        Assert.NotNull(calendar.Weather);

        service.Gate = new TaskCompletionSource<bool>();
        calendar.NextDayCommand.Execute(null);

        Assert.Null(calendar.Weather); // never today's forecast under tomorrow's heading
        service.Gate.SetResult(true);
        await calendar.PendingLoad;
        Assert.NotNull(calendar.Weather);
        Assert.Equal("Park closed · general forecast for the day", calendar.Weather!.ScopeText);
    }

    [Fact]
    public async Task RapidDateChanges_EndOnTheLastSelectedDatesWeather()
    {
        var (calendar, service, _) = await CreateAsync();
        service.Gate = new TaskCompletionSource<bool>();

        for (var i = 0; i < 10; i++)
        {
            calendar.NextDayCommand.Execute(null);
        }

        calendar.PreviousDayCommand.Execute(null); // the 26th
        Assert.Null(calendar.Weather);
        service.Gate.SetResult(true);
        await calendar.PendingLoad;

        Assert.Equal(new DateOnly(2026, 10, 26), calendar.SelectedDate);
        Assert.Equal(CalendarWeatherState.BeyondHorizon, calendar.Weather!.State);
    }

    [Fact]
    public async Task DateAndMonthNavigation_NeverRequestWeather_AndSubscribeOnce()
    {
        var (calendar, _, monitor) = await CreateAsync();

        for (var i = 0; i < 20; i++)
        {
            calendar.NextDayCommand.Execute(null);
            await calendar.PendingLoad;
        }

        calendar.NextMonthCommand.Execute(null);
        await calendar.PendingLoad;
        calendar.PreviousMonthCommand.Execute(null);
        await calendar.PendingLoad;
        calendar.GoToTodayCommand.Execute(null);
        await calendar.PendingLoad;
        calendar.IsWeatherHourlyExpanded = true;
        calendar.IsWeatherHourlyExpanded = false;

        Assert.Equal(0, monitor.RefreshNowCount);
        Assert.Equal(0, monitor.StartCount);
        Assert.Equal(1, monitor.ChangedSubscriberCount);
    }

    [Fact]
    public async Task MonitorUpdate_RebuildsOnlyTheWeatherGroup_AndAnUnchangedReportChangesNothing()
    {
        var (calendar, service, monitor) = await CreateAsync();
        var days = calendar.Days;
        var weeks = calendar.Weeks;
        var presentation = calendar.Presentation;
        var before = calendar.Weather;
        var loads = service.LoadCalls.Count;

        monitor.RaiseChanged(); // same snapshot
        Assert.Same(before, calendar.Weather);

        monitor.Current = WeatherUiFixtures.Stale();
        monitor.RaiseChanged();

        Assert.True(calendar.Weather!.IsStale);
        Assert.Same(days, calendar.Days);
        Assert.Same(weeks, calendar.Weeks);
        Assert.Same(presentation, calendar.Presentation);
        Assert.Equal(loads, service.LoadCalls.Count); // no reload of local data either
    }

    [Fact]
    public async Task MonitorReports_AreCoalescedIntoOneUiRebuild()
    {
        var dispatcher = new QueueDispatcher();
        var (calendar, _, monitor) = await CreateAsync(dispatcher);
        dispatcher.RunAll();

        monitor.Current = WeatherUiFixtures.Stale();
        for (var i = 0; i < 5; i++)
        {
            monitor.RaiseChanged(); // the monitor raises from a background thread in the app
        }

        Assert.Single(dispatcher.Pending);
        dispatcher.RunAll();
        Assert.True(calendar.Weather!.IsStale);
    }

    [Fact]
    public async Task HourlyForecast_IsCollapsedByDefault_StaysAsChosenAcrossDates_AndLoadsNothing()
    {
        var (calendar, service, _) = await CreateAsync();
        Assert.False(calendar.IsWeatherHourlyExpanded);
        Assert.Equal("Hourly forecast (12 hours)", calendar.WeatherHourlyToggleText);
        var loads = service.LoadCalls.Count;

        calendar.IsWeatherHourlyExpanded = true;
        Assert.Equal("Hide hourly forecast", calendar.WeatherHourlyToggleText);
        Assert.Equal(loads, service.LoadCalls.Count);

        calendar.NextDayCommand.Execute(null);
        await calendar.PendingLoad;
        Assert.True(calendar.IsWeatherHourlyExpanded);

        calendar.IsWeatherHourlyExpanded = false;
        Assert.StartsWith("Hourly forecast (", calendar.WeatherHourlyToggleText);
    }

    [Fact]
    public async Task WithoutAMonitor_TheCalendarHasNoWeather_AndItsOfficialDataIsUnchanged()
    {
        var service = new FakeOperationsCalendarService { SnapshotFactory = OpenSnapshot };
        var plain = new OperationsCalendarViewModel(service, new FakeClock(WeatherUiFixtures.Morning), uiDispatcher: new ImmediateUiDispatcher());
        await plain.PendingLoad;
        var (withWeather, _, _) = await CreateAsync();

        Assert.False(plain.HasWeather);
        Assert.Null(plain.Weather);
        Assert.Equal(plain.Presentation, withWeather.Presentation); // park status, hours, pricing, passdown, notes
        Assert.Equal(
            plain.Days.Select(d => (d.Date, d.OperatingStateLabel, d.EffectiveHoursLineDisplay, d.PassdownCellText)),
            withWeather.Days.Select(d => (d.Date, d.OperatingStateLabel, d.EffectiveHoursLineDisplay, d.PassdownCellText)));
    }
}
