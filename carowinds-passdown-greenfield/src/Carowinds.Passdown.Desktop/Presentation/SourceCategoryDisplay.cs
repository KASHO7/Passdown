using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Nontechnical display names for <see cref="SourceCategory"/> (spec §10.3 "use plain language,
/// not internal names") — presentation copy only, so it lives here rather than in Application.
/// </summary>
public static class SourceCategoryDisplay
{
    public static string FriendlyName(SourceCategory category) => category switch
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
        SourceCategory.DatedBringAFriend => "Bring-A-Friend",
        _ => category.ToString(),
    };
}
