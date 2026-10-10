using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Domain.Tests.Hours;

/// <summary>
/// Phase 13E Part AG's exact minimum test list, plus the parser's own documented edge cases
/// (bare-12 default, digit-run without a meridiem, decimal/slash rejection). One reusable parser
/// (spec Part G) — no per-field duplication of this grammar anywhere else in the codebase.
/// </summary>
public class OperationalTimeParserTests
{
    [Theory]
    [InlineData("9", 9, 0)] // AG #1 — bare hour, no meridiem => defaults to AM.
    [InlineData("9am", 9, 0)] // AG #2
    [InlineData("9 AM", 9, 0)] // AG #3 — case-insensitive, one space before the meridiem.
    [InlineData("9:00", 9, 0)] // AG #4
    [InlineData("9:00am", 9, 0)] // AG #5 — no space before the meridiem.
    [InlineData("9:30", 9, 30)] // AG #6
    [InlineData("930am", 9, 30)] // AG #7 — digit run + meridiem, no colon.
    [InlineData("10pm", 22, 0)] // AG #8
    [InlineData("10:30 PM", 22, 30)] // AG #9
    [InlineData("12am", 0, 0)] // midnight
    [InlineData("12pm", 12, 0)] // noon, explicit meridiem
    [InlineData("12", 12, 0)] // bare 12, no meridiem => documented default of noon
    [InlineData("12:15", 12, 15)]
    [InlineData("1030am", 10, 30)] // 4-digit run
    [InlineData("  9:00 am  ", 9, 0)] // surrounding whitespace trimmed
    public void TryParse_SupportedFormats_ProducesExpectedTime(string input, int expectedHour, int expectedMinute)
    {
        var success = OperationalTimeParser.TryParse(input, out var result, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(new TimeOnly(expectedHour, expectedMinute), result);
    }

    [Fact]
    public void TryParse_Null_IsAValidBlankValue()
    {
        var success = OperationalTimeParser.TryParse(null, out var result, out var error);

        Assert.True(success);
        Assert.Null(result);
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_WhitespaceOnly_IsAValidBlankValue()
    {
        // AG #11's "blank when required" is a caller-level (ViewModel) concern, not the parser's
        // (spec Part G: no UI/required-ness dependency here) — see AssociateViewModelTests for the
        // "required while enabled" behavior layered on top of this.
        var success = OperationalTimeParser.TryParse("   ", out var result, out var error);

        Assert.True(success);
        Assert.Null(result);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("abc")] // AG #12
    [InlineData("25:00")] // AG #13
    [InlineData("9:99")] // AG #14
    [InlineData("99pm")] // AG #15
    [InlineData("9xm")] // AG #16 — malformed AM/PM.
    [InlineData("9 pmm")]
    [InlineData("9.30")] // AG #17 — ambiguous/unsupported: decimal point, never locale-guessed.
    [InlineData("09/10")] // AG #17 — ambiguous/unsupported: slash.
    [InlineData("930")] // digit run with no meridiem is ambiguous — deliberately rejected, not guessed.
    [InlineData("13:00")] // hour above 12 with no meridiem is not a supported 24-hour input.
    [InlineData("0:30")] // hour 0 is never valid without an explicit "am"/"pm".
    public void TryParse_UnsupportedOrMalformedInput_FailsWithConciseMessage(string input)
    {
        var success = OperationalTimeParser.TryParse(input, out var result, out var error);

        Assert.False(success);
        Assert.Null(result);
        Assert.Equal(OperationalTimeParser.InvalidTimeMessage, error);
        Assert.DoesNotContain("Exception", error, StringComparison.OrdinalIgnoreCase);
    }
}
