using Carowinds.Passdown.Domain.Promotions;

namespace Carowinds.Passdown.Domain.Tests.Promotions;

public class Phase31OfficialOfferTextTests
{
    private static readonly DateOnly Nov21 = new(2026, 11, 21);

    [Theory]
    [InlineData("$69", 69.0)]
    [InlineData("$$69", 69.0)]
    [InlineData("$159", 159.0)]
    [InlineData("$40 /ea*", 40.0)]
    [InlineData("$1,299.00", 1299.0)]
    [InlineData("$89.99", 89.99)]
    [InlineData("69.00", 69.0)]
    public void ParseCurrencyAmount_ReadsOneExplicitAmount(string text, double expected) =>
        Assert.Equal((decimal)expected, OfficialOfferText.ParseCurrencyAmount(text));

    [Theory]
    [InlineData("3rd Night FREE")] // was $3 under the old digits-only parse
    [InlineData("Free")] // never a guessed $0
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Save $128 or $199")] // ambiguous
    [InlineData("Book 2 nights, get the")]
    public void ParseCurrencyAmount_ReturnsNullWhenNotExplicit(string? text) =>
        Assert.Null(OfficialOfferText.ParseCurrencyAmount(text));

    [Fact]
    public void PurchaseDeadline_BookBy_WithYear()
    {
        var text = "Terms and conditions apply. See Booking Portal for details. Book by November 30, 2026. Valid on select nights November 2, 2026 – December 30, 2027.";

        var deadline = OfficialOfferText.FindPurchaseDeadline(text, Nov21, DeadlineSource.OfferTerms);

        Assert.NotNull(deadline);
        Assert.Equal(new DateOnly(2026, 11, 30), deadline!.Date);
        Assert.False(deadline.YearInferred);
        Assert.Contains("Book by November 30, 2026", deadline.SourceText);
    }

    [Fact]
    public void PurchaseDeadline_YearlessEnds_ResolvesToNextOccurrence()
    {
        var deadline = OfficialOfferText.FindPurchaseDeadline("Hurry! Black Friday Deal Ends 11/27", Nov21, DeadlineSource.CampaignBanner);

        Assert.Equal(new DateOnly(2026, 11, 27), deadline!.Date);
        Assert.True(deadline.YearInferred);
        Assert.Equal(DeadlineSource.CampaignBanner, deadline.Source);
    }

    [Fact]
    public void PurchaseDeadline_YearlessDate_CrossesTheYearBoundary()
    {
        var deadline = OfficialOfferText.FindPurchaseDeadline("Sale ends 1/3", new DateOnly(2026, 12, 20), DeadlineSource.OfferTerms);

        Assert.Equal(new DateOnly(2027, 1, 3), deadline!.Date);
    }

    [Theory]
    [InlineData("Book a 3-night stay on select nights at Carowinds Camp Wilderness and get your third night FREE.")]
    [InlineData("Visits Thru Labor Day 2027")]
    [InlineData("Use by 1/3/27.")] // redemption, never purchase
    public void PurchaseDeadline_NotInventedFromOtherWording(string text) =>
        Assert.Null(OfficialOfferText.FindPurchaseDeadline(text, Nov21, DeadlineSource.OfferTerms));

    [Theory]
    [InlineData("Valid on select nights November 2, 2026 – December 30, 2027.", 2026, 11, 2, 2027, 12, 30)]
    [InlineData("Stays 11/2/26 – 12/30/27", 2026, 11, 2, 2027, 12, 30)]
    public void RedemptionWindow_ExplicitRanges(string text, int y1, int m1, int d1, int y2, int m2, int d2)
    {
        var window = OfficialOfferText.FindRedemptionWindow(text, Nov21);

        Assert.Equal(new DateOnly(y1, m1, d1), window!.Start);
        Assert.Equal(new DateOnly(y2, m2, d2), window.End);
    }

    [Fact]
    public void UseBy_IsReadSeparately()
    {
        Assert.Equal(new DateOnly(2027, 1, 3), OfficialOfferText.FindUseBy("Get a FREE Bring-A-Friend ticket. Use by 1/3/27.", Nov21)!.Date);
    }

    [Fact]
    public void PurchaseStart_OnSale()
    {
        Assert.Equal(new DateOnly(2026, 12, 1), OfficialOfferText.FindPurchaseStart("On sale December 1, 2026.", Nov21)!.Date);
    }
}

public class Phase31PromotionStatusTests
{
    private static readonly TimeSpan Eastern = TimeSpan.FromHours(-5);
    private static readonly DateTimeOffset CheckedNov21 = new(2026, 11, 21, 9, 0, 0, Eastern);
    private static readonly Uri Url = new("https://www.sixflags.com/carowinds/special-offers");

    private static OfficialPromotion Offer(Func<OfficialPromotion, OfficialPromotion>? configure = null)
    {
        var offer = new OfficialPromotion
        {
            PromotionId = "offer:test",
            Title = "2027 Silver Pass Bundle",
            PrimarySourceUrl = Url,
            SourceUrls = [Url],
            FirstSeenAt = CheckedNov21,
            LastVerifiedAt = CheckedNov21,
        };
        return configure is null ? offer : configure(offer);
    }

    private static PromotionStatus StatusOn(OfficialPromotion offer, DateOnly today, DateTimeOffset? latestCheck, DateTimeOffset now) =>
        PromotionStatusEvaluator.Evaluate(offer, today, latestCheck, now).Status;

    [Fact]
    public void ListedAtLatestCheck_IsCurrent() =>
        Assert.Equal(PromotionStatus.Current, StatusOn(Offer(), new(2026, 11, 21), CheckedNov21, CheckedNov21.AddHours(2)));

    [Fact]
    public void OwnPurchaseDeadlinePassed_IsExpired_EvenWhenCached()
    {
        var offer = Offer(o => o with { PurchaseDeadline = new OfficialDateStatement(new(2026, 11, 30), "Book by November 30, 2026.", DeadlineSource.OfferTerms) });

        Assert.Equal(PromotionStatus.Current, StatusOn(offer, new(2026, 11, 30), CheckedNov21, CheckedNov21.AddHours(1)));
        Assert.Equal(PromotionStatus.Expired, StatusOn(offer, new(2026, 12, 1), CheckedNov21, CheckedNov21.AddHours(1)));
    }

    [Fact]
    public void NotListedByALaterSuccessfulCheck_IsExpired()
    {
        var later = CheckedNov21.AddDays(1);

        Assert.Equal(PromotionStatus.Expired, StatusOn(Offer(), new(2026, 11, 22), later, later));
    }

    [Fact]
    public void UnreadableAtLaterCheck_IsUnknown_NotExpired()
    {
        var later = CheckedNov21.AddDays(1);

        Assert.Equal(PromotionStatus.Unknown, StatusOn(Offer(o => o with { LastCheckUnreadable = true }), new(2026, 11, 22), later, later));
    }

    [Fact]
    public void CampaignBannerEndPassed_WithoutRecheck_IsNotConfirmed_NeverExpired()
    {
        var offer = Offer(o => o with { CampaignEnd = new OfficialDateStatement(new(2026, 11, 27), "Hurry! Black Friday Deal Ends 11/27", DeadlineSource.CampaignBanner, YearInferred: true) });

        Assert.Equal(PromotionStatus.Current, StatusOn(offer, new(2026, 11, 22), CheckedNov21, CheckedNov21.AddHours(3)));
        var after = PromotionStatusEvaluator.Evaluate(offer, new(2026, 11, 28), CheckedNov21, CheckedNov21.AddDays(7));
        Assert.Equal(PromotionStatus.Unknown, after.Status);
        Assert.Contains("not confirmed as ended", after.Reason, StringComparison.Ordinal);
        Assert.Contains("November 27, 2026", after.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CampaignBannerEndPassed_AndALaterCheckNoLongerListsIt_IsExpired()
    {
        var offer = Offer(o => o with { CampaignEnd = new OfficialDateStatement(new(2026, 11, 27), "Hurry! Black Friday Deal Ends 11/27", DeadlineSource.CampaignBanner, YearInferred: true) });
        var nov28 = new DateTimeOffset(2026, 11, 28, 9, 0, 0, Eastern);

        Assert.Equal(PromotionStatus.Expired, StatusOn(offer, new(2026, 11, 28), nov28, nov28.AddHours(1)));
    }

    [Fact]
    public void YearInferredOwnDeadlinePassed_IsNotConfirmed_UntilTheOfficialPageSettlesIt()
    {
        var offer = Offer(o => o with { PurchaseDeadline = new OfficialDateStatement(new(2026, 11, 30), "Buy by 11/30.", DeadlineSource.OfferTerms, YearInferred: true) });

        Assert.Equal(PromotionStatus.Current, StatusOn(offer, new(2026, 11, 30), CheckedNov21, CheckedNov21.AddHours(1)));
        Assert.Equal(PromotionStatus.Unknown, StatusOn(offer, new(2026, 12, 1), CheckedNov21, CheckedNov21.AddHours(1)));
        var dec1 = new DateTimeOffset(2026, 12, 1, 9, 0, 0, Eastern);
        Assert.Equal(PromotionStatus.Expired, StatusOn(offer, new(2026, 12, 1), dec1, dec1.AddHours(1))); // no longer listed
    }

    [Fact]
    public void InferredVsConfirmed_Classification()
    {
        Assert.False(new OfficialDateStatement(new(2026, 11, 30), "Book by November 30, 2026.", DeadlineSource.OfferTerms).IsInferred());
        Assert.True(new OfficialDateStatement(new(2026, 11, 30), "Buy by 11/30.", DeadlineSource.OfferTerms, YearInferred: true).IsInferred());
        Assert.True(new OfficialDateStatement(new(2026, 11, 30), "Deal Ends November 30, 2026", DeadlineSource.CampaignBanner).IsInferred());
        Assert.Null(new OfficialDateStatement(new(2026, 11, 30), "Book by November 30, 2026.", DeadlineSource.OfferTerms).InferenceNote());
    }

    [Fact]
    public void CampaignBannerEndPassed_ButStillListedAfterwards_StaysCurrent()
    {
        var nov28 = new DateTimeOffset(2026, 11, 28, 9, 0, 0, Eastern);
        var offer = Offer(o => o with
        {
            CampaignEnd = new OfficialDateStatement(new(2026, 11, 27), "Hurry! Black Friday Deal Ends 11/27", DeadlineSource.CampaignBanner, YearInferred: true),
            LastVerifiedAt = nov28,
        });

        Assert.Equal(PromotionStatus.Current, StatusOn(offer, new(2026, 11, 28), nov28, nov28.AddHours(1)));
    }

    [Fact]
    public void OfferTermsWinOverABannerThatDisagrees()
    {
        var offer = Offer(o => o with
        {
            PurchaseDeadline = new OfficialDateStatement(new(2026, 11, 30), "Book by November 30, 2026.", DeadlineSource.OfferTerms),
            CampaignEnd = new OfficialDateStatement(new(2026, 11, 27), "Hurry! Black Friday Deal Ends 11/27", DeadlineSource.CampaignBanner, YearInferred: true),
        });

        Assert.Equal(PromotionStatus.Current, StatusOn(offer, new(2026, 11, 29), CheckedNov21, CheckedNov21.AddHours(1)));
    }

    [Fact]
    public void NotReverifiedRecently_IsUnknown_NeverNewlyConfirmed()
    {
        Assert.Equal(PromotionStatus.Unknown, StatusOn(Offer(), new(2026, 11, 25), CheckedNov21, CheckedNov21.AddDays(4)));
    }

    [Fact]
    public void FutureSaleStart_IsUpcoming()
    {
        var offer = Offer(o => o with { PurchaseStart = new OfficialDateStatement(new(2026, 12, 1), "On sale December 1, 2026.", DeadlineSource.OfferTerms) });

        Assert.Equal(PromotionStatus.Upcoming, StatusOn(offer, new(2026, 11, 21), CheckedNov21, CheckedNov21));
    }
}
