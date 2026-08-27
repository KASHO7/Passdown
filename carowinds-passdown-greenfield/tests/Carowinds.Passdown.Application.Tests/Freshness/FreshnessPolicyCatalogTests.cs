using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Freshness;

public class FreshnessPolicyCatalogTests
{
    [Fact]
    public void Build_CoversEveryProductAndDatedSourceCategory()
    {
        var catalog = FreshnessPolicyCatalog.Build(new FreshnessOptions());

        foreach (var category in Enum.GetValues<SourceCategory>())
        {
            Assert.True(catalog.ContainsKey(category), $"No policy configured for {category}.");
        }
    }

    [Theory]
    [InlineData(SourceCategory.ParkHours)]
    [InlineData(SourceCategory.DatedAdmissionTickets)]
    [InlineData(SourceCategory.DatedFastLane)]
    [InlineData(SourceCategory.HauntedAttractions)]
    [InlineData(SourceCategory.Promotions)]
    [InlineData(SourceCategory.SeasonPasses)]
    [InlineData(SourceCategory.AddOns)]
    [InlineData(SourceCategory.BringAFriendDuo)]
    [InlineData(SourceCategory.MilitaryFirstResponder)]
    public void Build_DailyCategories_UseDailyAtCadenceWithConfiguredTime(SourceCategory category)
    {
        var options = new FreshnessOptions { DailyRefreshTimeEastern = new TimeOnly(6, 0) };

        var policy = FreshnessPolicyCatalog.Build(options)[category];

        var cadence = Assert.IsType<FreshnessCadence.DailyAt>(policy.Cadence);
        Assert.Equal(new TimeOnly(6, 0), cadence.TimeOfDay);
    }

    [Fact]
    public void Build_RespectsCustomDailyRefreshTime()
    {
        var options = new FreshnessOptions { DailyRefreshTimeEastern = new TimeOnly(5, 30) };

        var policy = FreshnessPolicyCatalog.Build(options)[SourceCategory.ParkHours];

        var cadence = Assert.IsType<FreshnessCadence.DailyAt>(policy.Cadence);
        Assert.Equal(new TimeOnly(5, 30), cadence.TimeOfDay);
    }
}
