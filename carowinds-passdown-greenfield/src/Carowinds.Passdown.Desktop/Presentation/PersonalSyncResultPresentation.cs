using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>Display text for one <see cref="PersonalSyncResult"/> (Phase 16C §26) — a simple counts line, plus a conflict callout when present. No resolution controls; that's 16D.</summary>
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
                return summary;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null);
        }
    }
}
