namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// Phase 13B Part A: the explicit refresh-intent abstraction callers use instead of a scattered
/// <c>force: bool</c>. Every refresh in the system is triggered for exactly one of these reasons;
/// <see cref="IScheduledRefreshRunner.RunAsync"/> takes this purely to make its own logging say
/// which one actually triggered a given run — <see cref="Scheduled"/> and <see cref="StartupCatchUp"/>
/// still resolve to the identical due-based <see cref="IRefreshOrchestrator.RunScheduledRefreshAsync"/>
/// call (Phase 13A confirmed that sharing is already correct, not a gap to fix), while
/// <see cref="UserForced"/> never goes through the runner at all — it is a distinct
/// <see cref="IRefreshOrchestrator.RunUserForcedRefreshAsync"/> entry point reached only from an
/// explicit "Check Online" action.
/// </summary>
public enum RefreshMode
{
    /// <summary>The unattended <c>--scheduled-refresh</c> process, normally triggered by the Windows Task Scheduler at 6 AM.</summary>
    Scheduled,

    /// <summary>The best-effort background catch-up fired once at normal interactive startup (spec §6.6) — same due-based work as <see cref="Scheduled"/>, different trigger.</summary>
    StartupCatchUp,

    /// <summary>An explicit user action ("Check Online" on the Dashboard or Associate screen) that bypasses freshness entirely for every currently supported source family.</summary>
    UserForced,
}
