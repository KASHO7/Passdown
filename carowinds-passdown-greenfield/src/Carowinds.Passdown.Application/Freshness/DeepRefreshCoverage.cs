using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Freshness;

/// <summary>
/// Phase 13D Part L: <c>NeedsDeepRefresh</c>, computed purely from local persisted state (the
/// <see cref="DeepRefreshStatusRecord"/> a prior Deep Refresh left behind) — never from "has this
/// user ever clicked Check Online." True when no Deep Refresh has ever completed, or when the
/// persisted <see cref="DeepRefreshStatusRecord.CoverageThrough"/> has fallen behind the target
/// horizon by more than a small slack — a one-day short-fall (e.g. a single date's fetch failed on
/// the last run) should not force every subsequent Check Online back into bootstrap mode.
/// </summary>
public static class DeepRefreshCoverage
{
    /// <summary>How far <see cref="DeepRefreshStatusRecord.CoverageThrough"/> may trail the target horizon before coverage counts as insufficient.</summary>
    private const int CoverageSlackDays = 3;

    public static bool NeedsDeepRefresh(DeepRefreshStatusRecord? status, DateOnly today)
    {
        if (status?.CoverageThrough is not { } coverageThrough)
        {
            return true;
        }

        var horizon = ParkHoursCoveragePlanner.TargetHorizon(today);
        return coverageThrough < horizon.AddDays(-CoverageSlackDays);
    }
}
