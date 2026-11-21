using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Domain.Seasons;

/// <summary>
/// Phase 31: decides whether a current-catalog product that belongs only to one seasonal event (e.g.
/// "SCarowinds Bundles") is still a current product. Product availability is not the same as event
/// dates — an event product is commonly sold before its event starts — so the rule only ever demotes a
/// product when <b>both</b> hold:
/// <list type="number">
/// <item>the official schedule shows that event's latest occurrence (started on or before today) has
/// ended, and</item>
/// <item>no successful official check has listed the product since that occurrence ended.</item>
/// </list>
/// A product re-listed by an official check after the event ended is still on sale and stays current.
/// An unknown schedule, or a product tagged with the regular park season too, is never demoted.
/// </summary>
public static class SeasonalProductAvailability
{
    public static bool IsPastSeason(
        ProductSeasonTags tags,
        DateTimeOffset? lastVerifiedAt,
        OperatingEventSchedule? schedule,
        DateOnly today,
        out OfficialOperatingEvent? endedOccurrence)
    {
        endedOccurrence = null;

        var kind = SingleEventKind(tags);
        if (kind is null)
        {
            return false;
        }

        if (!SeasonResolver.HasEnded(schedule, kind.Value, today, out endedOccurrence))
        {
            return false;
        }

        // Verification instants are recorded in America/New_York, so the instant's own clock is the
        // Eastern calendar day.
        if (lastVerifiedAt is { } verified && DateOnly.FromDateTime(verified.DateTime) > endedOccurrence!.EndDate)
        {
            endedOccurrence = null;
            return false;
        }

        return true;
    }

    /// <summary>The one seasonal event a product belongs to exclusively, or null (none, several, or also the regular park season).</summary>
    public static OperatingEventKind? SingleEventKind(ProductSeasonTags tags)
    {
        if (tags.HasFlag(ProductSeasonTags.Carowinds))
        {
            return null;
        }

        return tags switch
        {
            ProductSeasonTags.SCarowinds => OperatingEventKind.SCarowinds,
            ProductSeasonTags.WinterFest => OperatingEventKind.WinterFest,
            ProductSeasonTags.Oktoberfest => OperatingEventKind.Oktoberfest,
            _ => null,
        };
    }
}
