namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Official exact-date admission-ticket products for one confirmed date (spec §8.1's "products"
/// concept as it applies to Phase 3C; see docs/retrieval/dated-admission-source.md for the live
/// investigation this is built from). Only ever attached to a
/// <see cref="Domain.Retrieval.RetrievalStatus.Verified"/> <see cref="SourceResult{TPayload}"/> —
/// the confirmed date itself already lives on <see cref="Domain.Retrieval.SourceEvidence.ConfirmedDate"/>.
/// </summary>
public sealed record DatedAdmissionPayload(IReadOnlyList<DatedAdmissionProduct> Products);

/// <summary>
/// One purchasable admission product for one confirmed date, keeping only what later
/// catalog/draft handling needs. <see cref="ComparisonPrice"/> is populated only when the source
/// itself returns a second, directly-observed price for the same product name on the same date —
/// see docs/retrieval/dated-admission-source.md. <see cref="PriceQualifier"/> is populated only
/// when the source explicitly labels why a price differs (e.g. a named rate/qualifier field on
/// the response itself) — as of this adapter, the live endpoint never carries such a field, so
/// this is always <c>null</c> in production. It is deliberately not inferred from a second,
/// out-of-band source (spec §5.2's "never fabricate or silently substitute"; see
/// docs/retrieval/dated-admission-source.md's "PriceQualifier is not populated" section for the
/// investigation-only catalog cross-reference that used to back a guessed label here).
/// <see cref="ApplicableDate"/> duplicates the evidence's confirmed date deliberately: unlike
/// <see cref="ParkHoursPayload"/> (one payload per date), a <see cref="DatedAdmissionPayload"/>
/// carries a list of products that later code may persist or merge independently of the evidence
/// they arrived with (spec's product/package-id field list for this phase explicitly asks for the
/// date on each product).
/// </summary>
public sealed record DatedAdmissionProduct(
    string PackageId,
    string DisplayName,
    decimal ExactDatePrice,
    decimal? ComparisonPrice,
    string? PriceQualifier,
    DateOnly ApplicableDate);
