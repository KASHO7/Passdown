using System.Globalization;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Domain.PassPerks;

/// <summary>
/// Phase 32: pure reading rules for official Pass Perks wording (no clock, no I/O). Turns the raw text of
/// one listed perk — its official title, its own date line and its description paragraphs — into an
/// <see cref="OfficialPassPerk"/>. Only what the wording states is recorded: a date needs an explicit month
/// and day in the perk's own text; a visit requirement needs an explicit number; a pass type must be named.
/// </summary>
public static partial class PassPerkText
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] MonthNames =
        ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];

    /// <summary>
    /// The campaign for a month heading such as "November Perks" (or "November 2026 Perks"), checked on
    /// <paramref name="checkedOn"/>. Without a stated year, the year that puts the month nearest the check
    /// date (five months back to six ahead) is used and flagged as inferred. Null when no month is named.
    /// </summary>
    public static PassPerkCampaign? CampaignFromHeading(string? heading, DateOnly checkedOn)
    {
        if (string.IsNullOrWhiteSpace(heading))
        {
            return null;
        }

        var match = HeadingRegex().Match(heading);
        if (!match.Success || MonthNumber(match.Groups["month"].Value) is not { } month)
        {
            return null;
        }

        if (match.Groups["year"].Success)
        {
            return new PassPerkCampaign(int.Parse(match.Groups["year"].Value, Us), month, heading.Trim(), YearInferred: false);
        }

        return new PassPerkCampaign(NearestYear(month, checkedOn.Year, checkedOn.Month), month, heading.Trim(), YearInferred: true);
    }

    /// <summary>
    /// Builds the perk from its official parts. <paramref name="isMonthlyListing"/> is true for an item under
    /// the "{Month} Perks" list (the official footnote says those are loaded to active passholders), false
    /// for a separately featured campaign block.
    /// </summary>
    public static OfficialPassPerk Interpret(
        string title,
        string? dateLine,
        IReadOnlyList<string> paragraphs,
        PassPerkCampaign campaign,
        Uri sourceUrl,
        DateTimeOffset checkedAt,
        bool isMonthlyListing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(sourceUrl);

        title = Clean(title)!;
        dateLine = Clean(dateLine);
        var body = paragraphs.Select(Clean).OfType<string>().ToList();
        var allText = string.Join(" ", body.Prepend(title));

        var dates = new List<PassPerkDateWindow>();
        var discrepancies = new List<string>();

        if (dateLine is not null && ReadDateLine(dateLine, campaign) is { } fromLine)
        {
            dates.Add(fromLine);
        }

        foreach (var sentence in body.SelectMany(Sentences))
        {
            foreach (var window in ReadSentenceDates(sentence, campaign))
            {
                var existing = dates.FirstOrDefault(d => d.Role == window.Role);
                if (existing is null)
                {
                    dates.Add(window);
                }
                else if (existing.Start != window.Start || existing.End != window.End)
                {
                    discrepancies.Add($"The perk's dates disagree: \"{existing.OfficialText}\" and \"{window.OfficialText}\". Both are shown; check the official page.");
                }
            }
        }

        var visits = ReadVisitCount(allText);
        var rewardType = Classify(allText, visits, isMonthlyListing);

        return new OfficialPassPerk
        {
            PerkId = PerkId(campaign, title),
            Title = title,
            Campaign = campaign,
            RewardType = rewardType,
            Category = Categorize(title, allText),
            OfficialParagraphs = body,
            DateText = dateLine,
            Dates = dates.OrderBy(d => d.Role).ToList(),
            RequiredVisits = visits,
            NamedPassTypes = PassTypeRegex().Matches(allText).Select(m => Normalize(m.Groups["type"].Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            EligibilityLines = Phrases(EligibilityRegex(), body),
            UsageLimitLines = Phrases(UsageLimitRegex(), body),
            LocationLines = Phrases(LocationRegex(), body),
            RestrictionLines = body.SelectMany(Sentences).Where(s => RestrictionRegex().IsMatch(s)).Distinct(StringComparer.Ordinal).ToList(),
            RewardText = RewardRegex().Match(title) is { Success: true } reward ? reward.Value.Trim() : null,
            PromoCode = PromoCodeRegex().Match(allText) is { Success: true } code ? code.Groups["code"].Value : null,
            Discrepancies = discrepancies,
            SourceUrl = sourceUrl,
            FirstSeenAt = checkedAt,
            LastVerifiedAt = checkedAt,
        };
    }

    /// <summary>Stable identity: campaign month plus a lowercase slug of the official title.</summary>
    public static string PerkId(PassPerkCampaign campaign, string title)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var slug = SlugRegex().Replace(title.ToLowerInvariant(), "-").Trim('-');
        return $"perk:{campaign.Key}:{slug}";
    }

    /// <summary>
    /// The perk's own date line: "November 1 - November 30" (redemption), "November 27 - January 3"
    /// (crosses the year), "Book by November 30" (booking deadline), "Valid through December 31" (end only).
    /// Null when the line has no explicit month and day.
    /// </summary>
    public static PassPerkDateWindow? ReadDateLine(string line, PassPerkCampaign campaign)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(campaign);

        if (DeadlineRegex().Match(line) is { Success: true } deadline &&
            TryDate(deadline.Groups["d"].Value, campaign, out var by, out var byInferred))
        {
            var role = deadline.Groups["verb"].Value.ToLowerInvariant() is "book" or "purchase" or "buy" or "reserve"
                ? PassPerkDateRole.BookingDeadline
                : PassPerkDateRole.Redemption;
            return new PassPerkDateWindow(role, null, by, line, byInferred);
        }

        if (RangeLineRegex().Match(line) is { Success: true } range && TryRange(range.Groups["a"].Value, range.Groups["b"].Value, campaign, out var start, out var end, out var inferred))
        {
            return new PassPerkDateWindow(PassPerkDateRole.Redemption, start, end, line, inferred);
        }

        return null;
    }

    private static IEnumerable<PassPerkDateWindow> ReadSentenceDates(string sentence, PassPerkCampaign campaign)
    {
        foreach (Match m in BetweenRegex().Matches(sentence))
        {
            if (TryRange(m.Groups["a"].Value, m.Groups["b"].Value, campaign, out var s, out var e, out var inferred))
            {
                var role = VisitWordRegex().IsMatch(sentence[..m.Index]) ? PassPerkDateRole.QualifyingVisits : PassPerkDateRole.Redemption;
                yield return new PassPerkDateWindow(role, s, e, m.Value.Trim(), inferred);
            }
        }

        foreach (Match m in ThroughRegex().Matches(sentence))
        {
            if (TryRange(m.Groups["a"].Value, m.Groups["b"].Value, campaign, out var s, out var e, out var inferred))
            {
                var isStay = m.Groups["lead"].Value.Equals("from", StringComparison.OrdinalIgnoreCase) && StayWordRegex().IsMatch(sentence[..m.Index]);
                yield return new PassPerkDateWindow(isStay ? PassPerkDateRole.StayWindow : PassPerkDateRole.Redemption, s, e, m.Value.Trim(), inferred);
            }
        }
    }

    /// <summary>"Visit twice", "visit 3 times", "after three visits" → the stated number; null when none is stated.</summary>
    public static int? ReadVisitCount(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var m = VisitCountRegex().Match(text);
        if (!m.Success)
        {
            return null;
        }

        var word = (m.Groups["n1"].Success ? m.Groups["n1"].Value : m.Groups["n2"].Value).ToLowerInvariant();
        return word switch
        {
            "once" or "one" => 1,
            "twice" or "two" => 2,
            "thrice" or "three" => 3,
            "four" => 4,
            "five" => 5,
            "six" => 6,
            "seven" => 7,
            "eight" => 8,
            "nine" => 9,
            "ten" => 10,
            _ => int.TryParse(word, NumberStyles.None, Us, out var n) && n > 0 ? n : null,
        };
    }

    private static PassPerkRewardType Classify(string text, int? visits, bool isMonthlyListing)
    {
        if (visits is not null)
        {
            return PassPerkRewardType.VisitBased;
        }

        if (GiveawayRegex().IsMatch(text))
        {
            return PassPerkRewardType.Giveaway;
        }

        if (PartnerRegex().IsMatch(text))
        {
            return PassPerkRewardType.Partnership;
        }

        if (LoyaltyRegex().IsMatch(text))
        {
            return PassPerkRewardType.LoyaltyCredit;
        }

        // The official footnote covers the monthly list: those rewards are loaded to active passholders.
        // A monthly item that is a booking code ("Promo code NOVPASS") rather than a pass-loaded reward is "Other".
        return isMonthlyListing && !PromoCodeRegex().IsMatch(text) ? PassPerkRewardType.ActivePassholder : PassPerkRewardType.Other;
    }

    private static PassPerkCategory Categorize(string title, string allText)
    {
        foreach (var text in new[] { title, allText })
        {
            if (AdmissionRegex().IsMatch(text)) return PassPerkCategory.Admission;
            if (LodgingRegex().IsMatch(text)) return PassPerkCategory.Lodging;
            if (GamesRegex().IsMatch(text)) return PassPerkCategory.Games;
            if (RetailRegex().IsMatch(text)) return PassPerkCategory.Retail;
            if (FoodRegex().IsMatch(text)) return PassPerkCategory.FoodAndBeverage;
            if (ExperienceRegex().IsMatch(text)) return PassPerkCategory.Experience;
        }

        return PassPerkCategory.Other;
    }

    private static bool TryRange(string a, string b, PassPerkCampaign campaign, out DateOnly start, out DateOnly end, out bool inferred)
    {
        start = end = default;
        inferred = false;

        // "November 1-30": the second part may be a bare day in the first part's month.
        if (BareDayRegex().IsMatch(b.Trim()) && DateRegex().Match(a) is { Success: true } first)
        {
            b = first.Groups["month"].Value + " " + b.Trim();
        }

        if (!TryDate(a, campaign, out start, out var aInferred) || !TryDate(b, campaign, out end, out var bInferred))
        {
            return false;
        }

        if (end < start)
        {
            if (!bInferred)
            {
                return false; // an explicit year that ends before it starts is not a usable range
            }

            end = end.AddYears(1); // "November 27 - January 3": the range crosses the year boundary
        }

        inferred = aInferred || bInferred;
        return true;
    }

    private static bool TryDate(string text, PassPerkCampaign campaign, out DateOnly date, out bool yearInferred)
    {
        date = default;
        yearInferred = false;
        var m = DateRegex().Match(text);
        if (!m.Success || MonthNumber(m.Groups["month"].Value) is not { } month)
        {
            return false;
        }

        var day = int.Parse(m.Groups["day"].Value, Us);
        int year;
        if (m.Groups["year"].Success)
        {
            year = int.Parse(m.Groups["year"].Value, Us);
        }
        else
        {
            year = NearestYear(month, campaign.Year, campaign.Month);
            yearInferred = true;
        }

        if (day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    /// <summary>The year placing <paramref name="month"/> from five months before to six months after the anchor month.</summary>
    internal static int NearestYear(int month, int anchorYear, int anchorMonth)
    {
        var anchor = anchorYear * 12 + (anchorMonth - 1);
        for (var year = anchorYear - 1; year <= anchorYear + 1; year++)
        {
            var diff = year * 12 + (month - 1) - anchor;
            if (diff is >= -5 and <= 6)
            {
                return year;
            }
        }

        return anchorYear;
    }

    private static int? MonthNumber(string name)
    {
        var key = name.Trim().TrimEnd('.').ToLowerInvariant();
        if (key == "sept")
        {
            return 9;
        }

        for (var i = 0; i < MonthNames.Length; i++)
        {
            if (MonthNames[i] == key || (key.Length == 3 && MonthNames[i].StartsWith(key, StringComparison.Ordinal)))
            {
                return i + 1;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> Phrases(Regex regex, IEnumerable<string> paragraphs) =>
        paragraphs.SelectMany(p => regex.Matches(p).Select(m => m.Value.Trim().TrimEnd('.', ','))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static IEnumerable<string> Sentences(string paragraph) =>
        SentenceSplitRegex().Split(paragraph).Select(s => s.Trim()).Where(s => s.Length > 0);

    private static string Normalize(string passType) =>
        Us.TextInfo.ToTitleCase(passType.Trim().ToLowerInvariant());

    private static string? Clean(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var cleaned = WhitespaceRegex().Replace(text.Replace(' ', ' '), " ").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private const string Month = @"(?<month>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?";
    private const string DatePattern = @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{1,2}(?:st|nd|rd|th)?(?:,?\s+\d{4})?";

    [GeneratedRegex(@"\b" + Month + @"\s+(?<day>\d{1,2})(?:st|nd|rd|th)?(?:,?\s+(?<year>\d{4}))?\b", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^\s*(?<a>" + DatePattern + @")\s*(?:-|–|—|to|through|thru)\s*(?<b>" + DatePattern + @"|\d{1,2}(?:st|nd|rd|th)?(?:,?\s+\d{4})?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RangeLineRegex();

    [GeneratedRegex(@"^\s*(?<verb>Book|Purchase|Buy|Reserve|Redeem|Use|Valid|Available|Ends?)\s+(?:by|through|thru|until)?\s*(?<d>" + DatePattern + @")\s*\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DeadlineRegex();

    [GeneratedRegex(@"\bbetween\s+(?<a>" + DatePattern + @")\s+and\s+(?<b>" + DatePattern + @"|\d{1,2}(?:st|nd|rd|th)?)", RegexOptions.IgnoreCase)]
    private static partial Regex BetweenRegex();

    [GeneratedRegex(@"\b(?<lead>valid|redeemable|available|from)\s+(?:from\s+)?(?<a>" + DatePattern + @")\s+(?:through|thru|to|until|-|–)\s+(?<b>" + DatePattern + @"|\d{1,2}(?:st|nd|rd|th)?)", RegexOptions.IgnoreCase)]
    private static partial Regex ThroughRegex();

    [GeneratedRegex(@"^\d{1,2}(?:st|nd|rd|th)?(?:,?\s+\d{4})?$")]
    private static partial Regex BareDayRegex();

    [GeneratedRegex(@"\bvisit", RegexOptions.IgnoreCase)]
    private static partial Regex VisitWordRegex();

    [GeneratedRegex(@"\b(?:stay|stays|night|nights|reservation|reservations)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StayWordRegex();

    [GeneratedRegex(@"\bvisit\s+(?<n1>once|twice|thrice|\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten)\b(?:\s+(?:separate\s+)?(?:times|days))?|\b(?<n2>\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten)\s+(?:qualifying\s+|separate\s+)?visits\b", RegexOptions.IgnoreCase)]
    private static partial Regex VisitCountRegex();

    [GeneratedRegex(@"^\s*(?<month>January|February|March|April|May|June|July|August|September|October|November|December)(?:\s+(?<year>\d{4}))?\s+(?:Pass\s+)?Perks\b", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\b(?:giveaway|sweepstakes|enter to win|chance to win)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GiveawayRegex();

    [GeneratedRegex(@"\b(?:partner|partners|partnership|courtesy of)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PartnerRegex();

    [GeneratedRegex(@"\bLoyalty Rewards?\b|\$\d+(?:\.\d{2})?\s+(?:in-park\s+)?(?:credit|reward balance)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LoyaltyRegex();

    [GeneratedRegex(@"\bpromo(?:tion)?\s+code\s+(?<code>[A-Z0-9]{3,})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PromoCodeRegex();

    [GeneratedRegex(@"\b(?<type>Prestige|Gold|Silver|Diamond|Platinum|Pre-K|All-Park Passport|Summer Fun|Haunt Season)\b(?=\s+(?:and\s+\w+\s+)?(?:Season\s+)?Pass)", RegexOptions.IgnoreCase)]
    private static partial Regex PassTypeRegex();

    [GeneratedRegex(@"\b(?:Season\s+)?Passholders?(?:\s+and\s+(?:members|Members))?\b|\bNew Reservations\b|\beligible\s+(?:passholders|members)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EligibilityRegex();

    [GeneratedRegex(@"\bone \(1\)|\blimit(?:ed)? (?:of |to )?(?:one|\d+)\b[^.]*|\b(?:one|once|\d+) per (?:pass|passholder|member|guest|visit|day|transaction)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UsageLimitRegex();

    [GeneratedRegex(@"\b(?:at|in)\s+(?:your\s+)?home park\b|\bat any participating location\b|\bat participating locations\b|\bat (?:Carowinds Camp Wilderness|Carowinds|the turnstile|a ticket window)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LocationRegex();

    [GeneratedRegex(@"Terms (?:&|and) Conditions apply|\bexclusions?\b|\bnot valid\b|\bcannot be combined\b|\bsubject to\b|\bwhile supplies last\b|\bblackout\b", RegexOptions.IgnoreCase)]
    private static partial Regex RestrictionRegex();

    [GeneratedRegex(@"^(?:BOGO\s+)?\d{1,3}%\s+Off|^Save\s+\d{1,3}%|^Buy\s+\w+,\s+Get\s+\w+\s+FREE|^(?:Earn\s+)?\d+\s+FREE|^FREE\b|^\$\d+(?:\.\d{2})?(?:\s+Off)?", RegexOptions.IgnoreCase)]
    private static partial Regex RewardRegex();

    [GeneratedRegex(@"Bring-A-Friend|\bticket|\badmission\b", RegexOptions.IgnoreCase)]
    private static partial Regex AdmissionRegex();

    [GeneratedRegex(@"Camp Wilderness|SpringHill|\bresort\b|\bhotel\b|\bnight\b|\bstay\b|\blodging\b", RegexOptions.IgnoreCase)]
    private static partial Regex LodgingRegex();

    [GeneratedRegex(@"\bgames?\b|\bgame play\b|\barcade\b", RegexOptions.IgnoreCase)]
    private static partial Regex GamesRegex();

    [GeneratedRegex(@"\bretail\b|\bmerchandise\b|\bmerch\b|\bgift shop\b", RegexOptions.IgnoreCase)]
    private static partial Regex RetailRegex();

    [GeneratedRegex(@"pretzel|churro|hot chocolate|\bfood\b|\bdining\b|\bdrinks?\b|\bbeverage|\bmeal\b|\bsnack|\bfunnel cake|\bpopcorn|\bcoffee|\bmug\b|\bicee\b", RegexOptions.IgnoreCase)]
    private static partial Regex FoodRegex();

    [GeneratedRegex(@"Fast Lane|\blounge\b|\bpreview\b|early entry|ride time|\bERT\b|\bmeet and greet\b|\bbackstage\b|\btour\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExperienceRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z])")]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
