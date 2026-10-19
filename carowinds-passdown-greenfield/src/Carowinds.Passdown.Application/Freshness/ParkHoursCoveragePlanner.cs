namespace Carowinds.Passdown.Application.Freshness;

/// <summary>
/// Phase 13D Part J/K: the future Park Hours coverage window Deep Refresh maintains — wide enough to
/// make Operations Calendar month planning useful, without issuing one slow request per date beyond
/// what is actually needed. Preferred target per spec Part J: today through the end of next calendar
/// month (e.g. today Aug 27 covers through Sep 30). A pure function so the target is computed
/// identically everywhere it's needed (planning, coverage-truthfulness checks) — never duplicated.
/// </summary>
public static class ParkHoursCoveragePlanner
{
    /// <summary>
    /// Phase 22E.1: default forward horizon, in months, for published-schedule discovery. The official
    /// feed's month form was observed answering through exactly one year ahead (2026-10-17 → 2027-10-17).
    /// </summary>
    public const int DefaultDiscoveryMonths = 12;

    /// <summary>Upper bound on a configured discovery horizon (keeps background traffic bounded).</summary>
    public const int MaxDiscoveryMonths = 18;

    /// <summary>
    /// Phase 22E.1: the last date published-schedule discovery reaches — <paramref name="months"/>
    /// calendar months after <paramref name="today"/> (clamped to 1..<see cref="MaxDiscoveryMonths"/>).
    /// <see cref="TargetHorizon"/> stays the minimum required coverage that triggers the Check Online
    /// bootstrap; this wider horizon is reached by the daily maintenance run.
    /// </summary>
    public static DateOnly DiscoveryHorizon(DateOnly today, int months = DefaultDiscoveryMonths) =>
        today.AddMonths(Math.Clamp(months, 1, MaxDiscoveryMonths));

    /// <summary>The first day of every month from <paramref name="firstDate"/>'s through <paramref name="lastDate"/>'s, inclusive — one official month request each.</summary>
    public static IReadOnlyList<DateOnly> MonthsBetween(DateOnly firstDate, DateOnly lastDate)
    {
        var months = new List<DateOnly>();
        for (var month = new DateOnly(firstDate.Year, firstDate.Month, 1); month <= lastDate; month = month.AddMonths(1))
        {
            months.Add(month);
        }

        return months;
    }

    /// <summary>The last date the coverage window should include for <paramref name="today"/> — the last day of the calendar month after <paramref name="today"/>'s.</summary>
    public static DateOnly TargetHorizon(DateOnly today) =>
        new DateOnly(today.Year, today.Month, 1).AddMonths(2).AddDays(-1);

    /// <summary>Every date from <paramref name="today"/> through <see cref="TargetHorizon"/>, inclusive.</summary>
    public static IReadOnlyList<DateOnly> BuildCoverageWindow(DateOnly today)
    {
        var horizon = TargetHorizon(today);
        var dates = new List<DateOnly>();
        for (var date = today; date <= horizon; date = date.AddDays(1))
        {
            dates.Add(date);
        }

        return dates;
    }
}
