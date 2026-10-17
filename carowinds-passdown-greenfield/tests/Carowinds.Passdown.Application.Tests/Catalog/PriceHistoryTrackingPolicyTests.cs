using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Tests.Catalog;

public class PriceHistoryTrackingPolicyTests
{
    [Fact]
    public void SeasonPass_IsTracked() =>
        Assert.Equal(PriceHistoryFamily.SeasonPass,
            PriceHistoryTrackingPolicy.Classify(SourceCategory.SeasonPasses, "Carowinds Season Passes", "2027 Gold Pass", dateSpecific: false));

    [Fact]
    public void SeasonPassAddOn_FromTheSeasonPassAddOnsPage_IsTracked() =>
        Assert.Equal(PriceHistoryFamily.SeasonPassAddOn,
            PriceHistoryTrackingPolicy.Classify(SourceCategory.AddOns, PriceHistoryTrackingPolicy.SeasonPassAddOnsSourceName, "All Season Dining Access", false));

    [Fact]
    public void MilitarySeasonPass_IsTracked_ButMilitaryDailyTicketIsNot()
    {
        Assert.Equal(PriceHistoryFamily.SeasonPass,
            PriceHistoryTrackingPolicy.Classify(SourceCategory.MilitaryFirstResponder, "Carowinds Military & First Responder Tickets", "Military Gold Pass", false));
        Assert.Null(PriceHistoryTrackingPolicy.Classify(SourceCategory.MilitaryFirstResponder, "Carowinds Military & First Responder Tickets", "Military 1-Day Ticket", false));
    }

    [Theory]
    [InlineData(SourceCategory.DatedAdmissionTickets, "Park Ticket")]
    [InlineData(SourceCategory.DatedFastLane, "FAST LANE RESERVE")]
    [InlineData(SourceCategory.HauntedAttractions, "Haunted Attractions Pass")]
    [InlineData(SourceCategory.DatedBringAFriend, "Bring-A-Friend Ticket")]
    [InlineData(SourceCategory.BringAFriendDuo, "Celebrate America! Bring-A-Friend Duo")]
    [InlineData(SourceCategory.ParkHours, "Park Hours")]
    public void DatedProducts_AreNeverTracked(SourceCategory category, string name)
    {
        Assert.False(PriceHistoryTrackingPolicy.ShouldTrackPriceHistory(category, "Any Source", name, dateSpecific: true));
        Assert.False(PriceHistoryTrackingPolicy.ShouldTrackPriceHistory(category, "Any Source", name, dateSpecific: false));
    }

    [Fact]
    public void DateSpecificObservation_IsNeverTracked_EvenInATrackedCategory() =>
        Assert.False(PriceHistoryTrackingPolicy.ShouldTrackPriceHistory(SourceCategory.SeasonPasses, "Carowinds Season Passes", "2027 Gold Pass", dateSpecific: true));

    [Fact]
    public void DailyAddOn_FromTheGeneralAddOnsPage_IsNotTracked() =>
        Assert.False(PriceHistoryTrackingPolicy.ShouldTrackPriceHistory(SourceCategory.AddOns, "Carowinds General Add-Ons", "Single Day FunPix", false));

    [Fact]
    public void PromotionsCard_IsNotTracked() =>
        Assert.False(PriceHistoryTrackingPolicy.ShouldTrackPriceHistory(SourceCategory.Promotions, "Carowinds Promotions", "2027 Gold Pass", false));

    [Fact]
    public void StoredProductWithHistory_StaysTracked_WhenItsLatestObservationCameFromAnUntrackedSource() =>
        Assert.Equal(PriceHistoryFamily.SeasonPassAddOn,
            PriceHistoryTrackingPolicy.ClassifyStoredProduct(SourceCategory.AddOns, "Carowinds General Add-Ons", "All Season FunPix", false, hasRecordedHistory: true));

    [Fact]
    public void StoredUntrackedProductWithoutHistory_IsNotTracked() =>
        Assert.Null(PriceHistoryTrackingPolicy.ClassifyStoredProduct(SourceCategory.AddOns, "Carowinds General Add-Ons", "Single Day FunPix", false, hasRecordedHistory: false));
}
