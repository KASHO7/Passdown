using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35E: the Calendar's own "Show Perks" box is the same shared Settings → Calendar preference (one value, one
/// store, saved on this computer only), and the three marker kinds stay distinct in the redesigned selected-date list.
/// A partial of <see cref="Phase32PassPerksUiTests"/> so it reuses that class's official November 2026 perk fixtures.
/// </summary>
public partial class Phase32PassPerksUiTests
{
    private static async Task<(OperationsCalendarViewModel Calendar, CalendarDisplaySettingsViewModel Settings, MemoryCalendarStore Store, FakeOperationsCalendarService Service, StubPassPerksService Perks)> Phase35ECalendarAsync(DateOnly today)
    {
        var store = new MemoryCalendarStore(new CalendarDisplayPreferences(true));
        var settings = new CalendarDisplaySettingsViewModel(store);
        await settings.PendingLoad;
        var service = new FakeOperationsCalendarService();
        var perks = new StubPassPerksService(Overview(today));
        var calendar = new OperationsCalendarViewModel(
            service,
            new FakeClock(new DateTimeOffset(today.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)),
            uiDispatcher: new ImmediateUiDispatcher(),
            passPerksService: perks,
            calendarDisplay: settings);
        await calendar.PendingLoad;
        return (calendar, settings, store, service, perks);
    }

    [Fact]
    public async Task Phase35E_ShowPerksBox_IsTheSharedSetting_HidesAndRestoresMarkers_WithoutAnyRead()
    {
        var (calendar, settings, store, service, perks) = await Phase35ECalendarAsync(new DateOnly(2026, 11, 22));
        var loads = service.LoadCalls.Count;
        var perkReads = perks.Calls;

        Assert.True(calendar.CanTogglePassPerks);
        Assert.Same(settings, calendar.CalendarDisplay);
        Assert.True(calendar.HasPassPerkLegend);
        Assert.True(calendar.HasPassPerksSection);

        calendar.CalendarDisplay!.ShowPassPerks = false; // what the Calendar's check box writes
        Assert.True(await settings.PendingSave);
        Assert.Equal(new CalendarDisplayPreferences(false), store.Saved);
        Assert.DoesNotContain(calendar.Days, d => d.HasPassPerks);
        Assert.False(calendar.HasPassPerkLegend);
        Assert.False(calendar.HasPassPerksSection);
        Assert.True(calendar.CanTogglePassPerks); // the box stays, so it can be turned back on

        calendar.CalendarDisplay.ShowPassPerks = true;
        Assert.Contains(calendar.Days, d => d.HasPassPerks);
        Assert.True(calendar.HasPassPerkLegend);

        Assert.Equal(loads, service.LoadCalls.Count); // re-formats cached data only
        Assert.Equal(perkReads, perks.Calls);
    }

    [Fact]
    public async Task Phase35E_ShowPerksBox_IsOfferedOnlyWithPerksAndThePreference()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 22, 15, 0, 0, TimeSpan.Zero));
        var noPerks = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock, calendarDisplay: new CalendarDisplaySettingsViewModel());
        var noPreference = new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock, uiDispatcher: new ImmediateUiDispatcher(),
            passPerksService: new StubPassPerksService(Overview(new DateOnly(2026, 11, 22))));
        await noPerks.PendingLoad;
        await noPreference.PendingLoad;

        Assert.False(noPerks.CanTogglePassPerks);
        Assert.False(noPreference.CanTogglePassPerks);
        Assert.Null(noPreference.CalendarDisplay);
    }

    [Theory]
    [InlineData(2026, 11, 22, "QualifyingVisit")]
    [InlineData(2026, 11, 27, "Redemption")]
    [InlineData(2026, 11, 30, "Deadline")]
    public async Task Phase35E_SelectedDateEntries_KeepTheirOwnMarkerKind(int year, int month, int day, string kind)
    {
        var (calendar, _, _, _, _) = await Phase35ECalendarAsync(new DateOnly(2026, 11, 21));
        calendar.SelectDateCommand.Execute(new DateOnly(year, month, day));
        await calendar.PendingLoad;

        var item = Assert.Single(calendar.SelectedDatePassPerks);
        Assert.Equal(kind, item.MarkerKind?.ToString());
        Assert.Equal(kind == "QualifyingVisit", item.IsQualifyingVisit);
        Assert.Equal(kind == "Redemption", item.IsRedemption);
        Assert.Equal(kind == "Deadline", item.IsDeadline);
        Assert.Equal(kind, Assert.Single(calendar.Days.Single(d => d.Date == new DateOnly(year, month, day)).PassPerkMarkers).Kind.ToString());

        // Month-long perks stay one collapsed summary, never daily markers; their rows carry no marker kind.
        Assert.True(calendar.HasMonthLongPassPerks);
        Assert.StartsWith("Valid all November (", calendar.MonthLongPassPerksHeaderText, StringComparison.Ordinal);
        Assert.All(calendar.MonthLongPassPerks, p => Assert.Null(p.MarkerKind));
    }
}
