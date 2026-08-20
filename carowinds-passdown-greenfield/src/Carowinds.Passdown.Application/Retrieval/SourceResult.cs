using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Outcome of one <see cref="IOfficialDataSource{TPayload}.RetrieveAsync"/> call (spec §16's
/// suggested <c>SourceResult</c>). Generic in <typeparamref name="TPayload"/> so the contract
/// itself stays adapter-neutral — this type never hardcodes any one source's parsed shape —
/// while each concrete adapter still gets a compile-time-typed payload instead of an
/// <see cref="object"/> every caller would have to downcast.
///
/// <see cref="Evidence"/> alone is always authoritative for status/traceability (spec §5.3).
/// <see cref="Payload"/> is only ever populated for <see cref="RetrievalStatus.Verified"/> —
/// the factory methods enforce this so a <see cref="RetrievalStatus.Closed"/>,
/// <see cref="RetrievalStatus.NotPublished"/>, <see cref="RetrievalStatus.Unavailable"/>, or
/// <see cref="RetrievalStatus.Error"/> result can never carry a fabricated stand-in value
/// (spec §5.2's "never fabricate or silently substitute").
/// </summary>
public sealed record SourceResult<TPayload>
{
    public string SourceId { get; }

    public SourceEvidence Evidence { get; }

    public TPayload? Payload { get; }

    private SourceResult(string sourceId, SourceEvidence evidence, TPayload? payload)
    {
        SourceId = sourceId;
        Evidence = evidence;
        Payload = payload;
    }

    /// <summary>A confirmed result carrying its parsed value. <paramref name="evidence"/> must be <see cref="RetrievalStatus.Verified"/>.</summary>
    public static SourceResult<TPayload> Success(string sourceId, SourceEvidence evidence, TPayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(payload);
        if (evidence.Status != RetrievalStatus.Verified)
        {
            throw new ArgumentException(
                $"A payload requires {nameof(RetrievalStatus)}.{nameof(RetrievalStatus.Verified)} evidence; got '{evidence.Status}'.",
                nameof(evidence));
        }

        return new SourceResult<TPayload>(sourceId, evidence, payload);
    }

    /// <summary>A result with no confirmed value — closed, not yet published, unavailable, or a failed attempt.</summary>
    public static SourceResult<TPayload> WithoutPayload(string sourceId, SourceEvidence evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Status == RetrievalStatus.Verified)
        {
            throw new ArgumentException(
                $"{nameof(RetrievalStatus)}.{nameof(RetrievalStatus.Verified)} evidence must carry a payload; use {nameof(Success)} instead.",
                nameof(evidence));
        }

        return new SourceResult<TPayload>(sourceId, evidence, default);
    }
}
