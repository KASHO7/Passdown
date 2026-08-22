using System.Reflection;
using Carowinds.Passdown.Application.Time;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// <see cref="IScheduledRefreshRunner"/> built on <see cref="IRefreshOrchestrator.RunScheduledRefreshAsync"/>
/// (Phase 6A Part D). Never calls <see cref="IRefreshOrchestrator.RunManualDateRefreshAsync"/> —
/// unattended execution always runs the due-sources rolling-window job, never a single-date check.
/// </summary>
public sealed class ScheduledRefreshRunner(
    IRefreshOrchestrator orchestrator,
    IClock clock,
    ILogger<ScheduledRefreshRunner> logger) : IScheduledRefreshRunner
{
    public async Task<ScheduledRefreshResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        logger.LogInformation("Scheduled refresh started at {StartedAt} (version {Version}).", clock.UtcNow, version);

        var summary = await orchestrator.RunScheduledRefreshAsync(progress: null, cancellationToken).ConfigureAwait(false);

        LogSummary(summary);

        // Unavailable/NotPublished/Closed are successful completions (spec §5.2) and already fold
        // into SourceOperationOutcomeKind.Success upstream — only a genuine Failure count here can
        // push this past Success (Part E "do not classify every non-Verified source state as a
        // failed process").
        var outcome = summary.Failed > 0 ? ScheduledRefreshOutcome.PartialFailure : ScheduledRefreshOutcome.Success;
        return new ScheduledRefreshResult(outcome, summary);
    }

    private void LogSummary(RefreshSummary summary)
    {
        var elapsed = summary.CompletedAt - summary.StartedAt;
        logger.LogInformation(
            "Scheduled refresh summary: {Attempted} attempted, {Succeeded} succeeded, {Failed} failed, {Cancelled} cancelled, elapsed {ElapsedMs}ms.",
            summary.OperationsAttempted, summary.Succeeded, summary.Failed, summary.Cancelled, elapsed.TotalMilliseconds);

        foreach (var failure in summary.Results.Where(r => r.Outcome == SourceOperationOutcomeKind.Failure))
        {
            logger.LogWarning(
                "Scheduled refresh source failure: source={SourceId} category={Category} date={RequestedDate} status={Status} detail={Detail}",
                failure.SourceId, failure.Category, failure.RequestedDate, failure.Status, failure.Detail);
        }

        var completionState = summary.Failed > 0 ? "partial failure" : "success";
        logger.LogInformation("Scheduled refresh completed: {CompletionState}.", completionState);
    }
}
