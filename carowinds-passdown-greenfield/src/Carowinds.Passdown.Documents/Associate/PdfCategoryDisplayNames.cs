using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// Human-readable <see cref="ProductDisplaySection"/> labels for the PDF's product section headings
/// (Phase 15A — previously keyed by <see cref="Carowinds.Passdown.Domain.SourceCategory"/> directly,
/// which is what let the same real-world product appear under two differently-named headings).
/// Deliberately duplicated from <c>Desktop.Presentation.ProductDisplaySectionDisplay</c> rather than
/// shared: Documents must not depend on Desktop (project rule), and this is a two-line lookup table,
/// not logic worth an extra shared project for.
/// </summary>
internal static class PdfCategoryDisplayNames
{
    public static string For(ProductDisplaySection section) => section switch
    {
        ProductDisplaySection.DailyAdmission => "Daily Admission",
        ProductDisplaySection.FastLane => "Fast Lane",
        ProductDisplaySection.SCarowinds => "SCarowinds",
        ProductDisplaySection.SeasonPasses => "Season Passes",
        ProductDisplaySection.AddOns => "Add-Ons",
        ProductDisplaySection.Promotions => "Promotions",
        _ => section.ToString(),
    };
}
