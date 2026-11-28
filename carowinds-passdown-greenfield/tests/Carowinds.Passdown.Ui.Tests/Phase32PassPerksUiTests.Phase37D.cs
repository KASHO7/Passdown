using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37D: the Calendar's selected-date Weather group leaves the three Pass Perks marker kinds, their counts, the
/// legend and the month-long summary exactly as they were — with or without weather, and across weather updates.
/// </summary>
public partial class Phase32PassPerksUiTests
{
    private static async Task<OperationsCalendarViewModel> Phase37DCalendarAsync(DateOnly today, FakeWeatherMonitor? monitor)
    {
        var settings = new CalendarDisplaySettingsViewModel(new MemoryCalendarStore(new CalendarDisplayPreferences(true)));
        await settings.PendingLoad;
        var calendar = new OperationsCalendarViewModel(
            new FakeOperationsCalendarService(),
            new FakeClock(new DateTimeOffset(today.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)),
            uiDispatcher: new ImmediateUiDispatcher(),
            passPerksService: new StubPassPerksService(Overview(today)),
            calendarDisplay: settings,
            weatherMonitor: monitor);
        await calendar.PendingLoad;
        return calendar;
    }

    private static string Markers(OperationsCalendarViewModel calendar) =>
        string.Join("|", calendar.Days.Select(d => $"{d.Date}:{string.Join(",", d.PassPerkMarkers.Select(m => $"{m.IsQualifyingVisit}{m.IsRedemption}{m.IsDeadline}{m.CountText}"))}"));

    [Fact]
    public async Task Phase37D_Weather_DoesNotChangePassPerksMarkers_LegendOrMonthSummary()
    {
        var today = new DateOnly(2026, 11, 21);
        var without = await Phase37DCalendarAsync(today, monitor: null);
        var monitor = new FakeWeatherMonitor { Current = WeatherUiFixtures.Normal() };
        var with = await Phase37DCalendarAsync(today, monitor);

        Assert.NotNull(with.Weather);
        Assert.Null(without.Weather);
        Assert.Equal(Markers(without), Markers(with));
        Assert.Equal(without.HasPassPerkLegend, with.HasPassPerkLegend);
        Assert.Equal(without.MonthLongPassPerks.Select(p => p.Title), with.MonthLongPassPerks.Select(p => p.Title));
        Assert.Equal(without.SelectedDatePassPerks.Select(p => p.Title), with.SelectedDatePassPerks.Select(p => p.Title));

        var days = with.Days;
        monitor.Current = WeatherUiFixtures.Stale();
        monitor.RaiseChanged();
        Assert.Same(days, with.Days); // a weather update never rebuilds the month cells
        Assert.Equal(Markers(without), Markers(with));
    }
}
