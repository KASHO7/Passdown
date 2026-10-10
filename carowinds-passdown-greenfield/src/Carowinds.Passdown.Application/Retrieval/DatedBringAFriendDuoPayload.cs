namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Official exact-date pricing for "Celebrate America! Bring-A-Friend Duo Offer" (Phase 13C; see
/// docs/retrieval/special-ticket-sources.md for the live investigation this is built from —
/// accesso keyword <c>SUBAF50</c>, discovered via <c>getkeywordcalendar</c> the same way Phase 3D's
/// Fast Lane keyword was). Only ever attached to a <see cref="Domain.Retrieval.RetrievalStatus.Verified"/>
/// <see cref="SourceResult{TPayload}"/>.
/// </summary>
public sealed record DatedBringAFriendDuoPayload(IReadOnlyList<DatedBringAFriendDuoProduct> Products);

/// <summary>
/// One confirmed-date Bring-A-Friend Duo package. The live endpoint returns exactly one package per
/// date under this keyword, and its <c>retail_amount</c> is the checkout charge for a single
/// passholder-accompanied friend ticket — i.e. the <em>per-person</em> price within the catalog
/// page's own "2 friends for $50" bundle framing (Phase 13C Part H/I worked example), not a bundle
/// total. <see cref="ComparisonPrice"/>/<see cref="PriceQualifier"/> are always <c>null</c>: no live
/// response observed a second package or a labeling field for this keyword, matching the same
/// never-fabricate rule the other dated sources already follow.
/// </summary>
public sealed record DatedBringAFriendDuoProduct(
    string PackageId,
    string DisplayName,
    decimal PerPersonPrice,
    decimal? ComparisonPrice,
    string? PriceQualifier,
    DateOnly ApplicableDate);
