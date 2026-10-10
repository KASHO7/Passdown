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
    /// Runs an explicit "Check Online" (spec §6.3, Phase 13B Part B — <see cref="RefreshMode.UserForced"/>):
    /// every registered dated source is checked for <paramref name="date"/> unconditionally, and
    /// every registered catalog source (Season Passes, Season Pass/General Add-Ons, Promotions) is
    /// checked once, also unconditionally — regardless of due-ness. Freshness never prevents any
    /// source from executing here, unlike <see cref="RunScheduledRefreshAsync"/>. Never updates
    /// scheduled-cadence source state (Part C/G) — running this must never change when the next
    /// scheduled rolling-window job considers a source due, and every source's freshness timestamps
    /// keep updating through the normal per-operation persistence path regardless.
    /// </summary>
    Task<RefreshSummary> RunUserForcedRefreshAsync(DateOnly date, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 13D Part L/O/Q: the one authoritative Deep Refresh operation, shared by scheduled/
    /// startup-catch-up maintenance and the manual "Run Deep Refresh" button (never a second,
    /// duplicated implementation). Always refreshes Park Hours across the full future coverage
    /// window (<see cref="Freshness.ParkHoursCoveragePlanner"/>), unconditionally — this is the one
    /// source family Deep Refresh exists to widen beyond the normal rolling window. Every other
    /// dated source runs over the normal rolling window, and every catalog source runs once;
    /// <paramref name="bypassFreshness"/> controls only whether that other work is due-gated
    /// (<see langword="false"/> — scheduled/startup-catch-up nightly maintenance, preserving normal
    /// cost/cadence) or fully unconditional (<see langword="true"/> — the manual button, and the
    /// first/insufficient-coverage "Check Online" bootstrap, spec Part M). <paramref name="focusDate"/>,
    /// when supplied, guarantees every non-Park-Hours dated source also covers that specific date even
    /// if it falls outside the normal rolling window — used by the insufficient-coverage Check Online
    /// bootstrap so the date the associate actually asked about is never left out. Updates
    /// scheduled-cadence source state for every source it touches (unlike <see cref="RunUserForcedRefreshAsync"/>)
    /// — a Deep Refresh genuinely is a fresh check.
    /// </summary>
    Task<RefreshSummary> RunDeepRefreshAsync(
        bool bypassFreshness,
        DateOnly? focusDate = null,
        IProgress<RefreshProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
