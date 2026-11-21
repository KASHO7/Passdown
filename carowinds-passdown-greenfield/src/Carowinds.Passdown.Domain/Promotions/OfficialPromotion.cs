namespace Carowinds.Passdown.Domain.Promotions;

/// <summary>Phase 31: broad kind of an official offer, from its official wording — used only to label and group.</summary>
public enum PromotionCategory
{
    Other = 0,
    SeasonPass,
    Bundle,
    DailyTicket,
    Lodging,
    Membership,
    Admission,
}

/// <summary>Phase 31: where a deadline came from — the offer's own terms, or a page-level campaign banner.</summary>
public enum DeadlineSource
{
    OfferTerms,
    CampaignBanner,
}

/// <summary>
/// Phase 31: an official calendar date stated in the offer's or page's own words, with the exact
/// text it was read from. A date written without a year ("Ends 11/27") is resolved to its next
/// occurrence on or after the day it was observed and flagged <see cref="YearInferred"/>.
/// </summary>
public sealed record OfficialDateStatement(DateOnly Date, string SourceText, DeadlineSource Source, bool YearInferred = false);

/// <summary>Phase 31: an official date range for when the offer can be used/visited (distinct from when it can be bought).</summary>
public sealed record OfficialDateRange(DateOnly Start, DateOnly End, string SourceText);

/// <summary>
/// Phase 31: one purchase choice inside an official offer card (e.g. "Silver Pass $69" beside "Good
/// Any Day Ticket $69"). <see cref="PriceAmount"/> is only ever a single, explicit currency amount
/// read from the option's own price text — never a fee, never a guessed $0.
/// </summary>
public sealed record PromotionPriceOption(
    string Title,
    string? Subtitle,
    string? PriceText,
    decimal? PriceAmount,
    string? PriceUnitText,
    string? SavingsText,
    string? Keyword);

/// <summary>
/// Phase 31: an official Carowinds promotion/deal, as last verified on the official pages.
/// <para>
/// Price fields follow strict rules: <see cref="AdvertisedPriceAmount"/> is the current selling price
/// only (never a struck-through price, never a processing/initiation fee or monthly payment);
/// <see cref="RegularPriceAmount"/> comes only from the official struck-through price;
/// <see cref="AdvertisedPriceText"/> keeps non-numeric official wording ("3rd Night FREE", "Free").
/// </para>
/// <para>
/// <see cref="PurchaseDeadline"/> (when it can be bought) and <see cref="RedemptionWindow"/> (when it
/// can be used) are kept separate, each with the exact official text it was read from; a page-level
/// banner date that disagrees with the offer's own terms is kept in <see cref="CampaignEnd"/> and
/// reported in <see cref="Discrepancies"/>, never silently merged.
/// </para>
/// </summary>
public sealed record OfficialPromotion
{
    public required string PromotionId { get; init; }
    public required string Title { get; init; }
    public PromotionCategory Category { get; init; }
    public string? Highlight { get; init; }
    public string? Subtitle { get; init; }

    public string? AdvertisedPriceText { get; init; }
    public decimal? AdvertisedPriceAmount { get; init; }
    public string? PriceQualifier { get; init; }
    public string? PriceUnitText { get; init; }
    public decimal? RegularPriceAmount { get; init; }
    public string? SavingsText { get; init; }
    public IReadOnlyList<PromotionPriceOption> PriceOptions { get; init; } = [];

    public OfficialDateStatement? PurchaseDeadline { get; init; }
    public OfficialDateStatement? PurchaseStart { get; init; }
    public OfficialDateStatement? CampaignEnd { get; init; }
    public OfficialDateRange? RedemptionWindow { get; init; }
    public IReadOnlyList<string> ValidityNotes { get; init; } = [];

    public IReadOnlyList<string> Terms { get; init; } = [];
    public IReadOnlyList<string> FeeDisclosures { get; init; } = [];
    public IReadOnlyList<string> Discrepancies { get; init; } = [];

    public required Uri PrimarySourceUrl { get; init; }
    public IReadOnlyList<Uri> SourceUrls { get; init; } = [];
    public string? Keyword { get; init; }

    public DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>The last successful official check that listed this offer.</summary>
    public DateTimeOffset LastVerifiedAt { get; init; }

    /// <summary>True when a successful check could not read this offer's card (kept, but not re-confirmed).</summary>
    public bool LastCheckUnreadable { get; init; }
}

/// <summary>Phase 31: the presented status of an official offer.</summary>
public enum PromotionStatus
{
    Current,
    Upcoming,
    Expired,

    /// <summary>Previously verified but not confirmed recently (or the last check could not read it) — never shown as newly confirmed.</summary>
    Unknown,
}

public sealed record PromotionStatusAssessment(PromotionStatus Status, string Reason);

/// <summary>
/// Phase 31: pure status rules for an official offer. Inputs are the offer, the Eastern "today", and
/// the time of the latest successful deals check. An offer is expired when its own purchase deadline
/// has passed, when a later successful check no longer lists it, or when a campaign banner's end date
/// has passed and no successful check has re-listed it since. Caching never keeps an expired offer
/// current, and staleness never makes an offer look newly confirmed.
/// </summary>
public static class PromotionStatusEvaluator
{
    /// <summary>An offer not re-verified within this window is shown as "previously verified" (Unknown), not Current.</summary>
    public static readonly TimeSpan VerificationStaleAfter = TimeSpan.FromHours(48);

    public static PromotionStatusAssessment Evaluate(OfficialPromotion promotion, DateOnly today, DateTimeOffset? latestSuccessfulCheck, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(promotion);

        if (promotion.PurchaseDeadline is { } deadline && deadline.Date < today)
        {
            return new(PromotionStatus.Expired, $"Purchase deadline passed ({deadline.SourceText.Trim()}).");
        }

        if (latestSuccessfulCheck is { } latest && promotion.LastVerifiedAt < latest && !promotion.LastCheckUnreadable)
        {
            return new(PromotionStatus.Expired, "No longer listed on the official Carowinds pages.");
        }

        var lastVerifiedDate = DateOnly.FromDateTime(EasternDate(promotion.LastVerifiedAt));
        if (promotion.PurchaseDeadline is null &&
            promotion.CampaignEnd is { } campaignEnd &&
            campaignEnd.Date < today &&
            lastVerifiedDate <= campaignEnd.Date)
        {
            return new(PromotionStatus.Expired, $"Advertised end date passed ({campaignEnd.SourceText.Trim()}).");
        }

        if (promotion.PurchaseStart is { } start && start.Date > today)
        {
            return new(PromotionStatus.Upcoming, string.Create(PresentationCulture.UnitedStates, $"On sale starting {start.Date:MMMM d, yyyy}."));
        }

        if (promotion.LastCheckUnreadable)
        {
            return new(PromotionStatus.Unknown, "The latest official check could not read this offer; showing what was verified before.");
        }

        if (now - promotion.LastVerifiedAt > VerificationStaleAfter)
        {
            return new(PromotionStatus.Unknown, "Not re-verified recently; showing what was verified before.");
        }

        return new(PromotionStatus.Current, "Listed on the official Carowinds pages at the last check.");
    }

    // Verification times are recorded as America/New_York instants (SourceEvidence.TimeChecked), so the
    // instant's own local clock is already the Eastern calendar day — no host time zone lookup here.
    private static DateTime EasternDate(DateTimeOffset instant) => instant.DateTime;
}
