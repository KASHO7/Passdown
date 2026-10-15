using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Catalog;

/// <summary>
/// Phase 19B: deterministic, evidence-based <see cref="ProductSeasonTags"/> classification, computed
/// at composition time from fields already stored (like <see cref="PriceSemanticsClassifier"/>) —
/// never persisted, so a newer build re-classifying a product can never alter an older finalized
/// snapshot. Evidence, strongest first:
/// <list type="number">
/// <item>The official source the product was retrieved from (<see cref="SourceCategory"/>): the
/// SCarowinds event page's dated keywords are SCarowinds; the regular park's daily-ticket calendar,
/// Fast Lane, season-pass, add-on and Military/First Responder pages are Carowinds.</item>
/// <item>The event's own brand name in the official product name ("SCarowinds", "WinterFest") —
/// the same established rule <see cref="Associate.ProductDisplaySectionResolver"/> already uses to
/// move a "SCarowinds Bundles" promotion card into the SCarowinds section. Additive to (1), so a
/// regular-park product naming an event carries both tags.</item>
/// </list>
/// Promotions, the dated Bring-A-Friend categories and custom products have no source-level season
/// signal, so without an event name they stay <see cref="ProductSeasonTags.None"/> rather than being
/// guessed. The calendar date is never an input.
/// </summary>
public static class ProductSeasonClassifier
{
    public static ProductSeasonTags Classify(SourceCategory? category, string? displayName) =>
        FromSourceCategory(category) | FromOfficialName(displayName);

    /// <summary>The tags a product inherits purely from the official source it was retrieved from.</summary>
    public static ProductSeasonTags FromSourceCategory(SourceCategory? category) => category switch
    {
        SourceCategory.DatedAdmissionTickets
            or SourceCategory.DatedFastLane
            or SourceCategory.SeasonPasses
            or SourceCategory.AddOns
            or SourceCategory.MilitaryFirstResponder => ProductSeasonTags.Carowinds,
        SourceCategory.HauntedAttractions => ProductSeasonTags.SCarowinds,
        _ => ProductSeasonTags.None,
    };

    /// <summary>Event tags named by the official product name itself (case-insensitive brand names only — never a product-specific string).</summary>
    public static ProductSeasonTags FromOfficialName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return ProductSeasonTags.None;
        }

        var tags = ProductSeasonTags.None;
        if (displayName.Contains("SCarowinds", StringComparison.OrdinalIgnoreCase))
        {
            tags |= ProductSeasonTags.SCarowinds;
        }

        if (displayName.Contains("WinterFest", StringComparison.OrdinalIgnoreCase))
        {
            tags |= ProductSeasonTags.WinterFest;
        }

        return tags;
    }

    /// <summary>
    /// True for a dated category that exists only for a seasonal event (today: the SCarowinds haunted
    /// keywords). Its "not published" answer for a date means the event is not running then — a normal
    /// seasonal fact, so the empty section is omitted rather than shown as a status heading.
    /// </summary>
    public static bool IsEventOnlyCategory(SourceCategory category)
    {
        var tags = FromSourceCategory(category);
        return tags != ProductSeasonTags.None && !tags.HasFlag(ProductSeasonTags.Carowinds);
    }
}
