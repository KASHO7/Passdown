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
}
