namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Why a sync pass was requested (Phase 16F §2/§29) — carried only for logging; every reason funnels
/// through the same <see cref="IPersonalSyncCoordinator"/> scheduling/coalescing rules.
/// </summary>
public enum SyncTriggerReason
{
    /// <summary>The app just started and Personal Sync is configured (§3).</summary>
    Startup,

    /// <summary>A local Associate draft or Operational Note save marked something dirty (§4).</summary>
    LocalChange,

    /// <summary>A finalization produced a new History record while the app was running (§5).</summary>
    Finalization,

    /// <summary>The low-frequency background safety sweep (§6).</summary>
    Periodic,

    /// <summary>A prior pass hit a transient failure; this is the scheduled backoff retry (§10).</summary>
    Retry,

    /// <summary>The user clicked "Sync Now" (§27).</summary>
    Manual,
}

/// <summary>
/// The richer automatic-sync status the Settings screen renders (Phase 16F §12), layered on top of
/// (never replacing) <see cref="PersonalSyncStatus"/> — that type still answers "is the folder/manifest
/// configured and reachable," this type answers "what is the background coordinator doing with it."
/// </summary>
public enum PersonalSyncAutomationState
{
    /// <summary>Personal Sync is turned off.</summary>
    Off,

    /// <summary>Enabled but no folder has been chosen/validated yet.</summary>
    NotConfigured,

    /// <summary>A sync pass is running right now.</summary>
    Syncing,

    /// <summary>Everything local is confirmed shared and no attention is needed.</summary>
    UpToDate,

    /// <summary>Local changes exist that have not yet been confirmed synced.</summary>
    Pending,

    /// <summary>The configured folder is unreachable right now; local work is unaffected and a retry is scheduled.</summary>
    Offline,

    /// <summary>One or more mutable (draft/note) conflicts need an explicit Keep Mine/Use Theirs choice.</summary>
    ConflictAttention,

    /// <summary>A finalized-History integrity/dual-finalization condition needs manual review — kept distinct from an ordinary mutable conflict (§16) so it is never mistaken for one.</summary>
    HistoryConflictAttention,

    /// <summary>The folder/manifest itself is structurally invalid or an unsupported (newer) schema — retrying will not fix this.</summary>
    Error,
}

/// <summary>
/// Everything the Settings screen needs to render automatic-sync status (Phase 16F §12/§13/§14/§15/§16)
/// — computed by <see cref="IPersonalSyncCoordinator"/> after every pass, never persisted longer than
/// this process's lifetime beyond the two UTC timestamps below (display-only metadata, §13).
/// </summary>
public sealed record PersonalSyncAutomationSnapshot(
    PersonalSyncAutomationState State,
    bool IsSyncRunning,
    DateTimeOffset? LastSuccessfulSyncUtc,
    DateTimeOffset? LastAttemptUtc,
    bool HasPendingChanges,
    int ConflictCount,
    int HistoryConflictCount,
    string? LastResultSummary,
    string? LastErrorMessage,
    int RetryAttempt)
{
    public static PersonalSyncAutomationSnapshot Initial { get; } =
        new(PersonalSyncAutomationState.Off, false, null, null, false, 0, 0, null, null, 0);
}
