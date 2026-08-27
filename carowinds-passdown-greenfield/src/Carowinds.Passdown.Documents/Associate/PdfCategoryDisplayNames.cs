using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// Human-readable <see cref="SourceCategory"/> labels for the PDF's product section headings.
/// Deliberately duplicated from <c>Desktop.Presentation.SourceCategoryDisplay</c> rather than
/// shared: Documents must not depend on Desktop (project rule), and this is a two-line lookup
/// table, not logic worth an extra shared project for.
/// </summary>
internal static class PdfCategoryDisplayNames
{
    public static string For(SourceCategory category) => category switch
    {
        SourceCategory.ParkHours => "Park Hours",
        SourceCategory.DatedAdmissionTickets => "Daily Admission",
        SourceCategory.DatedFastLane => "Fast Lane",
        SourceCategory.HauntedAttractions => "SCarowinds",
        SourceCategory.SeasonPasses => "Season Passes",
        SourceCategory.AddOns => "Add-Ons",
        SourceCategory.Promotions => "Promotions",
        SourceCategory.BringAFriendDuo => "Bring-A-Friend Duo",
        SourceCategory.MilitaryFirstResponder => "Military & First Responder",
        _ => category.ToString(),
    };
}
