namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// The unattended-execution entry point onto <see cref="IRefreshOrchestrator"/> (Phase 6A Part
/// D): logs start/summary/completion and classifies the run for a host's exit code, but owns none
/// of the actual freshness planning, persistence, retry, or partial-failure semantics — all of
/// that already belongs to <see cref="IRefreshOrchestrator"/>/<see cref="RefreshOrchestrator"/>.
/// Always calls <see cref="IRefreshOrchestrator.RunScheduledRefreshAsync"/> — never
/// <see cref="IRefreshOrchestrator.RunUserForcedRefreshAsync"/> — regardless of <paramref name="mode"/>;
/// see the <c>RunAsync</c> parameter doc.
/// </summary>
public interface IScheduledRefreshRunner
{
    /// <param name="mode">
    /// Phase 13B Part A: which caller triggered this run — <see cref="RefreshMode.Scheduled"/> (the
    /// unattended <c>--scheduled-refresh</c> process) or <see cref="RefreshMode.StartupCatchUp"/>
    /// (the interactive-startup background catch-up). Recorded only for accurate logging; both
    /// values still run the identical due-based rolling-window job (Phase 13A confirmed that
    /// sharing is correct). <see cref="RefreshMode.UserForced"/> is never valid here — that mode
    /// never reaches this runner at all.
    /// </param>
    Task<ScheduledRefreshResult> RunAsync(RefreshMode mode, CancellationToken cancellationToken = default);
}
