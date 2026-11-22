using Carowinds.Passdown.Domain.PassPerks;

namespace Carowinds.Passdown.Domain.Tests.PassPerks;

/// <summary>Phase 32: perk status on an operating date, and the Calendar marker rules.</summary>
public class Phase32PassPerkScheduleTests
{
    private static readonly Uri Source = new("https://www.sixflags.com/carowinds/perks-and-play");
    private static readonly DateTimeOffset Checked = new(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly PassPerkCampaign November2026 = new(2026, 11, "November Perks", YearInferred: true);

    private static OfficialPassPerk Perk(string title, string? dateLine, params string[] paragraphs) =>
        PassPerkText.Interpret(title, dateLine, paragraphs, November2026, Source, Checked, isMonthlyListing: true);

    private static OfficialPassPerk Pretzel => Perk("BOGO 50% Off Soft Pretzel", "November 1 - November 30", "Passholders and members can redeem one (1).");

    private static OfficialPassPerk Mug => Perk("Buy Two, Get One FREE Hot Chocolate Mug", "November 27 - January 3", "Passholders and members can redeem one (1).");

    private static OfficialPassPerk Camp => Perk("BOGO 50% Off Second Night at Carowinds Camp Wilderness", "Book by November 30",
        "New Reservations. BOGO 50% Off Second Night when you stay at Carowinds Camp Wilderness from October 31 to April 25. Promo code NOVPASS.");

    private static OfficialPassPerk Bring => PassPerkText.Interpret("Earn 2 FREE Bring-A-Friend Tickets", null,
        ["Visit twice between November 1 and November 22 to receive 2 FREE Bring-A-Friend Tickets, valid December 5 through January 3."],
        November2026, Source, Checked, isMonthlyListing: false);

    private static OfficialPassPerk Weekend => Perk("Passholder Appreciation Giveaway", "December 5 - December 6", "Enter to win at Guest Relations.");

    private static PassPerkAssessment On(OfficialPassPerk perk, int year, int month, int day) =>
        PassPerkStatusEvaluator.Evaluate(perk, new DateOnly(year, month, day), Checked.AddHours(1));

    [Fact]
    public void RedemptionWindow_IsInclusiveOnBothEnds()
    {
        Assert.Equal(PassPerkStatus.Current, On(Pretzel, 2026, 11, 1).Status);
        Assert.Equal(PassPerkStatus.Current, On(Pretzel, 2026, 11, 30).Status);
        Assert.Equal(PassPerkStatus.Expired, On(Pretzel, 2026, 12, 1).Status);
    }

    [Fact]
    public void FutureRedemption_IsUpcoming()
    {
        var status = On(Mug, 2026, 11, 21);

        Assert.Equal(PassPerkStatus.Upcoming, status.Status);
        Assert.Contains("Nov 27, 2026", status.Reason, StringComparison.Ordinal);
        Assert.Equal(PassPerkStatus.Current, On(Mug, 2027, 1, 3).Status);
        Assert.Equal(PassPerkStatus.Expired, On(Mug, 2027, 1, 4).Status);
    }

    [Fact]
    public void VisitBasedPerk_IsCurrentWhileQualifying_ThenUpcomingUntilRedemption()
    {
        var qualifying = On(Bring, 2026, 11, 21);
        Assert.Equal(PassPerkStatus.Current, qualifying.Status);
        Assert.Contains("Qualifying visits through Nov 22, 2026", qualifying.Reason, StringComparison.Ordinal);

        var between = On(Bring, 2026, 11, 25);
        Assert.Equal(PassPerkStatus.Upcoming, between.Status);
        Assert.Contains("earned rewards are redeemable from Dec 5, 2026", between.Reason, StringComparison.Ordinal);

        Assert.Equal(PassPerkStatus.Current, On(Bring, 2026, 12, 5).Status);
        Assert.Equal(PassPerkStatus.Expired, On(Bring, 2027, 1, 4).Status);
    }

    [Fact]
    public void BookingDeadline_ExpiresTheBookingOffer()
    {
        Assert.Equal(PassPerkStatus.Current, On(Camp, 2026, 11, 30).Status);
        var after = On(Camp, 2026, 12, 1);
        Assert.Equal(PassPerkStatus.Expired, after.Status);
        Assert.Contains("Booking closed Nov 30, 2026", after.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NoLongerListed_WinsOverEveryDate()
    {
        var removed = Mug with { NoLongerListedSince = new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.FromHours(-5)) };

        var status = PassPerkStatusEvaluator.Evaluate(removed, new DateOnly(2026, 12, 2), new DateTimeOffset(2026, 12, 2, 8, 0, 0, TimeSpan.FromHours(-5)));

        Assert.Equal(PassPerkStatus.NoLongerListed, status.Status);
        Assert.Contains("last listed Nov 21, 2026", status.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleOrIncompleteVerification_IsNotReconfirmed()
    {
        var stale = PassPerkStatusEvaluator.Evaluate(Pretzel, new DateOnly(2026, 11, 25), Checked.AddDays(4));
        Assert.Equal(PassPerkStatus.NotReconfirmed, stale.Status);

        Assert.Equal(PassPerkStatus.NotReconfirmed, On(Pretzel with { LastCheckIncomplete = true }, 2026, 11, 21).Status);

        // An expired perk stays expired: staleness never hides a confirmed end.
        Assert.Equal(PassPerkStatus.Expired, PassPerkStatusEvaluator.Evaluate(Pretzel, new DateOnly(2026, 12, 5), Checked.AddDays(14)).Status);
    }

    [Fact]
    public void Calendar_MonthLongPerk_HasNoDailyMarkers_ButIsSummarized()
    {
        var entries = PassPerkCalendar.EntriesBetween([Pretzel], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        Assert.Empty(entries);
        Assert.Equal([Pretzel.PerkId], PassPerkCalendar.MonthLongPerks([Pretzel, Mug], 2026, 11).Select(p => p.PerkId));
    }

    [Fact]
    public void Calendar_ShortWindow_MarksEveryDay()
    {
        var entries = PassPerkCalendar.EntriesBetween([Weekend], new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31));

        Assert.Equal([new DateOnly(2026, 12, 5), new DateOnly(2026, 12, 6)], entries.Select(e => e.Date));
        Assert.All(entries, e => Assert.Equal(PassPerkCalendarEntryKind.RedeemableThisDay, e.Kind));
    }

    [Fact]
    public void Calendar_LongWindow_MarksOnlyItsFirstAndLastDay_AcrossTheYear()
    {
        var december = PassPerkCalendar.EntriesBetween([Mug], new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31));
        var january = PassPerkCalendar.EntriesBetween([Mug], new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31));

        Assert.Equal([(new DateOnly(2026, 11, 27), PassPerkCalendarEntryKind.RedemptionBegins)], december.Select(e => (e.Date, e.Kind)));
        Assert.Equal([(new DateOnly(2027, 1, 3), PassPerkCalendarEntryKind.RedemptionEnds)], january.Select(e => (e.Date, e.Kind)));
    }

    [Fact]
    public void Calendar_DeadlinesAndQualifyingEnd_AreMarked_StayWindowIsNot()
    {
        var camp = Camp;
        var bring = Bring;
        var entries = PassPerkCalendar.EntriesBetween([camp, bring], new DateOnly(2026, 10, 1), new DateOnly(2027, 4, 30));

        Assert.Contains(entries, e => e.Perk == camp && e.Kind == PassPerkCalendarEntryKind.BookingDeadline && e.Date == new DateOnly(2026, 11, 30));
        Assert.DoesNotContain(entries, e => e.Perk == camp && e.Date == new DateOnly(2026, 10, 31)); // the stay window is not a perk date
        Assert.Contains(entries, e => e.Perk == bring && e.Kind == PassPerkCalendarEntryKind.QualifyingEnds && e.Date == new DateOnly(2026, 11, 22));
        Assert.Contains(entries, e => e.Perk == bring && e.Kind == PassPerkCalendarEntryKind.RedemptionBegins && e.Date == new DateOnly(2026, 12, 5));
    }

    [Fact]
    public void Calendar_SeveralPerksOnOneDay_AreAllListed()
    {
        var entries = PassPerkCalendar.EntriesBetween([Camp, Pretzel, Mug, Bring], new DateOnly(2027, 1, 3), new DateOnly(2027, 1, 3));

        Assert.Equal(["Buy Two, Get One FREE Hot Chocolate Mug", "Earn 2 FREE Bring-A-Friend Tickets"], entries.Select(e => e.Perk.Title).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Calendar_InferredOrHeadingOnlyDates_NeverPlaceMarkers()
    {
        var headingOnly = Perk("Mystery Reward", null, "Look for it in the app.");
        var notSpecific = Mug with { Dates = [Mug.Redemption! with { IsPerkSpecific = false }] };

        Assert.Empty(PassPerkCalendar.EntriesBetween([headingOnly, notSpecific], new DateOnly(2026, 11, 1), new DateOnly(2027, 1, 31)));
        Assert.Contains(PassPerkCalendar.MonthLongPerks([headingOnly], 2026, 11), p => p == headingOnly); // still listed for the month
    }

    [Fact]
    public void Calendar_RemovedPerk_HasNoMarkersAfterItWasLastListed()
    {
        var removed = Mug with
        {
            LastVerifiedAt = new DateTimeOffset(2026, 11, 30, 8, 0, 0, TimeSpan.FromHours(-5)),
            NoLongerListedSince = new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.FromHours(-5)),
        };

        var entries = PassPerkCalendar.EntriesBetween([removed], new DateOnly(2026, 11, 1), new DateOnly(2027, 1, 31));

        Assert.Equal([new DateOnly(2026, 11, 27)], entries.Select(e => e.Date)); // last listed Nov 30; the Jan 3 end is not shown
    }

    [Theory]
    [InlineData(PassPerkCalendarEntryKind.QualifyingEnds, PassPerkMarkerKind.QualifyingVisit)]
    [InlineData(PassPerkCalendarEntryKind.RedeemableThisDay, PassPerkMarkerKind.Redemption)]
    [InlineData(PassPerkCalendarEntryKind.RedemptionBegins, PassPerkMarkerKind.Redemption)]
    [InlineData(PassPerkCalendarEntryKind.RedemptionEnds, PassPerkMarkerKind.Redemption)]
    [InlineData(PassPerkCalendarEntryKind.BookingDeadline, PassPerkMarkerKind.Deadline)]
    public void Phase32A_MarkerKind_FollowsTheDateMeaning(PassPerkCalendarEntryKind kind, PassPerkMarkerKind expected) =>
        Assert.Equal(expected, PassPerkCalendar.MarkerKindOf(kind));

    [Fact]
    public void Phase32A_EveryEntryKindHasAMarkerKind() =>
        Assert.All(Enum.GetValues<PassPerkCalendarEntryKind>(), k => Assert.True(Enum.IsDefined(PassPerkCalendar.MarkerKindOf(k))));

    [Fact]
    public void Phase32A_VisitBasedPerk_QualifyingEndIsAVisitMarker_RedemptionStartIsARedemptionMarker()
    {
        var bring = Bring;
        var entries = PassPerkCalendar.EntriesBetween([bring], new DateOnly(2026, 11, 1), new DateOnly(2027, 1, 31));

        Assert.Equal(
            [(new DateOnly(2026, 11, 22), PassPerkMarkerKind.QualifyingVisit), (new DateOnly(2026, 12, 5), PassPerkMarkerKind.Redemption), (new DateOnly(2027, 1, 3), PassPerkMarkerKind.Redemption)],
            entries.Select(e => (e.Date, e.MarkerKind)));
    }
}
