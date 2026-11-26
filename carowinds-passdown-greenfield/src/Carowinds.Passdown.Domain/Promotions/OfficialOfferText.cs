using System.Globalization;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Domain.Promotions;

/// <summary>
/// Phase 31: reads official offer wording into prices and dates without guessing. Every method
/// returns null when the text does not state the value explicitly — a missing date stays missing and
/// a non-numeric price ("3rd Night FREE", "Free") never becomes a number.
/// </summary>
public static partial class OfficialOfferText
{
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// The single explicit currency amount in a price text ("$69", "$1,299.00", "$40 /ea*", "69.00"),
    /// or null. Text with no "$" amount and no plain number (e.g. "3rd Night FREE", "Free") is null;
    /// text naming more than one dollar amount is ambiguous and also null.
    /// </summary>
    public static decimal? ParseCurrencyAmount(string? priceText)
    {
        if (string.IsNullOrWhiteSpace(priceText))
        {
            return null;
        }

        var text = priceText.Replace("$$", "$", StringComparison.Ordinal);
        var dollarMatches = DollarAmountRegex().Matches(text);
        if (dollarMatches.Count == 1)
        {
            return ParseNumber(dollarMatches[0].Groups["n"].Value);
        }

        if (dollarMatches.Count > 1)
        {
            return null;
        }

        var plain = PlainNumberRegex().Match(text.Trim());
        return plain.Success ? ParseNumber(plain.Groups["n"].Value) : null;
    }

    private static decimal? ParseNumber(string digits) =>
        decimal.TryParse(digits.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;

    /// <summary>
    /// A purchase deadline stated in the text: "Book by November 30, 2026", "Buy by 11/27",
    /// "Purchase by …", "Offer ends …", "Sale ends …", "Ends 11/27". Returns the first one found.
    /// </summary>
    public static OfficialDateStatement? FindPurchaseDeadline(string? text, DateOnly observedOn, DeadlineSource source)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = DeadlineRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return TryParseDate(match.Groups["date"].Value, observedOn, out var date, out var inferred)
            ? new OfficialDateStatement(date, Sentence(text, match.Index, match.Length), source, inferred)
            : null;
    }

    /// <summary>A sales start stated in the text: "On sale November 1, 2026", "Sale starts 12/1", "Available starting …".</summary>
    public static OfficialDateStatement? FindPurchaseStart(string? text, DateOnly observedOn)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = SaleStartRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return TryParseDate(match.Groups["date"].Value, observedOn, out var date, out var inferred)
            ? new OfficialDateStatement(date, Sentence(text, match.Index, match.Length), DeadlineSource.OfferTerms, inferred)
            : null;
    }

    /// <summary>
    /// A use/visit window stated as an explicit date range: "Valid on select nights November 2, 2026 –
    /// December 30, 2027", "Stays 11/2/26 – 12/30/27", "Valid 12/1/26 - 1/3/27".
    /// </summary>
    public static OfficialDateRange? FindRedemptionWindow(string? text, DateOnly observedOn)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in DateRangeRegex().Matches(text))
        {
            if (TryParseDate(match.Groups["start"].Value, observedOn, out var start, out _) &&
                TryParseDate(match.Groups["end"].Value, start, out var end, out _) &&
                end >= start)
            {
                return new OfficialDateRange(start, end, match.Value.Trim());
            }
        }

        return null;
    }

    /// <summary>A "use by" date for a bonus or ticket ("Use by 1/3/27") — redemption, never purchase.</summary>
    public static OfficialDateStatement? FindUseBy(string? text, DateOnly observedOn)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = UseByRegex().Match(text);
        return match.Success && TryParseDate(match.Groups["date"].Value, observedOn, out var date, out var inferred)
            ? new OfficialDateStatement(date, Sentence(text, match.Index, match.Length), DeadlineSource.OfferTerms, inferred)
            : null;
    }

    /// <summary>
    /// Phase 31B: the last day to use/redeem something, stated as "Use by 1/3/27", "Redeem by …", "valid
    /// on any public operating day through 01/03/2027", "to be used … through …". Redemption, never
    /// purchase — a "Book/Buy by" date is never returned here.
    /// </summary>
    public static OfficialDateStatement? FindRedemptionDeadline(string? text, DateOnly observedOn)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (FindUseBy(text, observedOn) is { } useBy)
        {
            return useBy;
        }

        var match = RedemptionThroughRegex().Match(text);
        return match.Success && TryParseDate(match.Groups["date"].Value, observedOn, out var date, out var inferred)
            ? new OfficialDateStatement(date, Sentence(text, match.Index, match.Length), DeadlineSource.OfferTerms, inferred)
            : null;
    }

    /// <summary>
    /// Parses "November 30, 2026", "Nov 30, 2026", "11/30/2026", "11/30/26", or a year-less "11/27" /
    /// "November 27". A year-less date resolves to its next occurrence on or after
    /// <paramref name="observedOn"/> (the day the official page said it) and is flagged inferred.
    /// </summary>
    public static bool TryParseDate(string? text, DateOnly observedOn, out DateOnly date, out bool yearInferred)
    {
        date = default;
        yearInferred = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().TrimEnd('.', ',', ';');

        string[] withYear = ["MMMM d, yyyy", "MMMM d yyyy", "MMM d, yyyy", "MMM d yyyy", "MMM. d, yyyy", "M/d/yyyy", "M/d/yy"];
        if (DateTime.TryParseExact(value, withYear, UsCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }

        string[] withoutYear = ["MMMM d", "MMM d", "MMM. d", "M/d"];
        foreach (var format in withoutYear)
        {
            if (DateTime.TryParseExact(value + " 2000", format + " yyyy", UsCulture, DateTimeStyles.AllowWhiteSpaces, out var noYear))
            {
                var candidate = SafeDate(observedOn.Year, noYear.Month, noYear.Day);
                if (candidate is null)
                {
                    return false;
                }

                if (candidate.Value < observedOn)
                {
                    candidate = SafeDate(observedOn.Year + 1, noYear.Month, noYear.Day);
                }

                if (candidate is null)
                {
                    return false;
                }

                date = candidate.Value;
                yearInferred = true;
                return true;
            }
        }

        return false;
    }

    private static DateOnly? SafeDate(int year, int month, int day) =>
        day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;

    /// <summary>The sentence around a match, so the stored source text is the official wording, not a fragment.</summary>
    private static string Sentence(string text, int index, int length)
    {
        var start = text.LastIndexOfAny(['.', '!', '\n'], Math.Max(0, index - 1));
        // A period inside a date like "Nov. 30" or "11/27." is not a sentence break worth honoring here.
        start = start < 0 ? 0 : start + 1;
        var end = text.IndexOfAny(['.', '!', '\n'], index + length);
        end = end < 0 ? text.Length : end + 1;
        return text[start..end].Trim();
    }

    private const string MonthName = @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?";
    private const string DatePattern = @"(?:" + MonthName + @"\s+\d{1,2}(?:,?\s+\d{4})?|\d{1,2}/\d{1,2}(?:/\d{2,4})?)";

    [GeneratedRegex(@"\$\s*(?<n>\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)")]
    private static partial Regex DollarAmountRegex();

    [GeneratedRegex(@"^(?<n>\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)$")]
    private static partial Regex PlainNumberRegex();

    [GeneratedRegex(@"\b(?:(?:Book|Buy|Purchase|Order|Reserve)\s+(?:by|before|no later than)|(?:Offer|Sale|Deal|Promotion)\s+ends|Ends|Expires|Available\s+(?:through|thru|until))\s+(?:on\s+)?(?<date>" + DatePattern + ")", RegexOptions.IgnoreCase)]
    private static partial Regex DeadlineRegex();

    [GeneratedRegex(@"\b(?:On\s+sale|Sale\s+starts|Available\s+starting|Goes\s+on\s+sale)\s+(?:on\s+)?(?<date>" + DatePattern + ")", RegexOptions.IgnoreCase)]
    private static partial Regex SaleStartRegex();

    [GeneratedRegex(@"(?<start>" + DatePattern + @")\s*(?:–|—|-|to|through|thru)\s*(?<end>" + DatePattern + ")", RegexOptions.IgnoreCase)]
    private static partial Regex DateRangeRegex();

    [GeneratedRegex(@"\b(?:Use|Redeem|Valid)\s+(?:by|through|thru|until)\s+(?<date>" + DatePattern + ")", RegexOptions.IgnoreCase)]
    private static partial Regex UseByRegex();

    [GeneratedRegex(@"\b(?:used|use|valid|redeemable|redeemed)\b[^.\n]{0,80}?\b(?:through|thru|until|by)\s+(?<date>" + DatePattern + ")", RegexOptions.IgnoreCase)]
    private static partial Regex RedemptionThroughRegex();
}
