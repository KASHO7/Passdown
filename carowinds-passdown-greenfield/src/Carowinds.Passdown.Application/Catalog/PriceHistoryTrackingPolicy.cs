using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Catalog;

/// <summary>
/// The product families whose advertised price history is tracked (Phase 20A). Phase 25 splits the
/// official Memberships &amp; Season Passes page's prices by what they are — each family has one price
/// unit, so a monthly Membership rate never shares a series or a list group with a one-time price.
/// Never persisted (always derived from the product's category, key and name).
/// </summary>
public enum PriceHistoryFamily
{
    /// <summary>A Season Pass's one-time new-purchase price (every pre-Phase 25 pass record too).</summary>
    SeasonPass,

    /// <summary>Phase 25: a Season Pass renewal offer (one-time).</summary>
    SeasonPassRenewal,

    /// <summary>Phase 25: a Membership's recurring monthly rate.</summary>
    Membership,

    /// <summary>Phase 25: a Membership's one-time initiation fee.</summary>
    MembershipInitiationFee,

    SeasonPassAddOn,
}

/// <summary>
/// Phase 20A: the one authoritative decision of whether an official product's price history is
/// tracked. Retrieval, reconciliation, composition and the Price History screen all ask this — none
/// re-implements the category checks.
///
/// Tracked: long-lived, non-dated season products only —
/// <list type="bullet">
/// <item>every <see cref="SourceCategory.SeasonPasses"/> product (the official Season Passes page);</item>
/// <item><see cref="SourceCategory.AddOns"/> products observed by the official <b>Season Pass Add-Ons</b>
/// page (<see cref="SeasonPassAddOnsSourceName"/>). The general add-ons page shares the
/// <c>AddOns</c> category but also lists single-day items, so its observations are not tracked;</item>
/// <item><see cref="SourceCategory.MilitaryFirstResponder"/> products whose name is a season-pass tier
/// pass (e.g. "Military Gold Pass") — the same established rule that already files them under Season
/// Passes (<see cref="ProductDisplaySectionResolver.IsSeasonPassName"/>).</item>
/// </list>
/// Never tracked: anything date-specific (daily admission, dated Fast Lane, dated haunted/SCarowinds,
/// dated Bring-A-Friend), Promotions cards (marketing cards; a pass promoted there is a duplicate of its
/// native Season Passes row), general add-ons, and custom products.
/// </summary>
public static class PriceHistoryTrackingPolicy
{
    /// <summary>The official season-pass add-ons source's evidence name (<c>SeasonPassAddOnsCatalogSource</c>).</summary>
    public const string SeasonPassAddOnsSourceName = "Carowinds Season Pass Add-Ons";

    /// <summary>The tracked family for one official observation, or <see langword="null"/> when it is not tracked.</summary>
    /// <param name="productKey">Phase 25: the product's key text, which names a pass offer's price type (<see cref="PassOffer.FromKey"/>).</param>
    public static PriceHistoryFamily? Classify(SourceCategory category, string sourceName, string displayName, bool dateSpecific, string? productKey = null)
    {
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(displayName);

        if (dateSpecific)
        {
            return null;
        }

        return category switch
        {
            SourceCategory.SeasonPasses => FamilyOf(PassOffer.Resolve(category, productKey, displayName)),
            SourceCategory.AddOns when string.Equals(sourceName, SeasonPassAddOnsSourceName, StringComparison.Ordinal) => PriceHistoryFamily.SeasonPassAddOn,
            SourceCategory.MilitaryFirstResponder when ProductDisplaySectionResolver.IsSeasonPassName(displayName) => PriceHistoryFamily.SeasonPass,
            _ => null,
        };
    }

    public static bool ShouldTrackPriceHistory(SourceCategory category, string sourceName, string displayName, bool dateSpecific) =>
        Classify(category, sourceName, displayName, dateSpecific) is not null;

    /// <summary>Phase 25: the family of a Season Passes product's offer; an unrecognized (legacy) Season Passes product stays a Season Pass.</summary>
    private static PriceHistoryFamily FamilyOf(PassOffer? offer) => offer?.PriceType switch
    {
        PassPriceType.Renewal => PriceHistoryFamily.SeasonPassRenewal,
        PassPriceType.MonthlyRate => PriceHistoryFamily.Membership,
        PassPriceType.InitiationFee => PriceHistoryFamily.MembershipInitiationFee,
        _ => PriceHistoryFamily.SeasonPass,
    };

    /// <summary>Phase 25: the unit every price of <paramref name="family"/> is quoted in.</summary>
    public static PassPriceUnit UnitOf(PriceHistoryFamily family) =>
        family == PriceHistoryFamily.Membership ? PassPriceUnit.PerMonth : PassPriceUnit.OneTime;

    /// <summary>
    /// The categories that can contain a tracked product — the only ones the Price History screen and
    /// diagnostics ever read.
    /// </summary>
    public static IReadOnlyList<SourceCategory> CandidateCategories { get; } =
        [SourceCategory.SeasonPasses, SourceCategory.AddOns, SourceCategory.MilitaryFirstResponder];

    /// <summary>
    /// Family for a stored catalog product. A product that already has recorded history keeps exposing
    /// it even if its latest observation came from a source that is not itself tracked (the same
    /// official add-on id can be listed on both add-on pages) — the history is still real.
    /// </summary>
    public static PriceHistoryFamily? ClassifyStoredProduct(
        SourceCategory category, string lastSourceName, string displayName, bool dateSpecific, bool hasRecordedHistory, string? productKey = null)
    {
        var family = Classify(category, lastSourceName, displayName, dateSpecific, productKey);
        if (family is not null || !hasRecordedHistory)
        {
            return family;
        }

        return category == SourceCategory.AddOns ? PriceHistoryFamily.SeasonPassAddOn : FamilyOf(PassOffer.Resolve(category, productKey, displayName));
    }
}
