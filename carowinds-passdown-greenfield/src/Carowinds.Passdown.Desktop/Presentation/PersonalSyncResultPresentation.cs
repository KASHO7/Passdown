using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>Display text for one <see cref="PersonalSyncResult"/> (Phase 16C §26, Phase 16E §26/§30) — a simple counts line, plus conflict callouts when present. No resolution controls for History conflicts; those require manual/admin review (spec §13/§30).</summary>
public static class PersonalSyncResultPresentation
{
    public static string Format(PersonalSyncResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Outcome)
        {
            case PersonalSyncOutcome.AlreadyRunning:
                return "A sync pass is already running.";
            case PersonalSyncOutcome.Disabled:
                return "Personal Sync is off.";
            case PersonalSyncOutcome.NotConfigured:
                return "Choose a folder before syncing.";
            case PersonalSyncOutcome.FolderUnavailable:
                return result.Message is { Length: > 0 } unavailableDetail ? $"Sync folder unavailable: {unavailableDetail}" : "Sync folder unavailable.";
            case PersonalSyncOutcome.ConfigurationError:
                return result.Message is { Length: > 0 } configDetail ? $"Sync configuration error: {configDetail}" : "Sync configuration error.";
            case PersonalSyncOutcome.Completed:
                var summary = $"Synced: {result.ImportedCount} imported, {result.ExportedCount} exported, {result.UnchangedCount} unchanged.";
                if (result.ConflictCount > 0)
                {
                    summary += $" {result.ConflictCount} conflict{(result.ConflictCount == 1 ? "" : "s")} detected — not resolved automatically.";
                }
                if (result.MalformedCount > 0)
                {
                    summary += $" {result.MalformedCount} file{(result.MalformedCount == 1 ? "" : "s")} could not be read.";
                }
                if (result.IoFailureCount > 0)
                {
                    summary += $" {result.IoFailureCount} write{(result.IoFailureCount == 1 ? "" : "s")} failed and will retry next time.";
                }
                var historySynced = result.HistoryImportedCount + result.HistoryExportedCount;
                if (historySynced > 0)
                {
                    summary += $" {historySynced} history item{(historySynced == 1 ? "" : "s")} synced.";
                }
                if (result.HistoryConflictCount > 0)
                {
                    summary += $" {result.HistoryConflictCount} history conflict{(result.HistoryConflictCount == 1 ? "" : "s")} — requires manual review.";
                }
                if (result.HistoryIncompleteCount > 0)
                {
                    summary += $" {result.HistoryIncompleteCount} history item{(result.HistoryIncompleteCount == 1 ? "" : "s")} waiting for their PDF.";
                }
                // Phase 26: observations, never "price changes" — one new observation can confirm a known price.
                if (result.PriceObservationsMergedCount > 0)
                {
                    summary += $" {OfficialPriceHistorySummary.MergedText(result.PriceObservationsMergedCount)}";
                }
                if (result.PriceObservationsSkippedCount > 0)
                {
                    summary += $" {OfficialPriceHistorySummary.SkippedText(result.PriceObservationsSkippedCount)}";
                }
                // Phase 30: only when Sync Morning Messages is on; never counted as drafts/notes.
                if (result.MorningMessages is { } messages)
                {
                    summary += " " + MorningMessageText(messages);
                }
                return summary;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null);
        }
    }

    private static string MorningMessageText(Application.MorningMessage.Sync.MorningMessageSyncResult messages)
    {
        var parts = new List<string>();
        if (messages.DatesUpdated > 0)
        {
            parts.Add($"{messages.DatesUpdated} morning message{(messages.DatesUpdated == 1 ? "" : "s")} updated from another computer");
        }

        if (messages.PreferencesUpdated)
        {
            parts.Add("morning message preferences updated");
        }

        if (messages.ConflictCount > 0)
        {
            parts.Add($"{messages.ConflictCount} morning message conflict{(messages.ConflictCount == 1 ? "" : "s")} to review");
        }

        if (messages.DeferredCount > 0)
        {
            parts.Add("morning message changes waiting until the composer is closed");
        }

        return parts.Count == 0 ? "Morning messages up to date." : "Morning messages: " + string.Join("; ", parts) + ".";
    }
}
