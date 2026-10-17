using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Domain.Tests.Catalog;

public class PassValidityYearTests
{
    [Theory]
    [InlineData("2027 Gold Pass", 2027)]
    [InlineData("2026 Gold Pass", 2026)]
    [InlineData("2027 Military Gold Pass", 2027)]
    [InlineData("2026 All Season Haunted Attractions Pass", 2026)]
    [InlineData("Gold Pass - 2028", 2028)]
    [InlineData("2027 Gold Pass (2027 season)", 2027)]
    public void FromDisplayName_ExplicitYear_IsRead(string name, int expected) =>
        Assert.Equal(expected, PassValidityYear.FromDisplayName(name));

    [Theory]
    [InlineData("Gold Pass")]
    [InlineData("All Season Fast Lane")]
    [InlineData("2026/2027 Gold Pass")]
    [InlineData("Gold Pass 20270")]
    [InlineData("Pass 1999")]
    [InlineData("")]
    [InlineData(null)]
    public void FromDisplayName_NoOrAmbiguousYear_IsUnknown(string? name) =>
        Assert.Null(PassValidityYear.FromDisplayName(name));

    [Theory]
    [InlineData("2026 Gold Pass", "2027 Gold Pass", true)]
    [InlineData("2027 Gold Pass", "2027 Gold Season Pass", false)]
    [InlineData("2027 Gold Pass", "Gold Pass", false)]
    [InlineData("Gold Pass", "2027 Gold Pass", false)]
    public void StatesDifferentYear_OnlyWhenBothStateDifferentYears(string stored, string observed, bool expected) =>
        Assert.Equal(expected, PassValidityYear.StatesDifferentYear(stored, observed));

    [Fact]
    public void WithPassYear_KeepsCategoryAndBaseValue_AndRoundTrips()
    {
        var key = CatalogProductKey.FromOfficialId(SourceCategory.SeasonPasses, "550e3db6").WithPassYear(2027);

        Assert.Equal("SeasonPasses:id:550e3db6|pass-year:2027", key.ToString());
        Assert.Equal(key, CatalogProductKey.Parse(key.ToString()));
    }
}
