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

/// <summary>
/// Phase 31B finalization: confirmed vs inferred official dates. A date is <em>inferred</em> when the
/// official text does not state it, in full, as this item's own deadline — it was taken from a
/// page-level campaign banner, or written without a year. An inferred date is shown labeled as such
/// and is never, on its own, authoritative evidence that something has expired. (A method rather than
/// a record property so stored and synced JSON shapes are unchanged.)
/// </summary>
public static class OfficialDateStatementCertainty
{
    public static bool IsInferred(this OfficialDateStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return statement.YearInferred || statement.Source == DeadlineSource.CampaignBanner;
    }

    /// <summary>Why the date is inferred, for labels ("inferred from the page's campaign end date; year not stated"), or null when confirmed.</summary>
    public static string? InferenceNote(this OfficialDateStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return (statement.Source == DeadlineSource.CampaignBanner, statement.YearInferred) switch
        {
            (true, true) => "inferred from the page's campaign end date; year not stated",
            (true, false) => "inferred from the page's campaign end date",
            (false, true) => "year not stated; inferred",
            _ => null,
        };
    }
}

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
/// the time of the latest successful deals check. An offer is expired when its own confirmed purchase
/// deadline has passed or when a later successful check no longer lists it. An inferred end date (a
/// campaign banner's, or one written without a year) that has passed with no re-listing since makes the
/// offer Unknown ("not confirmed"), never Expired. Caching never keeps an expired offer current, and
/// staleness never makes an offer look newly confirmed.
/// </summary>
public static class PromotionStatusEvaluator
{
    /// <summary>An offer not re-verified within this window is shown as "previously verified" (Unknown), not Current.</summary>
    public static readonly TimeSpan VerificationStaleAfter = TimeSpan.FromHours(48);

    public static PromotionStatusAssessment Evaluate(OfficialPromotion promotion, DateOnly today, DateTimeOffset? latestSuccessfulCheck, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(promotion);

        // Only a confirmed date (stated in full in the offer's own terms) can expire an offer by itself.
        if (promotion.PurchaseDeadline is { } deadline && !deadline.IsInferred() && deadline.Date < today)
        {
            return new(PromotionStatus.Expired, $"Purchase deadline passed ({deadline.SourceText.Trim()}).");
        }

        if (latestSuccessfulCheck is { } latest && promotion.LastVerifiedAt < latest && !promotion.LastCheckUnreadable)
        {
            return new(PromotionStatus.Expired, "No longer listed on the official Carowinds pages.");
        }

        // An inferred end date (year not stated, or a page banner rather than the offer's own terms) that
        // has passed without the offer being re-listed since is "not confirmed", never confirmed-expired:
        // the next successful check either re-lists it or no longer lists it (authoritative either way).
        var lastVerifiedDate = DateOnly.FromDateTime(EasternDate(promotion.LastVerifiedAt));
        var inferredEnd = promotion.PurchaseDeadline ?? promotion.CampaignEnd;
        if (inferredEnd is not null &&
            inferredEnd.IsInferred() &&
            inferredEnd.Date < today &&
            lastVerifiedDate <= inferredEnd.Date)
        {
            return new(PromotionStatus.Unknown, string.Create(PresentationCulture.UnitedStates,
                $"Inferred end date passed (\"{inferredEnd.SourceText.Trim()}\", read as {inferredEnd.Date:MMMM d, yyyy}); not confirmed as ended by the official page."));
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
