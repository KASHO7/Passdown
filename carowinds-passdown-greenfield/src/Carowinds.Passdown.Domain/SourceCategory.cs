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

    /// <summary>
    /// Ordinary (non-Duo) Bring-A-Friend's exact per-date price (accesso keyword
    /// <c>BAFWizardDailyTickets</c>), discovered anonymously on the official
    /// <c>getkeywordcalendar</c> endpoint (Phase 15A — supersedes Phase 13C's "passholder-login-only"
    /// finding; see docs/phase13-hardening.md's Phase 15A section). Tracked under its own dated
    /// category, distinct from <see cref="Promotions"/> (the generic "Starting From" catalog card)
    /// and <see cref="BringAFriendDuo"/> (a different product), so an exact-dated value is never
    /// confused with either. Never surfaced as its own display section — its value is merged into the
    /// existing Promotions-category "Bring-A-Friend Ticket" row at composition time.
    /// </summary>
    DatedBringAFriend,

    /// <summary>
    /// Military and First Responder exact per-date ticket prices (accesso keyword
    /// <c>MilitaryWizardDailyTickets</c>) — the date calendar the official Military &amp; First
    /// Responder "Buy Now" store opens, which shows each date's price before any ID.me step (Phase
    /// 21A; docs/phase21a-pricing-historical-integrity.md). Distinct from
    /// <see cref="MilitaryFirstResponder"/> (the generic "Starting From" catalog card) so an exact
    /// dated value is never confused with the marketing minimum. Merged into that card's row at
    /// composition time when both eligibilities agree on one amount.
    /// </summary>
    DatedMilitaryFirstResponder,

    /// <summary>
    /// Official date-specific entertainment schedule — show names, venues, and start times from the
    /// <c>shows</c> array of the same official FIMS operating-hours feed <see cref="ParkHours"/> reads
    /// (Phase 21C; docs/phase21c-official-show-schedules.md). Dated scope; operational information,
    /// never a priced product — it never composes into any product section or Teams Copy output.
    /// </summary>
    ShowSchedule,
}
