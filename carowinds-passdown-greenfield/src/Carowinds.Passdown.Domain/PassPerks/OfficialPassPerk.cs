namespace Carowinds.Passdown.Domain.PassPerks;

/// <summary>
/// Phase 32: how an official Pass Perk is earned, using only classifications the official Perks &amp; Play
/// terms define. Never inferred from a reward's price or popularity.
/// </summary>
public enum PassPerkRewardType
{
    /// <summary>
    /// A monthly perk loaded to eligible passholders who hold an active pass (Carowinds page: rewards "are
    /// loaded to Season Passholders who hold an active pass on the first day of the month"). No visit needed.
    /// </summary>
    ActivePassholder,

    /// <summary>Earned by qualifying visits ("Visit twice between …") — the official "Visit-Based Terms".</summary>
    VisitBased,

    /// <summary>A promotional credit — the official "Pass Perks Loyalty Rewards Terms" (a dollar balance for in-park purchases).</summary>
    LoyaltyCredit,

    /// <summary>Managed by a partner organization — the official "Pass Perks Partnership Rewards" terms.</summary>
    Partnership,

    /// <summary>A sweepstakes/giveaway — the official "Pass Perks Giveaway" rules.</summary>
    Giveaway,

    /// <summary>Listed as a Pass Perk but its wording supports none of the classifications above.</summary>
    Other,
}

/// <summary>Phase 32: what the reward is for, read from its official title (classification only).</summary>
public enum PassPerkCategory
{
    FoodAndBeverage,
    Retail,
    Games,
    Lodging,
    Admission,
    Experience,
    Other,
}

/// <summary>Phase 32: which part of a perk an official date belongs to.</summary>
public enum PassPerkDateRole
{
    /// <summary>When the reward can be used ("November 1 - November 30", "valid December 5 through January 3").</summary>
    Redemption,

    /// <summary>When qualifying visits must happen ("between November 1 and November 22").</summary>
    QualifyingVisits,

    /// <summary>The last day to book/purchase ("Book by November 30").</summary>
    BookingDeadline,

    /// <summary>When a booked stay must take place ("stay … from October 31 to April 25").</summary>
    StayWindow,
}

/// <summary>
/// Phase 32: one official date or inclusive date range attached to a perk. <see cref="OfficialText"/> is
/// the wording exactly as published. The official Carowinds page omits years, so <see cref="YearInferred"/>
/// is set when the year was assigned from the month heading the perk was listed under and the day the
/// page was checked; the month and day are always the official ones. <see cref="IsPerkSpecific"/> is true
/// only when the date comes from the perk's own text — never from a month heading or page banner — and
/// only such dates may place a Calendar marker.
/// </summary>
public sealed record PassPerkDateWindow(
    PassPerkDateRole Role,
    DateOnly? Start,
    DateOnly End,
    string OfficialText,
    bool YearInferred,
    bool IsPerkSpecific = true)
{
    public bool Contains(DateOnly date) => date <= End && (Start is null || date >= Start.Value);

    public int? LengthInDays => Start is { } start ? End.DayNumber - start.DayNumber + 1 : null;
}

/// <summary>Phase 32: a prior official wording of a perk, kept when a later check found different terms.</summary>
public sealed record PassPerkTermsVersion(
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastVerifiedAt,
    string Title,
    IReadOnlyList<string> OfficialParagraphs,
    string? DateText);

/// <summary>Phase 32: the month campaign a perk was listed under ("November Perks" checked in November 2026).</summary>
public sealed record PassPerkCampaign(int Year, int Month, string OfficialHeading, bool YearInferred)
{
    public string Key => $"{Year:D4}-{Month:D2}";

    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);
}

/// <summary>
/// Phase 32: one officially advertised Carowinds Pass Perk — a public listing, never a statement that a
/// particular guest has earned or received it. Separate from standard Season Pass benefits, ticket
/// promotions and year-round passholder discounts. Every field is optional except identity, title,
/// campaign and source: nothing is filled in that the official wording does not state.
/// </summary>
public sealed record OfficialPassPerk
{
    /// <summary>Stable identity: <c>perk:{yyyy-MM}:{title slug}</c> (the campaign month keeps different months' same-named perks apart).</summary>
    public required string PerkId { get; init; }

    public required string Title { get; init; }

    public required PassPerkCampaign Campaign { get; init; }

    public PassPerkRewardType RewardType { get; init; }

    public PassPerkCategory Category { get; init; }

    /// <summary>The perk's official description paragraphs (date line and bare link labels excluded), in order.</summary>
    public IReadOnlyList<string> OfficialParagraphs { get; init; } = [];

    /// <summary>The perk's own date line as published ("November 27 - January 3", "Book by November 30"), or null.</summary>
    public string? DateText { get; init; }

    /// <summary>Every official date read from the perk's own text, by role.</summary>
    public IReadOnlyList<PassPerkDateWindow> Dates { get; init; } = [];

    /// <summary>Qualifying visits, only when the perk's own text states a number ("Visit twice" → 2).</summary>
    public int? RequiredVisits { get; init; }

    /// <summary>Pass types this perk names explicitly ("Gold and Prestige Passholders"); empty when it names none (program eligibility applies).</summary>
    public IReadOnlyList<string> NamedPassTypes { get; init; } = [];

    /// <summary>Official sentences about who can use it (e.g. "Passholders and members can redeem …", "New Reservations.").</summary>
    public IReadOnlyList<string> EligibilityLines { get; init; } = [];

    /// <summary>Official usage limits ("one (1)").</summary>
    public IReadOnlyList<string> UsageLimitLines { get; init; } = [];

    /// <summary>Where it can be used ("at your home park", "any participating location").</summary>
    public IReadOnlyList<string> LocationLines { get; init; } = [];

    /// <summary>Other official conditions ("Pass Perks Terms &amp; Conditions apply.").</summary>
    public IReadOnlyList<string> RestrictionLines { get; init; } = [];

    /// <summary>The reward or discount wording exactly as in the official title ("BOGO 50% Off", "2 FREE"), or null.</summary>
    public string? RewardText { get; init; }

    /// <summary>Official sources (or two dates in the same perk) that disagree; shown, never silently resolved.</summary>
    public IReadOnlyList<string> Discrepancies { get; init; } = [];

    /// <summary>An official promo code stated in the perk ("NOVPASS").</summary>
    public string? PromoCode { get; init; }

    public required Uri SourceUrl { get; init; }

    public DateTimeOffset FirstSeenAt { get; init; }

    public DateTimeOffset LastVerifiedAt { get; init; }

    /// <summary>Set when a complete later check no longer listed this perk (a verified removal), else null.</summary>
    public DateTimeOffset? NoLongerListedSince { get; init; }

    /// <summary>The latest check could not fully read the page, so this perk was kept with its last verified details.</summary>
    public bool LastCheckIncomplete { get; init; }

    /// <summary>Earlier official wordings of this perk, oldest first.</summary>
    public IReadOnlyList<PassPerkTermsVersion> PreviousVersions { get; init; } = [];

    public PassPerkDateWindow? DateOf(PassPerkDateRole role) => Dates.FirstOrDefault(d => d.Role == role);

    public PassPerkDateWindow? Redemption => DateOf(PassPerkDateRole.Redemption);

    public PassPerkDateWindow? QualifyingPeriod => DateOf(PassPerkDateRole.QualifyingVisits);

    public bool RequiresQualifyingActivity => RewardType == PassPerkRewardType.VisitBased || RequiredVisits is not null;

    public bool HasInferredYear => Dates.Any(d => d.YearInferred);

    /// <summary>The official content that, when changed, makes a new terms version (identity and provenance excluded).</summary>
    public bool HasSameTermsAs(OfficialPassPerk other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Title, other.Title, StringComparison.Ordinal)
            && string.Equals(DateText, other.DateText, StringComparison.Ordinal)
            && OfficialParagraphs.SequenceEqual(other.OfficialParagraphs, StringComparer.Ordinal);
    }
}
