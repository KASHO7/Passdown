using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Display text for <see cref="PersonalSyncAutomationSnapshot"/> (Phase 16F §12/§17) — the automatic-sync
/// status line the Settings screen shows, layered on top of (never replacing) the existing
/// <see cref="PersonalSyncStatusPresentation"/> readiness chip. Deliberately never alarming/data-loss
/// language for <see cref="PersonalSyncAutomationState.Offline"/> (§11: "the user should understand work
/// is safe locally, sync will retry").
/// </summary>
public sealed record PersonalSyncAutomationStatusPresentation(string Text, string? DetailText)
{
    public static PersonalSyncAutomationStatusPresentation Build(PersonalSyncAutomationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var text = snapshot.State switch
        {
            PersonalSyncAutomationState.Off => "Off",
            PersonalSyncAutomationState.NotConfigured => "Not configured",
            PersonalSyncAutomationState.Syncing => "Syncing…",
            PersonalSyncAutomationState.UpToDate => "Up to date",
            PersonalSyncAutomationState.Pending => "Changes pending",
            PersonalSyncAutomationState.Offline => "Sync unavailable",
            PersonalSyncAutomationState.ConflictAttention => "Attention needed",
            PersonalSyncAutomationState.HistoryConflictAttention => "History sync conflict",
            PersonalSyncAutomationState.Error => "Sync error",
            _ => throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.State, null),
        };

        var detail = snapshot.State switch
        {
            PersonalSyncAutomationState.Offline => "Changes are saved locally and will sync automatically once the folder is reachable again.",
            PersonalSyncAutomationState.ConflictAttention => snapshot.ConflictCount == 1
                ? "1 item needs attention."
                : $"{snapshot.ConflictCount} items need attention.",
            PersonalSyncAutomationState.HistoryConflictAttention => "A finalized History record needs manual review — this is separate from an ordinary draft/note conflict.",
            PersonalSyncAutomationState.Error => snapshot.LastErrorMessage,
            _ => null,
        };

        return new PersonalSyncAutomationStatusPresentation(text, detail);
    }

    /// <summary>"Last synced: 2:41 PM" (§13, display-only — local time, never used for reconciliation ordering) — null until the first successful pass.</summary>
    public static string? BuildLastSyncedText(DateTimeOffset? lastSuccessfulSyncUtc) =>
        lastSuccessfulSyncUtc is { } value ? string.Create(PresentationCulture.UnitedStates, $"Last synced: {value.ToLocalTime():h:mm tt}") : null;
}
