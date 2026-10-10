namespace Carowinds.Passdown.Application.Persistence;

/// <summary>Whether the most recently completed Deep Refresh (Phase 13D Part U) fully succeeded, partially failed, or failed outright — never falsely marked Success when it wasn't.</summary>
public enum DeepRefreshOutcome
{
    Success,
    Partial,
    Failed,
}

/// <summary>
/// The single persisted status row for the most recent Deep Refresh operation (Phase 13D Part U),
/// regardless of which caller ran it. <see cref="LastAttemptAtUtc"/> is recorded for every completed
/// run; <see cref="LastSuccessAtUtc"/> only when <see cref="Outcome"/> is
/// <see cref="DeepRefreshOutcome.Success"/> — distinguishing "attempted" from "fully succeeded" (Part
/// U). <see cref="CoverageThrough"/> reflects the actual persisted Park Hours coverage this run
/// achieved, never the desired target (Part AR) — null if no date was successfully covered at all.
/// </summary>
public sealed record DeepRefreshStatusRecord(
    DateTimeOffset LastAttemptAtUtc,
    DateTimeOffset? LastSuccessAtUtc,
    DeepRefreshOutcome Outcome,
    DateOnly? CoverageThrough,
    int SourceSuccessCount,
    int SourceFailureCount);

/// <summary>SQLite-backed persistence port for <see cref="DeepRefreshStatusRecord"/> — always exactly one current row (Phase 13D Part U).</summary>
public interface IDeepRefreshStatusRepository
{
    Task<DeepRefreshStatusRecord?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(DeepRefreshStatusRecord status, CancellationToken cancellationToken = default);
}
