using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// Persisted state for one exact <see cref="DatedResultKey"/> (spec §6.2), holding
/// two independent evidence/value snapshots — mirroring
/// <c>Carowinds.Passdown.Domain.Retrieval.LastKnownGood{T}</c> — so a failed attempt
/// is never allowed to overwrite a previously verified value (spec §6.7).
/// <paramref name="SuccessPayloadJson"/>'s shape is opaque to persistence; it is
/// sufficient to round-trip whatever typed dated data a later phase introduces.
/// </summary>
/// <param name="SuccessEvidence">Evidence for the most recent successful completion, if any.</param>
/// <param name="SuccessPayloadJson">The value recorded alongside <paramref name="SuccessEvidence"/>, if any.</param>
/// <param name="LastAttemptEvidence">Evidence for the most recent attempt of any outcome. Always present once a row exists.</param>
public sealed record DatedResultRecord(
    DatedResultKey Key,
    SourceEvidence? SuccessEvidence,
    string? SuccessPayloadJson,
    SourceEvidence LastAttemptEvidence);

public interface IDatedResultRepository
{
    Task<DatedResultRecord?> GetAsync(DatedResultKey key, CancellationToken cancellationToken = default);

    /// <summary>Cached results for one requested date, across every category with a cached result. Useful for later rolling-window orchestration (spec §6.5).</summary>
    Task<IReadOnlyList<DatedResultRecord>> GetByRequestedDateAsync(DateOnly requestedDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful completion: <paramref name="evidence"/> and
    /// <paramref name="payloadJson"/> become both the new last-known-good value and
    /// the latest attempt. <paramref name="evidence"/> must be a successful
    /// completion (spec §5.2); the requested date must equal <paramref name="key"/>'s date.
    /// </summary>
    Task RecordSuccessAsync(DatedResultKey key, SourceEvidence evidence, string? payloadJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed attempt: only the latest-attempt evidence is updated. Any
    /// prior successful value is left untouched (spec §6.7). <paramref name="evidence"/>
    /// must not be a successful completion; the requested date must equal
    /// <paramref name="key"/>'s date.
    /// </summary>
    Task RecordFailedAttemptAsync(DatedResultKey key, SourceEvidence evidence, CancellationToken cancellationToken = default);
}
