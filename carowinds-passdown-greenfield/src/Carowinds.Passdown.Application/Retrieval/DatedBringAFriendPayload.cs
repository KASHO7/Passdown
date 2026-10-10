namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Official exact-date pricing for ordinary (non-Duo) "Bring-A-Friend Ticket" (Phase 15A; see
/// docs/phase13-hardening.md's Phase 15A section for the live investigation this is built from —
/// accesso keyword <c>BAFWizardDailyTickets</c>, discovered anonymously via <c>getkeywordcalendar</c>
/// the same way <c>SUBAF50</c> was in Phase 13C). Supersedes Phase 13C's "passholder-login-only"
/// finding for this product. Only ever attached to a
/// <see cref="Domain.Retrieval.RetrievalStatus.Verified"/> <see cref="SourceResult{TPayload}"/>.
/// </summary>
public sealed record DatedBringAFriendPayload(IReadOnlyList<DatedBringAFriendProduct> Products);

/// <summary>
/// One confirmed-date ordinary Bring-A-Friend package. The live <c>BAFWizardDailyTickets</c>
/// response returns two packages per date under this one keyword — "Celebrate America!
/// Bring-A-Friend Duo Offer" (already covered by <c>DatedBringAFriendDuoSource</c>'s own
/// <c>SUBAF50</c> keyword) and "Bring-A-Friend Ticket" — so the adapter selects only the package
/// named "Bring-A-Friend Ticket", never both. <see cref="ComparisonPrice"/> is always <c>null</c>: no
/// live response for this keyword ever included one (the official "$80 Gate Price" comparison lives
/// only on the static catalog card, already retrieved by <c>PromotionsCatalogSource</c>) — composition
/// pairs this exact price with that already-sourced comparison value rather than fabricating one here.
/// </summary>
public sealed record DatedBringAFriendProduct(
    string PackageId,
    string DisplayName,
    decimal Price,
    DateOnly ApplicableDate);
