namespace Carowinds.Passdown.Domain.Retrieval;

/// <summary>
/// Last-known-good semantics for a single retrieved value, per spec §6.7:
/// a failed attempt must never overwrite a previously verified value, and the
/// latest failed attempt is preserved separately from the last successful one.
/// Reusable for any dated or catalog value type recorded in later phases.
/// </summary>
public sealed record LastKnownGood<TValue>
{
    /// <summary>The most recently successfully retrieved value, if any.</summary>
    public TValue? SuccessValue { get; }

    /// <summary>Evidence for the most recent successful completion, if any.</summary>
    public SourceEvidence? SuccessEvidence { get; }

    /// <summary>Evidence for the most recent attempt of any outcome, if any.</summary>
    public SourceEvidence? LastAttemptEvidence { get; }

    private LastKnownGood(TValue? successValue, SourceEvidence? successEvidence, SourceEvidence? lastAttemptEvidence)
    {
        SuccessValue = successValue;
        SuccessEvidence = successEvidence;
        LastAttemptEvidence = lastAttemptEvidence;
    }

    public static LastKnownGood<TValue> Empty { get; } = new(default, null, null);

    /// <summary>
    /// Records a new attempt. A successful completion replaces the last-known-good
    /// value; a failure updates only the latest-attempt record, leaving any prior
    /// successful value untouched (spec §6.7).
    /// </summary>
    public LastKnownGood<TValue> RecordAttempt(SourceEvidence evidence, TValue? value)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return evidence.Status.IsSuccessfulCompletion()
            ? new LastKnownGood<TValue>(value, evidence, evidence)
            : new LastKnownGood<TValue>(SuccessValue, SuccessEvidence, evidence);
    }

    public bool HasSuccess => SuccessEvidence is not null;

    /// <summary>
    /// True when the value on hand is not from the most recent attempt — i.e. the
    /// latest check failed and only older last-known-good data is available.
    /// </summary>
    public bool IsStale => HasSuccess && LastAttemptEvidence is not null && LastAttemptEvidence != SuccessEvidence;

    /// <summary>
    /// The status to present: <see cref="RetrievalStatus.Stale"/> when relying on
    /// older data after a later failure, the underlying status otherwise, or null
    /// when nothing has ever been attempted ("Not checked yet", per spec §6.3).
    /// </summary>
    public RetrievalStatus? EffectiveStatus =>
        IsStale ? RetrievalStatus.Stale : SuccessEvidence?.Status ?? LastAttemptEvidence?.Status;

    /// <summary>Age of the current last-known-good value as of <paramref name="asOf"/>, if any.</summary>
    public TimeSpan? Age(DateTimeOffset asOf) =>
        SuccessEvidence is null ? null : asOf - SuccessEvidence.TimeChecked;
}
