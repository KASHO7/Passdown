namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>How one <see cref="IPersonalSyncService.RunAsync"/> pass ended (Phase 16C §24).</summary>
public enum PersonalSyncOutcome
{
    /// <summary>The pass ran to completion (individual malformed/I-O failures may still have occurred — see the counts).</summary>
    Completed,

    /// <summary>Personal Sync is turned off — nothing was touched.</summary>
    Disabled,

    /// <summary>Sync is enabled but no folder has been chosen yet.</summary>
    NotConfigured,

    /// <summary>The configured folder could not be reached right now (missing/offline/locked) — may resolve itself; local data is unaffected.</summary>
    FolderUnavailable,

    /// <summary>The configured folder or its manifest is structurally invalid/unsupported.</summary>
    ConfigurationError,

    /// <summary>Another sync pass was already running in this process — this call did nothing (Phase 16C §27).</summary>
    AlreadyRunning,
}

/// <summary>
/// Structured outcome of one <see cref="IPersonalSyncService.RunAsync"/> pass (Phase 16C §24) — enough
/// for the Phase 16C "Sync Now" status line and for Phase 16F's richer status UX later. An individual
/// malformed file or a single failed export never turns the whole pass into a failure; those are
/// counted, not thrown.
/// </summary>
public sealed record PersonalSyncResult(
    PersonalSyncOutcome Outcome,
    int ImportedCount,
    int ExportedCount,
    int UnchangedCount,
    int ConflictCount,
    int MalformedCount,
    int IoFailureCount,
    string? Message)
{
    public static PersonalSyncResult AlreadyRunning() =>
        new(PersonalSyncOutcome.AlreadyRunning, 0, 0, 0, 0, 0, 0, "A Personal Sync pass is already running.");

    public static PersonalSyncResult Disabled() =>
        new(PersonalSyncOutcome.Disabled, 0, 0, 0, 0, 0, 0, null);

    public static PersonalSyncResult NotReady(PersonalSyncStatus status, string? statusDetail)
    {
        var outcome = status switch
        {
            PersonalSyncStatus.NotConfigured => PersonalSyncOutcome.NotConfigured,
            PersonalSyncStatus.FolderUnavailable => PersonalSyncOutcome.FolderUnavailable,
            PersonalSyncStatus.ConfigurationError => PersonalSyncOutcome.ConfigurationError,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Expected a not-ready Personal Sync status."),
        };

        return new PersonalSyncResult(outcome, 0, 0, 0, 0, 0, 0, statusDetail);
    }
}

/// <summary>
/// One full Personal Sync pass over Associate drafts and Operational Notes (Phase 16C §23):
/// import every valid remote file, detect (never auto-resolve) conflicts, then export local
/// pending/bootstrap changes. Orchestration only — no ViewModel ever contains this logic (§23).
/// </summary>
public interface IPersonalSyncService
{
    Task<PersonalSyncResult> RunAsync(CancellationToken cancellationToken = default);
}
