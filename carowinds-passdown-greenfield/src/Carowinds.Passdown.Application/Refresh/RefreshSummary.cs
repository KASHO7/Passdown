namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// The outcome of one refresh job (Part I). Never discards an individual source's result just
/// because another source in the same job failed (Part G/I) — <see cref="Results"/> always holds
/// one entry per planned operation, including cancelled ones.
/// </summary>
/// <param name="WasSkippedDueToOverlap">
/// Phase 13B Part K/L: <see langword="true"/> when this summary represents a
/// <see cref="RefreshMode.UserForced"/> request that was never actually run because another
/// refresh already held the shared overlap lock (<see cref="Carowinds.Passdown.Application.Concurrency.IScheduledRefreshLock"/>) —
/// every count is zero and <see cref="Results"/> is empty in that case. Always <see langword="false"/>
/// for a job that actually executed, whatever its outcome.
/// </param>
public sealed record RefreshSummary(
    RefreshJobType JobType,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int OperationsAttempted,
    int Succeeded,
    int Failed,
    int Cancelled,
    IReadOnlyList<SourceOperationResult> Results,
    bool WasSkippedDueToOverlap = false)
{
    /// <summary>Phase 13B Part L: a <see cref="RefreshMode.UserForced"/> request that could not run because a refresh was already in progress — never a duplicate attempt, never a raw lock exception surfaced to a caller.</summary>
    public static RefreshSummary AlreadyInProgress(DateTimeOffset now) =>
        new(RefreshJobType.UserForced, now, now, OperationsAttempted: 0, Succeeded: 0, Failed: 0, Cancelled: 0, Results: [], WasSkippedDueToOverlap: true);
}
