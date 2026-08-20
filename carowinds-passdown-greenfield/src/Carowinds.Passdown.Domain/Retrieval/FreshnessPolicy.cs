namespace Carowinds.Passdown.Domain.Retrieval;

/// <summary>
/// The configurable due/not-due rule for one source category, per spec §6.1.
/// Cadences are supplied by configuration rather than scattered hard-coded
/// comparisons — this type is where that comparison logic lives, once.
/// </summary>
/// <remarks>
/// All timestamps must already be expressed in the same offset the cadence's
/// <see cref="FreshnessCadence.DailyAt"/> time-of-day refers to (America/New_York
/// per spec §6.6). Domain does not perform IANA timezone conversion itself —
/// callers in Infrastructure/Application supply already-converted instants.
/// </remarks>
public sealed record FreshnessPolicy
{
    public SourceCategory Category { get; }
    public FreshnessCadence Cadence { get; }
    public bool AllowManualRefresh { get; }

    public FreshnessPolicy(SourceCategory category, FreshnessCadence cadence, bool allowManualRefresh = true)
    {
        Category = category;
        Cadence = cadence ?? throw new ArgumentNullException(nameof(cadence));
        AllowManualRefresh = allowManualRefresh;
    }

    /// <summary>
    /// True when this source is due for refresh given the instant it last
    /// succeeded and the current instant. A source that has never succeeded
    /// (<paramref name="lastSuccessAt"/> is null) is always due. Takes the raw
    /// last-success instant rather than a full <see cref="SourceState"/> because
    /// that is the only fact due-ness depends on — callers that only have a
    /// persisted projection of scheduling state (not a live <see cref="SourceState"/>
    /// aggregate) can call this without reconstructing one.
    /// </summary>
    public bool IsDue(DateTimeOffset? lastSuccessAt, DateTimeOffset now)
    {
        if (lastSuccessAt is not { } lastSuccess)
            return true;

        return Cadence switch
        {
            FreshnessCadence.DailyAt daily => lastSuccess < MostRecentOccurrence(daily.TimeOfDay, now),
            FreshnessCadence.RollingInterval rolling => now - lastSuccess >= rolling.Interval,
            _ => throw new NotSupportedException($"Unsupported cadence type '{Cadence.GetType()}'."),
        };
    }

    /// <summary>The next instant this source becomes due, given a successful check at <paramref name="lastSuccessAt"/>.</summary>
    public DateTimeOffset ComputeNextDueAt(DateTimeOffset lastSuccessAt) =>
        Cadence switch
        {
            FreshnessCadence.DailyAt daily => NextOccurrenceAfter(daily.TimeOfDay, lastSuccessAt),
            FreshnessCadence.RollingInterval rolling => lastSuccessAt + rolling.Interval,
            _ => throw new NotSupportedException($"Unsupported cadence type '{Cadence.GetType()}'."),
        };

    private static DateTimeOffset MostRecentOccurrence(TimeOnly timeOfDay, DateTimeOffset now)
    {
        var todaysOccurrence = new DateTimeOffset(now.Date, now.Offset) + timeOfDay.ToTimeSpan();
        return todaysOccurrence <= now ? todaysOccurrence : todaysOccurrence.AddDays(-1);
    }

    private static DateTimeOffset NextOccurrenceAfter(TimeOnly timeOfDay, DateTimeOffset after)
    {
        var sameDayOccurrence = new DateTimeOffset(after.Date, after.Offset) + timeOfDay.ToTimeSpan();
        return sameDayOccurrence > after ? sameDayOccurrence : sameDayOccurrence.AddDays(1);
    }
}
