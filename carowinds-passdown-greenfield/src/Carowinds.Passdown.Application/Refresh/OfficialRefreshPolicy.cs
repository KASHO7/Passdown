using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// Phase 19A: the one authoritative home of the interactive app's automatic official-refresh timing
/// (see docs/phase19a-automatic-refresh.md). Every decision here is a pure function of persisted
/// state plus "now" (UTC instants, so DST changes and the PC sleeping cannot distort an elapsed
/// interval), which is what makes it deterministic to test without waiting real hours.
/// </summary>
public static class OfficialRefreshPolicy
{
    /// <summary>The fixed product interval between automatic comprehensive checks while the app stays open. Not a user setting.</summary>
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(4);

    /// <summary>
    /// How recently a fully successful comprehensive check (by the scheduled task, a manual Deep
    /// Refresh, or a previous app session) must have happened for interactive startup to skip its own
    /// check. Deliberately much shorter than <see cref="AutomaticInterval"/>: opening the app should
    /// normally freshen official data, except when that was just done.
    /// </summary>
    public static readonly TimeSpan StartupFreshnessGrace = TimeSpan.FromMinutes(60);

    /// <summary>
    /// After an automatic date check of one Associate date completes (whatever its outcome), that date
    /// is not checked automatically again for this long — so flipping back and forth between dates while
    /// offline cannot turn navigation into a request loop. Check Online ignores this.
    /// </summary>
    public static readonly TimeSpan DateCheckCooldown = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The longest the background loop ever sleeps before comparing the wall clock again. Bounds how late
    /// a due check can start after the PC wakes from sleep, and notices the Eastern day rolling over —
    /// an in-memory comparison only, never a database poll.
    /// </summary>
    public static readonly TimeSpan MaxSleepSlice = TimeSpan.FromMinutes(5);

    /// <summary>A persisted check timestamp further in the future than this is treated as untrustworthy (the system clock moved backward), so it can never postpone checks indefinitely.</summary>
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When the next automatic comprehensive check is due: four hours after the most recent
    /// qualifying comprehensive check (one that attempted every official source — this app's automatic
    /// check, the scheduled task, or a manual Deep Refresh), whether or not every source succeeded, so a
    /// failure (e.g. offline) never turns into a rapid retry loop. <see langword="null"/> means "due now".
    /// </summary>
    public static DateTimeOffset? NextAutomaticCheckAt(OfficialDataStatus status, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.LastComprehensiveCheckAt is not { } last || last > nowUtc + ClockSkewTolerance)
        {
            return null;
        }

        return last + AutomaticInterval;
    }

    public static bool IsAutomaticCheckDue(OfficialDataStatus status, DateTimeOffset nowUtc) =>
        NextAutomaticCheckAt(status, nowUtc) is not { } next || nowUtc >= next;

    /// <summary>
    /// Startup duplicate-work protection: true when every official source was already attempted within
    /// <see cref="StartupFreshnessGrace"/> — e.g. the 6 AM scheduled task finished at 7:58 and the app
    /// opened at 8:00, or startup maintenance just checked everything. This holds whatever the outcome
    /// of that attempt: a source that failed minutes ago is not retried by repeating every request (the
    /// status line shows the failure; Check Online / Deep Refresh retry on demand, and the four-hour
    /// policy tries again).
    /// </summary>
    public static bool IsFreshEnoughToSkipStartupCheck(OfficialDataStatus status, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.LastComprehensiveCheckAt is not { } lastComprehensive)
        {
            return false;
        }

        var age = nowUtc - lastComprehensive;
        return age < StartupFreshnessGrace && age > -ClockSkewTolerance;
    }

    /// <summary>
    /// Whether interactive startup must also run the missed daily scheduled maintenance (spec §6.6
    /// catch-up): no Deep Refresh has been attempted since the most recent daily refresh boundary
    /// (6:00 AM Eastern, the same cadence <see cref="FreshnessPolicy"/> already applies to every
    /// source), or future Park Hours coverage has fallen behind (<see cref="DeepRefreshCoverage"/>).
    /// When the scheduled task already ran today this is false, so startup never repeats its whole
    /// Park Hours coverage sweep.
    /// </summary>
    public static bool IsDailyMaintenanceDue(DeepRefreshStatusRecord? deepRefreshStatus, FreshnessPolicy dailyPolicy, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(dailyPolicy);
        if (deepRefreshStatus is null)
        {
            return true;
        }

        var nowEastern = EasternTimeZone.ConvertFromUtc(nowUtc);
        var today = DateOnly.FromDateTime(nowEastern.DateTime);
        return dailyPolicy.IsDue(deepRefreshStatus.LastAttemptAtUtc, nowEastern)
            || DeepRefreshCoverage.NeedsDeepRefresh(deepRefreshStatus, today);
    }
}
