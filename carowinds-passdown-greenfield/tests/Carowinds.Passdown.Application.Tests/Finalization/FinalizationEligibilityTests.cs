using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.Tests.Finalization;

/// <summary>
/// Covers only the pure cutoff-instant helper (spec Phase 9 Part Q) — no scheduler exists yet to
/// evaluate it against "now"; that is Phase 9B's job.
/// </summary>
public class FinalizationEligibilityTests
{
    [Fact]
    public void EligibleForFinalizationAt_IsTheFollowingCalendarDayAtSixAm()
    {
        var passdownDate = new DateOnly(2026, 8, 25);

        var eligibleAt = FinalizationEligibility.EligibleForFinalizationAt(passdownDate);

        var eastern = EasternTimeZone.ConvertFromUtc(eligibleAt);
        Assert.Equal(new DateOnly(2026, 8, 26), DateOnly.FromDateTime(eastern.DateTime));
        Assert.Equal(new TimeOnly(6, 0), TimeOnly.FromDateTime(eastern.DateTime));
    }

    [Fact]
    public void EligibleForFinalizationAt_DuringEasternDaylightTime_UsesEdtOffset()
    {
        // 2026-08-26 is within EDT (UTC-4), not EST (UTC-5).
        var eligibleAt = FinalizationEligibility.EligibleForFinalizationAt(new DateOnly(2026, 8, 25));

        Assert.Equal(TimeSpan.FromHours(-4), eligibleAt.Offset);
    }

    [Fact]
    public void EligibleForFinalizationAt_DuringEasternStandardTime_UsesEstOffset()
    {
        // A December passdown date's following-day cutoff falls under EST (UTC-5).
        var eligibleAt = FinalizationEligibility.EligibleForFinalizationAt(new DateOnly(2026, 12, 24));

        Assert.Equal(TimeSpan.FromHours(-5), eligibleAt.Offset);
    }

    [Fact]
    public void EligibleForFinalizationAt_IsDeterministicForTheSameDate()
    {
        var date = new DateOnly(2026, 8, 25);

        var first = FinalizationEligibility.EligibleForFinalizationAt(date);
        var second = FinalizationEligibility.EligibleForFinalizationAt(date);

        Assert.Equal(first, second);
    }
}
