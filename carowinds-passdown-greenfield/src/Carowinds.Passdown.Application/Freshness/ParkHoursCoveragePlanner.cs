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
