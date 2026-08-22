namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// The unattended-execution entry point onto <see cref="IRefreshOrchestrator"/> (Phase 6A Part
/// D): logs start/summary/completion and classifies the run for a host's exit code, but owns none
/// of the actual freshness planning, persistence, retry, or partial-failure semantics — all of
/// that already belongs to <see cref="IRefreshOrchestrator"/>/<see cref="RefreshOrchestrator"/>.
/// </summary>
public interface IScheduledRefreshRunner
{
    Task<ScheduledRefreshResult> RunAsync(CancellationToken cancellationToken = default);
}
