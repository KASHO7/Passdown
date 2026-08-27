namespace Carowinds.Passdown.Domain;

/// <summary>
/// The data groups that carry their own freshness cadence per spec §6.1.
/// General and season-pass add-ons share one cadence, so they share one category.
/// </summary>
public enum SourceCategory
{
    ParkHours,
    DatedAdmissionTickets,
    DatedFastLane,
    HauntedAttractions,
    Promotions,
    SeasonPasses,
    AddOns,

    /// <summary>
    /// "Celebrate America! Bring-A-Friend Duo Offer" — a dated, per-date exact price for the
    /// passholder-exclusive two-friends bundle (accesso keyword <c>SUBAF50</c>), tracked separately
    /// from ordinary Bring-A-Friend (which stays under <see cref="Promotions"/>, no dated source
    /// exists for it — see Phase 13C docs) and from Daily Admission. Dated scope (Phase 13C).
    /// </summary>
    BringAFriendDuo,

    /// <summary>
    /// Military / First Responder discounted admission (accesso keyword <c>MilitaryWizard</c>),
    /// tracked separately from ordinary Daily Admission so eligibility-restricted pricing is never
    /// presented as unrestricted. Catalog scope (Phase 13C).
    /// </summary>
    MilitaryFirstResponder,
}
