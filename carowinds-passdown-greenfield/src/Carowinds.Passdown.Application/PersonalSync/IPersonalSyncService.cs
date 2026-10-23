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
/// <param name="HistoryImportedCount">Finalized History/PDF pairs newly imported this pass (Phase 16E §23) — a subset already included in <paramref name="ImportedCount"/>, broken out for status-line visibility distinct from drafts/notes.</param>
/// <param name="HistoryExportedCount">Finalized History/PDF pairs newly exported this pass — a subset already included in <paramref name="ExportedCount"/>.</param>
/// <param name="HistoryUnchangedCount">Finalized History records that converged/were already in sync (self-authored re-scan or identical content-hash dedup, Phase 16E §12) — a subset already included in <paramref name="UnchangedCount"/>.</param>
/// <param name="HistoryConflictCount">Finalized History integrity/dual-finalization conditions that require manual review (Phase 16E §13/§14/§17) — same-id-different-hash, independent same-date finalizations, and corrupt/hash-mismatched PDFs. A subset already included in <paramref name="ConflictCount"/>; never auto-resolved.</param>
/// <param name="PriceObservationsMergedCount">Phase 26: verified official price observations from other computers that this computer did not have before (observations, not price changes). Never counted in <paramref name="ImportedCount"/>.</param>
/// <param name="PriceObservationsSkippedCount">Phase 26: incoming price observations that failed verification or conflicted with a stored identity — skipped, never written; local history kept.</param>
/// <param name="IncludesOfficialPriceHistory">Phase 26: this pass also exchanged official price history.</param>
/// <param name="HistoryIncompleteCount">Finalized History JSON envelopes seen with no PDF companion present yet (Phase 16E §16) — not a failure, not a conflict; expected to resolve on a later pass once the PDF arrives. Never counted anywhere else.</param>
public sealed record PersonalSyncResult(
    PersonalSyncOutcome Outcome,
    int ImportedCount,
    int ExportedCount,
    int UnchangedCount,
    int ConflictCount,
    int MalformedCount,
    int IoFailureCount,
    string? Message,
    int HistoryImportedCount = 0,
    int HistoryExportedCount = 0,
    int HistoryUnchangedCount = 0,
    int HistoryConflictCount = 0,
    int HistoryIncompleteCount = 0,
    int PriceObservationsMergedCount = 0,
    int PriceObservationsSkippedCount = 0,
    bool IncludesOfficialPriceHistory = false)
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
/// One full Personal Sync pass over Associate drafts, Operational Notes, and finalized History/PDFs
/// (Phase 16C §23, Phase 16E §23): import every valid remote file, detect (never auto-resolve)
/// conflicts, then export local pending/bootstrap changes. Orchestration only — no ViewModel ever
/// contains this logic (§23). One broken History record never blocks draft/note sync, and vice versa
/// (Phase 16E §23).
/// </summary>
public interface IPersonalSyncService
{
    Task<PersonalSyncResult> RunAsync(CancellationToken cancellationToken = default);
}
