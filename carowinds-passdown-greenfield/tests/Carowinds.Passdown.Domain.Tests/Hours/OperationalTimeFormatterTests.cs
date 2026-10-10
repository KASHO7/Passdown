using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Domain.Tests.Hours;

/// <summary>Phase 13E Part AG #10 — the canonical stored/display form every parsed time normalizes to.</summary>
public class OperationalTimeFormatterTests
{
    [Theory]
    [InlineData(9, 0, "9:00 AM")]
    [InlineData(22, 30, "10:30 PM")]
    [InlineData(0, 0, "12:00 AM")]
    [InlineData(12, 0, "12:00 PM")]
    [InlineData(23, 59, "11:59 PM")]
    public void Format_ProducesCanonicalTwelveHourDisplay(int hour, int minute, string expected)
    {
        Assert.Equal(expected, OperationalTimeFormatter.Format(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void Format_RoundTripsThroughParser()
    {
        Assert.True(OperationalTimeParser.TryParse("930am", out var parsed, out _));
        var formatted = OperationalTimeFormatter.Format(parsed!.Value);

        Assert.Equal("9:30 AM", formatted);
        Assert.True(OperationalTimeParser.TryParse(formatted, out var reparsed, out _));
        Assert.Equal(parsed, reparsed);
    }
}
