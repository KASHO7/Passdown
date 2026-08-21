namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// Application-layer refresh orchestration (Phase 3G): decides what is due, runs due sources
/// efficiently, and reports what happened. Never triggered by a selected-date change (spec §6.3)
/// — callers decide when to invoke this, this interface has no notion of "currently selected
/// date" at all.
/// </summary>
public interface IRefreshOrchestrator
{
    /// <summary>
    /// Runs the due-sources rolling-window job (spec §6.5/§6.6): every registered source is
    /// checked for due-ness via <see cref="Freshness.IFreshnessPlanner"/> first, and a source that
    /// is not due is skipped entirely — including every date it would otherwise have covered.
    /// </summary>
    Task<RefreshSummary> RunScheduledRefreshAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs "Check this date online" (spec §6.3) for exactly one date: every registered dated
    /// source is checked for <paramref name="date"/> unconditionally, regardless of due-ness, and
    /// no catalog source is touched. Never updates scheduled-cadence source state (Part C) —
    /// running this must never change when the next scheduled rolling-window job considers a
    /// source due.
    /// </summary>
    Task<RefreshSummary> RunManualDateRefreshAsync(DateOnly date, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default);
}
