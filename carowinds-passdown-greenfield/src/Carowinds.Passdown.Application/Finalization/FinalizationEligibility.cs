using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.Finalization;

/// <summary>
/// Pure helper establishing the Phase 9 finalization-cutoff concept (spec Phase 9 Part A/Q) — NOT a
/// scheduler. Nothing in the app calls <see cref="EligibleForFinalizationAt"/> yet; Phase 9B owns
/// actually evaluating and enforcing this cutoff. Kept here now only so the concept has one
/// authoritative, tested definition before 9B needs it, per spec Part Q's "perhaps as a pure
/// helper/test if needed to establish the model."
/// </summary>
public static class FinalizationEligibility
{
    private static readonly TimeOnly CutoffLocalTime = new(6, 0);

    /// <summary>
    /// The first instant a <paramref name="passdownDate"/> passdown becomes eligible for automatic
    /// finalization: 6:00 AM Eastern on the following calendar day (spec Phase 9 example: 2026-08-25
    /// -&gt; 2026-08-26 06:00 America/New_York). Computed via <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/>
    /// against the unspecified-kind wall-clock instant so the correct EST/EDT offset is applied for
    /// whichever side of a DST transition this particular cutoff falls on (matches
    /// <see cref="EasternTimeZone"/>'s own "never assume a fixed UTC offset" rule).
    /// </summary>
    public static DateTimeOffset EligibleForFinalizationAt(DateOnly passdownDate)
    {
        var cutoffLocalDate = passdownDate.AddDays(1);
        var cutoffWallClock = cutoffLocalDate.ToDateTime(CutoffLocalTime, DateTimeKind.Unspecified);
        var offset = EasternTimeZone.Instance.GetUtcOffset(cutoffWallClock);
        return new DateTimeOffset(cutoffWallClock, offset);
    }
}
