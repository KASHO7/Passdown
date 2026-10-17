using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Tests.Catalog;

/// <summary>Phase 19B: deterministic, non-exclusive season tagging from official evidence only — the calendar date is never an input.</summary>
public class ProductSeasonClassifierTests
{
    [Theory]
    // A: regular park source
    [InlineData(SourceCategory.DatedAdmissionTickets, "Park Ticket", ProductSeasonTags.Carowinds)]
    [InlineData(SourceCategory.SeasonPasses, "2027 Gold Pass", ProductSeasonTags.Carowinds)]
    [InlineData(SourceCategory.AddOns, "All Season Dining Access", ProductSeasonTags.Carowinds)]
    [InlineData(SourceCategory.MilitaryFirstResponder, "Military 1-Day Ticket", ProductSeasonTags.Carowinds)]
    [InlineData(SourceCategory.DatedFastLane, "FAST LANE RESERVE", ProductSeasonTags.Carowinds)]
    // B: regular park source naming an event — both
    [InlineData(SourceCategory.DatedAdmissionTickets, "Ticket + SCarowinds", ProductSeasonTags.Carowinds | ProductSeasonTags.SCarowinds)]
    // C: SCarowinds-only source / event-named promotion
    [InlineData(SourceCategory.HauntedAttractions, "The Conjuring: Beyond Fear", ProductSeasonTags.SCarowinds)]
    [InlineData(SourceCategory.HauntedAttractions, "Haunted Attractions Pass", ProductSeasonTags.SCarowinds)]
    [InlineData(SourceCategory.Promotions, "SCarowinds Bundles", ProductSeasonTags.SCarowinds)]
    // D: WinterFest, and an SCarowinds + WinterFest combination
    [InlineData(SourceCategory.Promotions, "WinterFest Ticket", ProductSeasonTags.WinterFest)]
    [InlineData(SourceCategory.Promotions, "SCarowinds & WinterFest Combo", ProductSeasonTags.SCarowinds | ProductSeasonTags.WinterFest)]
    [InlineData(SourceCategory.AddOns, "winterfest photo package", ProductSeasonTags.Carowinds | ProductSeasonTags.WinterFest)]
    public void Classify_UsesSourceAndOfficialEventNames_NonExclusively(SourceCategory category, string name, ProductSeasonTags expected) =>
        Assert.Equal(expected, ProductSeasonClassifier.Classify(category, name));

    [Theory]
    [InlineData(SourceCategory.Promotions, "Bring-A-Friend Ticket")]
    [InlineData(SourceCategory.Promotions, "Celebrate America! Bring-A-Friend Duo")]
    [InlineData(SourceCategory.BringAFriendDuo, "Celebrate America! Bring-A-Friend Duo Offer")]
    [InlineData(SourceCategory.DatedBringAFriend, "Bring-A-Friend Ticket")]
    public void Classify_NoConfidentEvidence_LeavesTheProductUnclassified(SourceCategory category, string name) =>
        Assert.Equal(ProductSeasonTags.None, ProductSeasonClassifier.Classify(category, name));

    [Fact]
    public void Classify_CustomProductWithoutCategory_IsUnclassified() =>
        Assert.Equal(ProductSeasonTags.None, ProductSeasonClassifier.Classify(null, "Manager special"));

    [Fact]
    public void IsEventOnlyCategory_OnlyTheScarowindsHauntedSource()
    {
        Assert.True(ProductSeasonClassifier.IsEventOnlyCategory(SourceCategory.HauntedAttractions));
        Assert.All(
            Enum.GetValues<SourceCategory>().Where(c => c != SourceCategory.HauntedAttractions),
            c => Assert.False(ProductSeasonClassifier.IsEventOnlyCategory(c)));
    }

    [Fact]
    public void SeasonTags_AreFlags_SoEveryCombinationIsRepresentable()
    {
        var all = ProductSeasonTags.Carowinds | ProductSeasonTags.SCarowinds | ProductSeasonTags.WinterFest;
        Assert.True(all.HasFlag(ProductSeasonTags.Carowinds));
        Assert.True(all.HasFlag(ProductSeasonTags.SCarowinds));
        Assert.True(all.HasFlag(ProductSeasonTags.WinterFest));
        Assert.Equal(7, (int)all);
    }
}
