using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Domain.Tests.Catalog;

public class SeasonPriceTimelineTests
{
    private static SeasonPriceState Exact(decimal amount) => new(amount, PriceKind.StandardCatalog);

    private static SeasonPriceState From(decimal amount) => new(amount, PriceKind.StartingFrom);

    [Fact]
    public void FirstObservation_IsABaseline()
    {
        Assert.Equal(SeasonPriceObservationOutcome.Baseline, SeasonPriceTimeline.Classify(null, Exact(99m), resumedAfterRetirement: false));
    }

    [Fact]
    public void SameState_IsAConfirmation_ThatDoesNotStartANewInterval()
    {
        var outcome = SeasonPriceTimeline.Classify(Exact(105m), Exact(105.00m), resumedAfterRetirement: false);

        Assert.Equal(SeasonPriceObservationOutcome.Confirmed, outcome);
        Assert.False(SeasonPriceTimeline.StartsNewInterval(outcome));
    }

    [Theory]
    [InlineData(99, 105)]
    [InlineData(105, 95)]
    public void AmountChange_IsAPriceChange(int before, int after)
    {
        var outcome = SeasonPriceTimeline.Classify(Exact(before), Exact(after), resumedAfterRetirement: false);

        Assert.Equal(SeasonPriceObservationOutcome.PriceChanged, outcome);
        Assert.True(SeasonPriceTimeline.StartsNewInterval(outcome));
    }

    [Fact]
    public void ExactAndFromWithTheSameAmount_AreDifferentStates()
    {
        Assert.NotEqual(Exact(99m), From(99m));
        Assert.Equal(SeasonPriceObservationOutcome.SemanticsChanged, SeasonPriceTimeline.Classify(Exact(105m), From(105m), false));
        Assert.Equal(SeasonPriceObservationOutcome.SemanticsChanged, SeasonPriceTimeline.Classify(From(105m), Exact(105m), false));
    }

    [Fact]
    public void AmountAndKindChange_IsReportedAsBoth()
    {
        Assert.Equal(SeasonPriceObservationOutcome.PriceAndSemanticsChanged, SeasonPriceTimeline.Classify(Exact(99m), From(105m), false));
    }

    [Fact]
    public void SameStateAfterRetirement_StartsANewInterval()
    {
        var outcome = SeasonPriceTimeline.Classify(Exact(105m), Exact(105m), resumedAfterRetirement: true);

        Assert.Equal(SeasonPriceObservationOutcome.Reappeared, outcome);
        Assert.True(SeasonPriceTimeline.StartsNewInterval(outcome));
    }

    [Fact]
    public void Change_Increase_UsesThePreviousPriceAsTheDenominator()
    {
        var change = SeasonPriceChange.Between(Exact(99m), Exact(105m));

        Assert.Equal(6m, change.AmountDelta);
        Assert.Equal(6.06m, Math.Round(change.PercentChange!.Value, 2));
        Assert.True(change.AmountChanged);
        Assert.False(change.KindChanged);
    }

    [Fact]
    public void Change_Decrease_IsNegative()
    {
        var change = SeasonPriceChange.Between(Exact(105m), Exact(95m));

        Assert.Equal(-10m, change.AmountDelta);
        Assert.Equal(-9.52m, Math.Round(change.PercentChange!.Value, 2));
    }

    [Fact]
    public void Change_FromAZeroPrice_HasNoPercentage()
    {
        var change = SeasonPriceChange.Between(Exact(0m), Exact(10m));

        Assert.Equal(10m, change.AmountDelta);
        Assert.Null(change.PercentChange);
    }

    [Fact]
    public void Change_SemanticOnly_HasAZeroDeltaButIsStillAChange()
    {
        var change = SeasonPriceChange.Between(Exact(105m), From(105m));

        Assert.Equal(0m, change.AmountDelta);
        Assert.False(change.AmountChanged);
        Assert.True(change.KindChanged);
    }
}
