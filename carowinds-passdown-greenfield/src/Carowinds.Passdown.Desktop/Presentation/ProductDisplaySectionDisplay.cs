using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Nontechnical display names for <see cref="ProductDisplaySection"/> (spec §10.3 "use plain
/// language, not internal names"; Phase 15A) — presentation copy only, so it lives here rather than
/// in Application. Deliberately duplicated from <c>Documents.Associate.PdfCategoryDisplayNames</c>
/// rather than shared, matching the existing <see cref="SourceCategoryDisplay"/> convention.
/// </summary>
public static class ProductDisplaySectionDisplay
{
    public static string FriendlyName(ProductDisplaySection section) => section switch
    {
        ProductDisplaySection.DailyAdmission => "Daily Admission",
        ProductDisplaySection.FastLane => "Fast Lane",
        ProductDisplaySection.SCarowinds => "SCarowinds",
        ProductDisplaySection.SeasonPasses => "Season Passes",
        ProductDisplaySection.AddOns => "Add-Ons",
        ProductDisplaySection.Promotions => "Promotions",
        ProductDisplaySection.Memberships => "Memberships",
        _ => section.ToString(),
    };
}
