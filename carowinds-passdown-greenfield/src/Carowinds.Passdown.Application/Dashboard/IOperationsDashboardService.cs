using Carowinds.Passdown.Application.Refresh;

namespace Carowinds.Passdown.Application.Dashboard;

/// <summary>
/// <see cref="OperationsDashboardSnapshot"/> plus the raw job outcome behind an explicit Check
/// Online action (Phase 10D Part L), mirroring <see cref="Associate.AssociateOnlineCheckResult"/> —
/// so a caller can render both the recomposed Dashboard and a plain-language refresh summary from
/// one round trip.
/// </summary>
public sealed record OperationsDashboardCheckOnlineResult(OperationsDashboardSnapshot Snapshot, RefreshSummary Summary);

/// <summary>
/// The Application-layer seam a supervisor's Operations Dashboard is built from (Phase 10A Part J):
/// composes <see cref="OperationsDashboardSnapshot"/> entirely from existing local
/// Associate/finalization state. Never performs HTTP/Playwright retrieval itself — opening the
/// dashboard must never trigger a network check (Phase 10A Part K).
/// </summary>
public interface IOperationsDashboardService
{
    /// <summary>
    /// Today's snapshot, where "today" is the current date in America/New_York (Phase 10A Part D) —
    /// deliberately independent of whatever date the Associate editor currently has selected.
    /// </summary>
    Task<OperationsDashboardSnapshot> GetTodayAsync(CancellationToken cancellationToken = default);

    /// <summary>The snapshot for an arbitrary operating date, for future callers/tests that need a date other than today.</summary>
    Task<OperationsDashboardSnapshot> LoadAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicit "Check Online" (Phase 10D Part L/M): reuses the exact same manual-date refresh
    /// workflow the Associate screen's own Check Online action calls — via the
    /// <see cref="Associate.IAssociatePassdownService.CheckAssociateDateOnlineAsync"/> dependency
    /// this service already has, scoped to today's own operating date (Part D). Never called except
    /// from an explicit user action; opening or reloading the Dashboard never calls this.
    /// </summary>
    Task<OperationsDashboardCheckOnlineResult> CheckOnlineAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default);
}
