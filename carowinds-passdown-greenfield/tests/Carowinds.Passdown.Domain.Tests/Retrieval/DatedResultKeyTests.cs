using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Domain.Tests.Retrieval;

public class DatedResultKeyTests
{
    [Fact]
    public void Equality_SameCategoryAndDateAreEqual()
    {
        var a = new DatedResultKey(SourceCategory.ParkHours, new DateOnly(2026, 10, 31));
        var b = new DatedResultKey(SourceCategory.ParkHours, new DateOnly(2026, 10, 31));

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_DifferentDateIsNeverEqual_EvenOneDayApart()
    {
        var halloween = new DatedResultKey(SourceCategory.ParkHours, new DateOnly(2026, 10, 31));
        var novemberFirst = new DatedResultKey(SourceCategory.ParkHours, new DateOnly(2026, 11, 1));

        Assert.NotEqual(halloween, novemberFirst);
    }

    [Fact]
    public void Equality_SameDateDifferentCategoryIsNeverEqual()
    {
        var hours = new DatedResultKey(SourceCategory.ParkHours, new DateOnly(2026, 10, 31));
        var fastLane = new DatedResultKey(SourceCategory.DatedFastLane, new DateOnly(2026, 10, 31));

        Assert.NotEqual(hours, fastLane);
    }
}
