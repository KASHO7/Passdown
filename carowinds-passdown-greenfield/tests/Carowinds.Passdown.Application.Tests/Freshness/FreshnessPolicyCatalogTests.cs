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
    public void Build_DailyCategories_UseDailyAtCadenceWithConfiguredTime(SourceCategory category)
    {
        var options = new FreshnessOptions { DailyRefreshTimeEastern = new TimeOnly(6, 0) };

        var policy = FreshnessPolicyCatalog.Build(options)[category];

        var cadence = Assert.IsType<FreshnessCadence.DailyAt>(policy.Cadence);
        Assert.Equal(new TimeOnly(6, 0), cadence.TimeOfDay);
    }

    [Fact]
    public void Build_SeasonPasses_UsesRollingIntervalFromOptions()
    {
        var options = new FreshnessOptions { SeasonPassRefreshIntervalDays = 7 };

        var policy = FreshnessPolicyCatalog.Build(options)[SourceCategory.SeasonPasses];

        var cadence = Assert.IsType<FreshnessCadence.RollingInterval>(policy.Cadence);
        Assert.Equal(TimeSpan.FromDays(7), cadence.Interval);
    }

    [Fact]
    public void Build_AddOns_UsesRollingIntervalFromOptions()
    {
        var options = new FreshnessOptions { AddOnRefreshIntervalDays = 14 };

        var policy = FreshnessPolicyCatalog.Build(options)[SourceCategory.AddOns];

        var cadence = Assert.IsType<FreshnessCadence.RollingInterval>(policy.Cadence);
        Assert.Equal(TimeSpan.FromDays(14), cadence.Interval);
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
