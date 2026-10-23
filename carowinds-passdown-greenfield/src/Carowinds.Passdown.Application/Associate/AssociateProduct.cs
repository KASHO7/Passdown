using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// One UI-neutral product row for the Associate Passdown view (spec §8.1's dynamic product list),
/// unifying official dated products, official catalog products, and manager-added custom products
/// (spec §7.5) behind one shape so a future view model does not need three parallel lists. Carries
/// no Avalonia/formatting concerns.
///
/// A product is only ever projected here when it is currently present in cached official/catalog
/// data (or is a draft-local custom entry) — nothing is hard-coded per category or season (spec
/// §7.1/§7.4): if the official source has no SCarowinds products for the selected date, no
/// haunted-attraction rows are projected at all, the same way an empty catalog category simply
/// contributes zero rows.
/// </summary>
/// <param name="Category">Null only for an <see cref="IsCustom"/> entry — a manual product has no official source category.</param>
public sealed record AssociateProduct(
    string ProductIdentity,
    string DisplayName,
    SourceCategory? Category,
    bool IsCustom,
    decimal? OfficialPriceAmount,
    string? OfficialDisplayPrice,
    decimal? ComparisonPriceAmount,
    string? PriceQualifier,
    bool DateSpecific,
    DateOnly? ApplicableDate,
    AssociateVerificationInfo Verification,
    bool IncludeOnPdf,
    decimal? DisplayPriceOverride,
    PriceKind PriceKind = PriceKind.Unknown,
    decimal? BundleTotalPriceAmount = null,
    int? BundleQuantity = null,
    decimal? PerPersonPriceAmount = null)
{
    /// <summary>The price to actually show: the draft's override when present, the official price otherwise (or a custom product's own manually entered price).</summary>
    public decimal? EffectiveDisplayPrice => DisplayPriceOverride ?? OfficialPriceAmount;

    /// <summary>
    /// Phase 19B: non-exclusive operating-season tags (<see cref="ProductSeasonClassifier"/>), computed
    /// from the official category and name — never persisted and never an availability signal.
    /// Custom products have no official evidence, so they are always <see cref="ProductSeasonTags.None"/>.
    /// </summary>
    public ProductSeasonTags SeasonTags => IsCustom ? ProductSeasonTags.None : ProductSeasonClassifier.Classify(Category, DisplayName);

    /// <summary>
    /// Phase 20A: the catalog product key whose season price history this row can show, or
    /// <see langword="null"/> when the product is not a tracked Season Pass / Season Pass Add-On
    /// (<see cref="PriceHistoryTrackingPolicy"/>). Composition-time only, never persisted.
    /// </summary>
    public string? PriceHistoryProductKey { get; init; }

    /// <summary>
    /// Phase 25: the official pass offer this row is (<see cref="Domain.Catalog.PassOffer.Resolve"/>) —
    /// a Season Pass new-purchase price, a renewal offer, a Membership monthly rate or an initiation
    /// fee — or <see langword="null"/> for every non-pass product. Composition-time only, never persisted.
    /// </summary>
    public PassOffer? PassOffer { get; init; }

    /// <summary>Phase 25: the Season Pass's published renewal offer, folded onto its new-purchase row (never a new-customer price).</summary>
    public decimal? RenewalPriceAmount { get; init; }

    /// <summary>Phase 25: the Membership's published initiation fee, folded onto its monthly-rate row (never added to the monthly rate).</summary>
    public decimal? InitiationFeeAmount { get; init; }

    /// <summary>Phase 25: the official page a catalog product was last verified from (its stored evidence), for source attribution.</summary>
    public Uri? OfficialSourceUrl { get; init; }

    /// <summary>Phase 25: the unit <see cref="OfficialPriceAmount"/> is quoted in — per month for a Membership rate.</summary>
    public PassPriceUnit PriceUnit => PassOffer?.Unit ?? PassPriceUnit.OneTime;

    /// <summary>
    /// Phase 25: whether this row belongs to the Associate PDF/Teams Copy contract — every non-pass
    /// product and the Season Pass new-purchase price; never a Membership, renewal offer or initiation fee.
    /// </summary>
    public bool IsPassdownExportable => PassOffer is null || PassOffer.IsPassdownExportable;

    /// <summary>Phase 25: a pass option the source lists but marks unavailable ("Not Available") — not a $0 price.</summary>
    public bool IsUnavailablePassOption => PassOffer is not null && OfficialPriceAmount is null && !IsCustom;
}
