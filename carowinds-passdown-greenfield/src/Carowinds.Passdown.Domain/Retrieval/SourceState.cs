namespace Carowinds.Passdown.Domain.Retrieval;

/// <summary>
/// Scheduling state for one source/category, per spec §6.2: last attempt, last
/// success, next due time, source fingerprint, and failure count. Distinct from
/// <see cref="LastKnownGood{TValue}"/>, which tracks a retrieved value's own history —
/// this tracks when the source itself was last checked and when it is due again.
/// </summary>
public sealed record SourceState
{
    public SourceCategory Category { get; }
    public DateTimeOffset? LastAttemptAt { get; }
    public DateTimeOffset? LastSuccessAt { get; }
    public DateTimeOffset? NextDueAt { get; }
    public string? SourceFingerprint { get; }
    public int ConsecutiveFailureCount { get; }

    private SourceState(
        SourceCategory category,
        DateTimeOffset? lastAttemptAt,
        DateTimeOffset? lastSuccessAt,
        DateTimeOffset? nextDueAt,
        string? sourceFingerprint,
        int consecutiveFailureCount)
    {
        Category = category;
        LastAttemptAt = lastAttemptAt;
        LastSuccessAt = lastSuccessAt;
        NextDueAt = nextDueAt;
        SourceFingerprint = sourceFingerprint;
        ConsecutiveFailureCount = consecutiveFailureCount;
    }

    public static SourceState Initial(SourceCategory category) => new(category, null, null, null, null, 0);

    /// <summary>Records a successful attempt: resets the failure count and updates last-success.</summary>
    public SourceState RecordSuccess(DateTimeOffset attemptAt, DateTimeOffset? nextDueAt, string? fingerprint = null) =>
        new(Category, attemptAt, attemptAt, nextDueAt, fingerprint ?? SourceFingerprint, 0);

    /// <summary>Records a failed attempt: last-success is left untouched (spec §6.7).</summary>
    public SourceState RecordFailure(DateTimeOffset attemptAt, DateTimeOffset? nextDueAt) =>
        new(Category, attemptAt, LastSuccessAt, nextDueAt, SourceFingerprint, ConsecutiveFailureCount + 1);
}
