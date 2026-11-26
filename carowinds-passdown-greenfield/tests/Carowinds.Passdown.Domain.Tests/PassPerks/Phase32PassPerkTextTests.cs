using Carowinds.Passdown.Domain.PassPerks;

namespace Carowinds.Passdown.Domain.Tests.PassPerks;

/// <summary>Phase 32: reading official Pass Perks wording — dates without years, visits, pass types, classification.</summary>
public class Phase32PassPerkTextTests
{
    private static readonly Uri Source = new("https://www.sixflags.com/carowinds/perks-and-play");
    private static readonly DateTimeOffset Checked = new(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly PassPerkCampaign November2026 = new(2026, 11, "November Perks", YearInferred: true);

    private static OfficialPassPerk Perk(string title, string? dateLine, params string[] paragraphs) =>
        PassPerkText.Interpret(title, dateLine, paragraphs, November2026, Source, Checked, isMonthlyListing: true);

    [Theory]
    [InlineData("November Perks", 2026, 11, 21, 2026, 11, true)]
    [InlineData("December Perks", 2026, 11, 28, 2026, 12, true)]  // next month published early
    [InlineData("January Perks", 2026, 12, 29, 2027, 1, true)]    // crosses the year boundary
    [InlineData("November Perks", 2027, 1, 4, 2026, 11, true)]    // a stale page is never relabelled as this year's November
    [InlineData("March 2027 Perks", 2026, 11, 21, 2027, 3, false)] // an explicit year is used as stated
    public void CampaignFromHeading_ResolvesTheYearNearestTheCheck(string heading, int y, int m, int d, int year, int month, bool inferred)
    {
        var campaign = PassPerkText.CampaignFromHeading(heading, new DateOnly(y, m, d));

        Assert.NotNull(campaign);
        Assert.Equal((year, month, inferred), (campaign.Year, campaign.Month, campaign.YearInferred));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Perks & Play")]
    [InlineData("Your Year-Round Benefits Await")]
    public void CampaignFromHeading_WithoutAMonth_IsNull(string? heading) =>
        Assert.Null(PassPerkText.CampaignFromHeading(heading, new DateOnly(2026, 11, 21)));

    [Fact]
    public void DateLine_SameMonthRange_IsAnInclusiveRedemptionWindow()
    {
        var window = PassPerkText.ReadDateLine("November 1 - November 30", November2026)!;

        Assert.Equal(PassPerkDateRole.Redemption, window.Role);
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), (window.Start, window.End));
        Assert.True(window.Contains(new DateOnly(2026, 11, 30)));
        Assert.False(window.Contains(new DateOnly(2026, 12, 1)));
        Assert.Equal(30, window.LengthInDays);
        Assert.True(window.YearInferred);
    }

    [Fact]
    public void DateLine_CrossingTheYear_EndsInTheNextYear()
    {
        var window = PassPerkText.ReadDateLine("November 27 - January 3", November2026)!;

        Assert.Equal((new DateOnly(2026, 11, 27), new DateOnly(2027, 1, 3)), (window.Start, window.End));
    }

    [Fact]
    public void DateLine_ShortForms_AreRead()
    {
        Assert.Equal((new DateOnly(2026, 12, 5), new DateOnly(2026, 12, 6)), Range(PassPerkText.ReadDateLine("Dec 5-6", November2026)!));
        Assert.Equal((new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 8)), Range(PassPerkText.ReadDateLine("November 7th – 8th", November2026)!));
        var explicitYear = PassPerkText.ReadDateLine("December 5, 2026 - January 3, 2027", November2026)!;
        Assert.False(explicitYear.YearInferred);
    }

    [Fact]
    public void DateLine_BookBy_IsABookingDeadline_ValidThrough_IsARedemptionEnd()
    {
        var book = PassPerkText.ReadDateLine("Book by November 30", November2026)!;
        Assert.Equal((PassPerkDateRole.BookingDeadline, (DateOnly?)null, new DateOnly(2026, 11, 30)), (book.Role, book.Start, book.End));

        var through = PassPerkText.ReadDateLine("Valid through December 31", November2026)!;
        Assert.Equal((PassPerkDateRole.Redemption, (DateOnly?)null, new DateOnly(2026, 12, 31)), (through.Role, through.Start, through.End));
    }

    [Theory]
    [InlineData("All month long")]
    [InlineData("Limited time")]
    [InlineData("November")]           // a month alone is never a date
    [InlineData("February 30 - March 2")] // impossible date
    public void DateLine_WithoutAnExplicitMonthAndDay_IsNeverInvented(string line) =>
        Assert.Null(PassPerkText.ReadDateLine(line, November2026));

    [Fact]
    public void DateLine_ExplicitYearsEndingBeforeTheyStart_IsRejected() =>
        Assert.Null(PassPerkText.ReadDateLine("December 5, 2026 - January 3, 2026", November2026));

    [Theory]
    [InlineData("Visit twice between November 1 and November 22 to receive a reward.", 2)]
    [InlineData("Visit 3 times this month.", 3)]
    [InlineData("Earn a reward after five qualifying visits.", 5)]
    [InlineData("Visit once on separate days.", 1)]
    [InlineData("Passholders can redeem one (1) churro.", null)]
    [InlineData("Plan your visit today.", null)]
    public void VisitCount_OnlyWhenANumberIsStated(string text, int? expected) =>
        Assert.Equal(expected, PassPerkText.ReadVisitCount(text));

    [Fact]
    public void VisitBasedPerk_HasSeparateQualifyingAndRedemptionWindows()
    {
        var perk = PassPerkText.Interpret(
            "Earn 2 FREE Bring-A-Friend Tickets",
            null,
            ["Visit twice between November 1 and November 22 to receive 2 FREE Bring-A-Friend Tickets, valid December 5 through January 3."],
            November2026,
            Source,
            Checked,
            isMonthlyListing: false);

        Assert.Equal(PassPerkRewardType.VisitBased, perk.RewardType);
        Assert.True(perk.RequiresQualifyingActivity);
        Assert.Equal(2, perk.RequiredVisits);
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 22)), Range(perk.QualifyingPeriod!));
        Assert.Equal((new DateOnly(2026, 12, 5), new DateOnly(2027, 1, 3)), Range(perk.Redemption!));
        Assert.True(perk.HasInferredYear);
    }

    [Fact]
    public void AutomaticallyDistributedPerk_HasNoVisitRequirement()
    {
        var perk = Perk("BOGO 50% Off Soft Pretzel", "November 1 - November 30",
            "Passholders and members can redeem one (1) BOGO 50% Off Soft Pretzel by scanning your season pass or membership at any participating location. Offer valid at your home park. Pass Perks Terms & Conditions apply.");

        Assert.Equal(PassPerkRewardType.ActivePassholder, perk.RewardType);
        Assert.False(perk.RequiresQualifyingActivity);
        Assert.Null(perk.RequiredVisits);
        Assert.Empty(perk.NamedPassTypes); // program eligibility applies
        Assert.Contains("Passholders and members", perk.EligibilityLines);
        Assert.Contains("at any participating location", perk.LocationLines);
        Assert.Contains("at your home park", perk.LocationLines);
    }

    [Fact]
    public void PassTypeRestriction_IsRecordedOnlyWhenNamed()
    {
        var perk = Perk("Exclusive Lounge Access", "December 5 - December 6",
            "Gold and Prestige Passholders can visit the lounge once per day.");

        Assert.Equal(["Gold", "Prestige"], perk.NamedPassTypes);
        Assert.Equal(PassPerkCategory.Experience, perk.Category);
        Assert.Contains("once per day", string.Join("|", perk.UsageLimitLines), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Pass Perks Giveaway", "Enter to win a VIP tour.", PassPerkRewardType.Giveaway)]
    [InlineData("Partner Offer", "A reward courtesy of our partner.", PassPerkRewardType.Partnership)]
    [InlineData("$10 Loyalty Reward", "Pass Perks Loyalty Rewards can be redeemed for in-park purchases.", PassPerkRewardType.LoyaltyCredit)]
    [InlineData("BOGO 50% Off Churro", "Passholders and members can redeem one (1).", PassPerkRewardType.ActivePassholder)]
    public void Classification_FollowsTheOfficialWording(string title, string body, PassPerkRewardType expected) =>
        Assert.Equal(expected, Perk(title, "November 1 - November 30", body).RewardType);

    [Theory]
    [InlineData("BOGO 50% Off Churro", PassPerkCategory.FoodAndBeverage)]
    [InlineData("Buy Two, Get One FREE Hot Chocolate Mug", PassPerkCategory.FoodAndBeverage)]
    [InlineData("Save 20% When You Spend $50+ On Retail", PassPerkCategory.Retail)]
    [InlineData("20% Off Game Play", PassPerkCategory.Games)]
    [InlineData("BOGO 50% Off Second Night at Carowinds Camp Wilderness", PassPerkCategory.Lodging)]
    [InlineData("Earn 2 FREE Bring-A-Friend Tickets", PassPerkCategory.Admission)]
    [InlineData("Mystery Reward", PassPerkCategory.Other)]
    public void Category_ComesFromTheTitle(string title, PassPerkCategory expected) =>
        Assert.Equal(expected, Perk(title, null).Category);

    [Fact]
    public void DisagreeingDatesInOnePerk_AreKeptAsADiscrepancy()
    {
        var perk = Perk("BOGO 50% Off Churro", "November 1 - November 30", "Valid November 1 through November 15 at your home park.");

        Assert.Equal(new DateOnly(2026, 11, 30), perk.Redemption!.End); // the perk's own date line is kept
        Assert.Single(perk.Discrepancies);
    }

    [Fact]
    public void PerkId_IsStableAndIncludesTheCampaignMonth()
    {
        Assert.Equal("perk:2026-11:bogo-50-off-churro", PassPerkText.PerkId(November2026, "BOGO 50% Off Churro"));
        Assert.Equal("perk:2026-12:bogo-50-off-churro", PassPerkText.PerkId(November2026 with { Month = 12 }, "BOGO 50% Off Churro"));
    }

    [Fact]
    public void ChangedTerms_AreDetected()
    {
        var a = Perk("BOGO 50% Off Churro", "November 1 - November 30", "Redeem one (1).");
        var b = Perk("BOGO 50% Off Churro", "November 1 - November 30", "Redeem two (2).");

        Assert.True(a.HasSameTermsAs(a with { LastVerifiedAt = Checked.AddDays(1) }));
        Assert.False(a.HasSameTermsAs(b));
    }

    private static (DateOnly?, DateOnly) Range(PassPerkDateWindow w) => (w.Start, w.End);
}
