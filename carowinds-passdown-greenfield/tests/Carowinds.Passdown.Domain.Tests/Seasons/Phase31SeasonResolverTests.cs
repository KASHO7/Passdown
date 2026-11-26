using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Seasons;

namespace Carowinds.Passdown.Domain.Tests.Seasons;

/// <summary>
/// Phase 31: season resolution from official event dates. The 2026 dates below are the official
/// dates verified on 2026-11-21 and are test fixtures only — the resolver itself knows no dates.
/// </summary>
public class Phase31SeasonResolverTests
{
    private static readonly DateTimeOffset Seen = new(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Uri Url = new("https://www.sixflags.com/carowinds/events");

    internal static OfficialOperatingEvent Event(OperatingEventKind kind, string title, DateOnly start, DateOnly end, bool select, string? label = null) =>
        new($"{kind}:{start:yyyy-MM-dd}", title, kind, start, end, select, label, Url, Seen, Seen);

    internal static readonly OfficialOperatingEvent SCarowinds2026 =
        Event(OperatingEventKind.SCarowinds, "SCarowinds", new(2026, 9, 11), new(2026, 11, 7), select: true, "Select Nights. September 11 - November 7, 2026");

    internal static readonly OfficialOperatingEvent Oktoberfest2026 =
        Event(OperatingEventKind.Oktoberfest, "Oktoberfest Presented by Cash App", new(2026, 11, 7), new(2026, 11, 22), select: true, "Select Days, November 7-22, 2026");

    internal static readonly OfficialOperatingEvent WinterFest2026 =
        Event(OperatingEventKind.WinterFest, "WinterFest", new(2026, 11, 27), new(2027, 1, 3), select: true, "Select Dates, Nov 27 - Jan 3, 2027");

    internal static readonly OfficialOperatingEvent NewYearsEve2026 =
        Event(OperatingEventKind.NewYearsEve, "New Year’s Eve at Carowinds", new(2026, 12, 31), new(2026, 12, 31), select: false, "December 31, 2026");

    internal static readonly OfficialOperatingEvent EducationDays2027 =
        Event(OperatingEventKind.Other, "Education Days", new(2027, 4, 1), new(2027, 5, 21), select: true);

    internal static OperatingEventSchedule Schedule2026 => new([SCarowinds2026, Oktoberfest2026, WinterFest2026, NewYearsEve2026, EducationDays2027]);

    [Fact]
    public void Oktoberfest_OpenNovemberDate_IsScheduled_AndSCarowindsIsNotCurrent()
    {
        var resolution = SeasonResolver.Resolve(new(2026, 11, 21), Schedule2026, ParkDayOperation.Open);

        Assert.True(resolution.ScheduleKnown);
        var only = Assert.Single(resolution.Applicable);
        Assert.Equal(OperatingEventKind.Oktoberfest, only.Event.Kind);
        Assert.Equal(EventDayStatus.Scheduled, only.Status);
        Assert.True(resolution.Offers(OperatingEventKind.Oktoberfest));
        Assert.False(resolution.Offers(OperatingEventKind.SCarowinds));
        Assert.Contains(resolution.RecentlyEnded, e => e.Kind == OperatingEventKind.SCarowinds);
        Assert.Contains(resolution.Upcoming, e => e.Kind == OperatingEventKind.WinterFest);
    }

    [Fact]
    public void SelectDates_UnknownParkDay_IsNotAssumedToBeAnEventDay()
    {
        var resolution = SeasonResolver.Resolve(new(2026, 11, 18), Schedule2026, ParkDayOperation.Unknown);

        Assert.Equal(EventDayStatus.WithinSelectDateRange, Assert.Single(resolution.Applicable).Status);
    }

    [Fact]
    public void SelectDates_ParkClosed_IsNotOperatingThatDay_AndNotOffered()
    {
        var resolution = SeasonResolver.Resolve(new(2026, 11, 18), Schedule2026, ParkDayOperation.Closed);

        Assert.Equal(EventDayStatus.NotOperatingThatDay, Assert.Single(resolution.Applicable).Status);
        Assert.False(resolution.Offers(OperatingEventKind.Oktoberfest));
    }

    [Fact]
    public void SCarowindsLastNight_OverlapsOktoberfestFirstDay()
    {
        var resolution = SeasonResolver.Resolve(new(2026, 11, 7), Schedule2026, ParkDayOperation.Open);

        Assert.Equal([OperatingEventKind.SCarowinds, OperatingEventKind.Oktoberfest], resolution.Applicable.Select(a => a.Event.Kind));
    }

    [Fact]
    public void HistoricalSCarowindsDate_RetainsItsEvent()
    {
        var resolution = SeasonResolver.Resolve(new(2026, 10, 17), Schedule2026, ParkDayOperation.Open);

        Assert.True(resolution.Offers(OperatingEventKind.SCarowinds));
    }

    [Fact]
    public void DayBetweenOktoberfestAndWinterFest_HasNoNamedEvent_ButKnowsWhatIsNext()
    {
        var resolution = SeasonResolver.Resolve(new(2026, 11, 24), Schedule2026, ParkDayOperation.Unknown);

        Assert.True(resolution.ScheduleKnown);
        Assert.Empty(resolution.Applicable);
        Assert.Equal(OperatingEventKind.WinterFest, resolution.Upcoming.First().Kind);
    }

    [Fact]
    public void WinterFest_BeginsNov27_AndCrossesIntoJanuary2027()
    {
        Assert.True(SeasonResolver.Resolve(new(2026, 11, 27), Schedule2026, ParkDayOperation.Open).Offers(OperatingEventKind.WinterFest));
        Assert.False(SeasonResolver.Resolve(new(2026, 11, 26), Schedule2026, ParkDayOperation.Open).Offers(OperatingEventKind.WinterFest));
        Assert.True(SeasonResolver.Resolve(new(2027, 1, 3), Schedule2026, ParkDayOperation.Open).Offers(OperatingEventKind.WinterFest));
        Assert.False(SeasonResolver.Resolve(new(2027, 1, 4), Schedule2026, ParkDayOperation.Open).Offers(OperatingEventKind.WinterFest));
    }

    [Fact]
    public void NewYearsEve_OverlapsWinterFest()
    {
        var kinds = SeasonResolver.Resolve(new(2026, 12, 31), Schedule2026, ParkDayOperation.Open).Applicable.Select(a => a.Event.Kind).ToList();

        Assert.Contains(OperatingEventKind.WinterFest, kinds);
        Assert.Contains(OperatingEventKind.NewYearsEve, kinds);
    }

    [Fact]
    public void NonSeasonalListings_AreNeverTreatedAsASeason()
    {
        var resolution = SeasonResolver.Resolve(new(2027, 4, 15), Schedule2026, ParkDayOperation.Open);

        Assert.Empty(resolution.Applicable);
    }

    [Fact]
    public void MissingSchedule_IsUnknown_NotProofThatAnEventEnded()
    {
        var resolution = SeasonResolver.Resolve(new(2026, 11, 21), null, ParkDayOperation.Open);

        Assert.False(resolution.ScheduleKnown);
        Assert.False(SeasonResolver.HasEnded(null, OperatingEventKind.SCarowinds, new(2026, 11, 21), out _));
        Assert.False(SeasonResolver.HasEnded(OperatingEventSchedule.Empty, OperatingEventKind.SCarowinds, new(2026, 11, 21), out _));
    }

    [Fact]
    public void HasEnded_UsesTheLatestOccurrenceStartedOnOrBeforeToday()
    {
        Assert.True(SeasonResolver.HasEnded(Schedule2026, OperatingEventKind.SCarowinds, new(2026, 11, 8), out var ended));
        Assert.Equal(new DateOnly(2026, 11, 7), ended!.EndDate);
        Assert.False(SeasonResolver.HasEnded(Schedule2026, OperatingEventKind.SCarowinds, new(2026, 11, 7), out _)); // inclusive end
        Assert.False(SeasonResolver.HasEnded(Schedule2026, OperatingEventKind.WinterFest, new(2026, 11, 21), out _)); // not started yet

        var with2027 = Schedule2026.Merge([SCarowinds2026 with { EventId = "SCarowinds:2027-09-10", StartDate = new(2027, 9, 10), EndDate = new(2027, 11, 6) }]);
        Assert.False(SeasonResolver.HasEnded(with2027, OperatingEventKind.SCarowinds, new(2027, 9, 20), out _));
    }

    [Fact]
    public void Merge_KeepsOccurrencesNoLongerListed_AndUpdatesListedOnes()
    {
        var previous = new OperatingEventSchedule([SCarowinds2026, Oktoberfest2026]);
        var later = Seen.AddDays(1);

        var merged = previous.Merge([Oktoberfest2026 with { EndDate = new(2026, 11, 29), FirstSeenAt = later, LastListedAt = later }]);

        Assert.Contains(merged.Events, e => e.Kind == OperatingEventKind.SCarowinds);
        var oktoberfest = Assert.Single(merged.Events, e => e.Kind == OperatingEventKind.Oktoberfest);
        Assert.Equal(new DateOnly(2026, 11, 29), oktoberfest.EndDate);
        Assert.Equal(Seen, oktoberfest.FirstSeenAt);
        Assert.Equal(later, oktoberfest.LastListedAt);
    }

    [Theory]
    [InlineData("SCarowinds", OperatingEventKind.SCarowinds)]
    [InlineData("Oktoberfest Presented by Cash App", OperatingEventKind.Oktoberfest)]
    [InlineData("WinterFest", OperatingEventKind.WinterFest)]
    [InlineData("WinterFest Theatre Arts Day", OperatingEventKind.Other)]
    [InlineData("New Year’s Eve at Carowinds", OperatingEventKind.NewYearsEve)]
    [InlineData("Festival of Music", OperatingEventKind.Other)]
    [InlineData(null, OperatingEventKind.Other)]
    public void EventKind_FromOfficialTitleBrandOnly(string? title, OperatingEventKind expected) =>
        Assert.Equal(expected, OperatingEventKinds.FromOfficialTitle(title));

    [Fact]
    public void DateText_PrefersOfficialWording()
    {
        Assert.Equal("Select Days, November 7-22, 2026", Oktoberfest2026.DateText);
        Assert.Equal("Select dates, November 27, 2026 – January 3, 2027", (WinterFest2026 with { DateLabel = null }).DateText);
    }
}

public class Phase31SeasonalProductAvailabilityTests
{
    private static readonly OperatingEventSchedule Schedule = Phase31SeasonResolverTests.Schedule2026;
    private static readonly DateOnly Nov21 = new(2026, 11, 21);

    private static DateTimeOffset Eastern(int month, int day) => new(2026, month, day, 12, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void SCarowindsOnlyProduct_LastListedBeforeEventEnded_IsPastSeason()
    {
        Assert.True(SeasonalProductAvailability.IsPastSeason(ProductSeasonTags.SCarowinds, Eastern(10, 15), Schedule, Nov21, out var ended));
        Assert.Equal(OperatingEventKind.SCarowinds, ended!.Kind);
    }

    [Fact]
    public void SCarowindsOnlyProduct_StillListedAfterEventEnded_IsStillOnSale()
    {
        Assert.False(SeasonalProductAvailability.IsPastSeason(ProductSeasonTags.SCarowinds, Eastern(11, 20), Schedule, Nov21, out _));
    }

    [Fact]
    public void RegularSeasonProductNamingAnEvent_IsNeverDemoted()
    {
        Assert.False(SeasonalProductAvailability.IsPastSeason(ProductSeasonTags.Carowinds | ProductSeasonTags.SCarowinds, Eastern(10, 15), Schedule, Nov21, out _));
    }

    [Fact]
    public void UpcomingEventProduct_SoldBeforeItsEventStarts_StaysCurrent()
    {
        Assert.False(SeasonalProductAvailability.IsPastSeason(ProductSeasonTags.WinterFest, Eastern(11, 1), Schedule, Nov21, out _));
    }

    [Fact]
    public void UnknownSchedule_NeverDemotes()
    {
        Assert.False(SeasonalProductAvailability.IsPastSeason(ProductSeasonTags.SCarowinds, Eastern(10, 15), null, Nov21, out _));
    }

    [Fact]
    public void UntaggedOrMultiEventProducts_AreNeverDemoted()
    {
        Assert.False(SeasonalProductAvailability.IsPastSeason(ProductSeasonTags.None, Eastern(10, 15), Schedule, Nov21, out _));
        Assert.False(SeasonalProductAvailability.IsPastSeason(ProductSeasonTags.SCarowinds | ProductSeasonTags.WinterFest, Eastern(10, 15), Schedule, Nov21, out _));
    }
}
