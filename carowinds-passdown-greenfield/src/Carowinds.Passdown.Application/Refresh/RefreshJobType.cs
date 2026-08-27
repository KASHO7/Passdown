namespace Carowinds.Passdown.Application.Refresh;

/// <summary>Which refresh operation produced a <see cref="RefreshSummary"/> (Phase 3G Parts B/C; Phase 13B Part A).</summary>
public enum RefreshJobType
{
    /// <summary>The due-sources rolling-window job (spec §6.5/§6.6) — every source not yet due is skipped entirely. Shared by <see cref="RefreshMode.Scheduled"/> and <see cref="RefreshMode.StartupCatchUp"/>.</summary>
    ScheduledRollingWindow,

    /// <summary>
    /// A user-initiated "Check Online" (spec §6.3, Phase 13B Part B): every registered dated source
    /// runs for the requested date and every registered catalog source runs once, all unconditionally
    /// regardless of due-ness, and never touches scheduled-cadence source state. Produced only by
    /// <see cref="RefreshMode.UserForced"/>.
    /// </summary>
    UserForced,

    /// <summary>
    /// Phase 13D: the coverage-aware maintenance operation — always refreshes Park Hours across the
    /// full future coverage window (today through the end of next calendar month, unconditionally),
    /// plus every other dated/catalog source over the normal rolling window, either due-gated
    /// (scheduled/startup catch-up maintenance) or fully unconditional (the manual "Run Deep Refresh"
    /// button, and the first/insufficient-coverage "Check Online" bootstrap). Updates scheduled-
    /// cadence source state, unlike <see cref="UserForced"/> — a successful Deep Refresh genuinely is
    /// a fresh check for every source it touches.
    /// </summary>
    DeepRefresh,
}
