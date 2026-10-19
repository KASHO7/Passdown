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

    /// <summary>
    /// Phase 22E.1: true when stored coverage has not yet reached the published-schedule discovery
    /// horizon (<see cref="ParkHoursCoveragePlanner.DiscoveryHorizon"/>). Deliberately separate from
    /// <see cref="NeedsDeepRefresh"/>: a short official feed must never turn every Check Online into a
    /// bootstrap Deep Refresh — this only makes the (rate-limited) daily maintenance run again.
    /// </summary>
    public static bool NeedsScheduleDiscovery(DeepRefreshStatusRecord? status, DateOnly today, int months = ParkHoursCoveragePlanner.DefaultDiscoveryMonths)
    {
        if (status?.CoverageThrough is not { } coverageThrough)
        {
            return true;
        }

        var horizon = ParkHoursCoveragePlanner.DiscoveryHorizon(today, months);
        return coverageThrough < horizon.AddDays(-CoverageSlackDays);
    }
}
