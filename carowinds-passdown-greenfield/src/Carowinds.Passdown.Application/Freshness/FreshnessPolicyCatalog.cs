using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Freshness;

/// <summary>
/// Builds the one <see cref="FreshnessPolicy"/> per <see cref="SourceCategory"/>
/// (spec §6.1's freshness-tiers table) from <see cref="FreshnessOptions"/>.
///
/// Dated haunted/SCarowinds products and short-term promotions get the same daily
/// cadence as park hours/admission/Fast Lane: the "while published/active" and
/// "with validity if known" qualifiers in spec §6.1 are about *whether* a source is
/// even worth checking at all (a seasonal-visibility/reconciliation concern, out of
/// scope here — spec §7.1), not a different refresh formula. Because persisted
/// scheduling state is keyed by an independent source id, not by category (spec
/// §6.2), a later phase can register a second, lightweight off-season source id
/// under <see cref="SourceCategory.HauntedAttractions"/> without changing this
/// catalog's shape or any cadence type.
/// </summary>
public static class FreshnessPolicyCatalog
{
    public static IReadOnlyDictionary<SourceCategory, FreshnessPolicy> Build(FreshnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var daily = new FreshnessCadence.DailyAt(options.DailyRefreshTimeEastern);

        return new Dictionary<SourceCategory, FreshnessPolicy>
        {
            [SourceCategory.ParkHours] = new(SourceCategory.ParkHours, daily),
            [SourceCategory.DatedAdmissionTickets] = new(SourceCategory.DatedAdmissionTickets, daily),
            [SourceCategory.DatedFastLane] = new(SourceCategory.DatedFastLane, daily),
            [SourceCategory.HauntedAttractions] = new(SourceCategory.HauntedAttractions, daily),
            [SourceCategory.Promotions] = new(SourceCategory.Promotions, daily),
            [SourceCategory.SeasonPasses] = new(
                SourceCategory.SeasonPasses,
                new FreshnessCadence.RollingInterval(TimeSpan.FromDays(options.SeasonPassRefreshIntervalDays))),
            [SourceCategory.AddOns] = new(
                SourceCategory.AddOns,
                new FreshnessCadence.RollingInterval(TimeSpan.FromDays(options.AddOnRefreshIntervalDays))),
        };
    }
}
