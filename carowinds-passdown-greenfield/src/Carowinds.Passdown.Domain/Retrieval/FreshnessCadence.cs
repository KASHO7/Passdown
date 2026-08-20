namespace Carowinds.Passdown.Domain.Retrieval;

/// <summary>
/// How often a source category should be refreshed, per spec §6.1. The concrete
/// cadence values (times, intervals) are configuration supplied by the caller —
/// Domain only shapes what a cadence can be and how it evaluates due-ness.
/// </summary>
public abstract record FreshnessCadence
{
    private FreshnessCadence()
    {
    }

    /// <summary>Due once per day at a fixed time, e.g. daily at 6:00 AM Eastern.</summary>
    public sealed record DailyAt(TimeOnly TimeOfDay) : FreshnessCadence;

    /// <summary>Due after a fixed interval has elapsed since the last success, e.g. every 7 days.</summary>
    public sealed record RollingInterval(TimeSpan Interval) : FreshnessCadence;
}
