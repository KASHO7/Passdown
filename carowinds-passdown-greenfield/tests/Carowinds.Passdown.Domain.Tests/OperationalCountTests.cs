using System.Globalization;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Domain.Tests;

/// <summary>
/// Phase 22D: <see cref="OperationalCount"/> — the shared en-US display rule for attendance and
/// pre-sold counts, and the lenient-but-safe parse of what an associate types.
/// </summary>
public sealed class OperationalCountTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(250, "250")]
    [InlineData(999, "999")]
    [InlineData(1000, "1,000")]
    [InlineData(4210, "4,210")]
    [InlineData(21240, "21,240")]
    [InlineData(38500, "38,500")]
    [InlineData(100000, "100,000")]
    [InlineData(250000, "250,000")]
    [InlineData(1000000, "1,000,000")]
    [InlineData(int.MaxValue, "2,147,483,647")]
    public void Format_GroupsThousandsWithCommas(int count, string expected) =>
        Assert.Equal(expected, OperationalCount.Format(count));

    [Fact]
    public void Format_Missing_UsesTheCallersMissingText_NeverZero()
    {
        Assert.Equal("—", OperationalCount.Format(null, "—"));
        Assert.Equal("Not entered", OperationalCount.Format(null, "Not entered"));
        Assert.Equal("0", OperationalCount.Format(0, "—"));
        Assert.Equal("38,500", OperationalCount.Format(38500, "—"));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("en-GB")]
    [InlineData("hi-IN")]
    [InlineData("ja-JP")]
    [InlineData("th-TH")]
    [InlineData("sv-SE")]
    public void Format_IgnoresTheHostCulture(string culture)
    {
        var (previous, previousUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("38,500", OperationalCount.Format(38500));
            Assert.Equal("21,240", OperationalCount.Format(21240));
            Assert.Equal("1,000,000", OperationalCount.Format(1000000));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData("38500", 38500)]
    [InlineData("38,500", 38500)]
    [InlineData("  21,240  ", 21240)]
    [InlineData("1,000,000", 1000000)]
    [InlineData("0", 0)]
    [InlineData("007", 7)]
    [InlineData("+12", 12)]
    [InlineData("-5", -5)] // range checking (≥ 0) stays with the caller, as before.
    [InlineData("38,5001", 385001)] // a digit typed at the end of a displayed "38,500".
    [InlineData("3,8500", 38500)] // a separator in an unusual place is ignored, never an error trap.
    public void TryParseEntry_AcceptsPlainAndGroupedWholeNumbers(string text, int expected)
    {
        Assert.True(OperationalCount.TryParseEntry(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData(",,,")]
    [InlineData("1.5")]
    [InlineData("38.500")]
    [InlineData("1 000")]
    [InlineData("abc")]
    [InlineData("12a")]
    [InlineData("$100")]
    [InlineData("99999999999")]
    public void TryParseEntry_RejectsAnythingElse(string text) =>
        Assert.False(OperationalCount.TryParseEntry(text, out _));

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    public void TryParseEntry_IgnoresTheHostCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.True(OperationalCount.TryParseEntry("38,500", out var grouped));
            Assert.Equal(38500, grouped);
            Assert.False(OperationalCount.TryParseEntry("38.500", out _)); // a German group separator is not a US one.
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
