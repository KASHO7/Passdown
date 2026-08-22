namespace Carowinds.Passdown.Application.Refresh;

/// <summary>The result of one <see cref="IScheduledRefreshRunner.RunAsync"/> call (Phase 6A Part D).</summary>
public sealed record ScheduledRefreshResult(ScheduledRefreshOutcome Outcome, RefreshSummary Summary);
