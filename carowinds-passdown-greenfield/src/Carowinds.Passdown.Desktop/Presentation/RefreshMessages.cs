using Carowinds.Passdown.Application.Refresh;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Nontechnical progress/summary copy for the explicit "Check Online" action (spec §10.3, Phase
/// 4B Part F) — built only from <see cref="RefreshProgress"/>/<see cref="RefreshSummary"/>, which
/// already carry no Avalonia/technical-endpoint detail. Never surfaces
/// <see cref="Domain.Retrieval.SourceEvidence.TechnicalDetail"/> or a stack trace; that stays in
/// the app's own logging.
/// </summary>
public static class RefreshMessages
{
    public const string InProgressDefault = "Checking official information online…";

    public static string Progress(RefreshProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var category = SourceCategoryDisplay.FriendlyName(progress.LastCompleted.Category);
        return $"Checked {category} — {progress.CompletedOperations} of {progress.TotalOperations} complete";
    }

    public static string Summary(RefreshSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (summary.OperationsAttempted == 0)
        {
            return "Nothing to check for this date.";
        }

        if (summary.Failed == 0 && summary.Cancelled == 0)
        {
            return "Updated successfully.";
        }

        if (summary.Succeeded == 0)
        {
            return "Some information could not be refreshed. Your saved draft and previously verified information are still available.";
        }

        var unavailable = summary.Failed + summary.Cancelled;
        return $"Updated with {unavailable} source{(unavailable == 1 ? "" : "s")} unavailable.";
    }
}
