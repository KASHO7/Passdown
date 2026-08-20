namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Official exact-date Fast Lane products for one confirmed date (Phase 3D; see
/// docs/retrieval/dated-fast-lane-source.md for the live investigation this is built from). Only
/// ever attached to a <see cref="Domain.Retrieval.RetrievalStatus.Verified"/>
/// <see cref="SourceResult{TPayload}"/> — the confirmed date itself already lives on
/// <see cref="Domain.Retrieval.SourceEvidence.ConfirmedDate"/>.
/// </summary>
public sealed record DatedFastLanePayload(IReadOnlyList<DatedFastLaneProduct> Products);

/// <summary>
/// One purchasable Fast Lane product for one confirmed date. Unlike
/// <see cref="DatedAdmissionProduct"/>, every live `FastLaneOnly` response observed during
/// investigation had exactly one package per distinct display name per date — no case of two
/// packages sharing a name (which is what would justify a comparison price) was ever seen — so
/// <see cref="ComparisonPrice"/> is always <c>null</c> in production. <see cref="PriceQualifier"/>
/// is also always <c>null</c>: no field in this response labels why a price is what it is, and
/// per the Phase 3C hardening principle this adapter does not infer one. There is likewise no
/// separate product-type/variant field distinct from <see cref="DisplayName"/> in the observed
/// schema (`"FAST LANE RESERVE"`/`"FAST LANE ULTIMATE"` are the only variant signal the source
/// provides), so no additional variant field is fabricated here — see
/// docs/retrieval/dated-fast-lane-source.md.
/// </summary>
public sealed record DatedFastLaneProduct(
    string PackageId,
    string DisplayName,
    decimal ExactDatePrice,
    decimal? ComparisonPrice,
    string? PriceQualifier,
    DateOnly ApplicableDate);
