namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Official exact-date haunted-attraction (SCarowinds) products for one confirmed date (Phase 3E;
/// see docs/retrieval/dated-haunted-source.md for the live investigation this is built from). Only
/// ever attached to a <see cref="Domain.Retrieval.RetrievalStatus.Verified"/>
/// <see cref="SourceResult{TPayload}"/> — the confirmed date itself already lives on
/// <see cref="Domain.Retrieval.SourceEvidence.ConfirmedDate"/>. Carries no season-date-range
/// assumption anywhere: whether SCarowinds is currently active is determined solely by what a
/// given <c>RetrieveAsync</c> call for a given date actually returns (spec §7.1 — no hardcoded
/// seasonal visibility).
/// </summary>
public sealed record DatedHauntedPayload(IReadOnlyList<DatedHauntedProduct> Products);

/// <summary>
/// One purchasable haunted-attraction product for one confirmed date. Like
/// <see cref="DatedFastLaneProduct"/> and unlike <see cref="DatedAdmissionProduct"/>, every live
/// `HAPWizard` response observed during investigation had exactly one package per distinct display
/// name per date, so <see cref="ComparisonPrice"/> is always <c>null</c> in production. No field in
/// the response labels why a price is what it is, so <see cref="PriceQualifier"/> is always
/// <c>null</c> too (Phase 3C's hardening principle, applied here from the start). There is likewise
/// no separate product-type/variant field distinct from <see cref="DisplayName"/>
/// (`"Haunted Attractions Pass"`/`"Haunted Attractions Express Pass"` are the only variant signal
/// the source provides) — see docs/retrieval/dated-haunted-source.md.
/// </summary>
public sealed record DatedHauntedProduct(
    string PackageId,
    string DisplayName,
    decimal ExactDatePrice,
    decimal? ComparisonPrice,
    string? PriceQualifier,
    DateOnly ApplicableDate);
