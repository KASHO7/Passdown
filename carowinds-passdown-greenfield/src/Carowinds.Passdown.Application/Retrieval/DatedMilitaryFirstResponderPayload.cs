namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Official exact-date Military and First Responder ticket pricing (Phase 21A; accesso keyword
/// <c>MilitaryWizardDailyTickets</c>, read anonymously from <c>getkeywordcalendar</c> — the same
/// calendar the official Military &amp; First Responder store shows before its ID.me step). Only
/// ever attached to a <see cref="Domain.Retrieval.RetrievalStatus.Verified"/>
/// <see cref="SourceResult{TPayload}"/>.
/// </summary>
public sealed record DatedMilitaryFirstResponderPayload(IReadOnlyList<DatedMilitaryFirstResponderProduct> Products);

/// <summary>Who a dated Military/First Responder ticket is for — read from the official package name, never inferred.</summary>
public enum MilitaryFirstResponderEligibility
{
    Military,
    FirstResponder,
}

/// <summary>
/// One confirmed-date ticket price for one eligibility. The live response can carry more than one
/// package per eligibility on the same date (e.g. "Fall Military Ticket" and "Military Ticket"); the
/// adapter only produces a product when every such package agrees on one amount, so
/// <see cref="Price"/> is never a minimum across differing options. <see cref="DisplayName"/> is the
/// stable official base name ("Military Ticket"/"First Responder Ticket"); no comparison price is
/// carried because the calendar response never includes one.
/// </summary>
public sealed record DatedMilitaryFirstResponderProduct(
    string PackageId,
    string DisplayName,
    MilitaryFirstResponderEligibility Eligibility,
    decimal Price,
    DateOnly ApplicableDate);
