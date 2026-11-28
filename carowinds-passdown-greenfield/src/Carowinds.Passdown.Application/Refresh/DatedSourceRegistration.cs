using System.Text.Json;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// <see cref="SourceRegistration"/> for a per-date source (park hours, dated admission, dated
/// Fast Lane, dated haunted/SCarowinds). Persists every completed operation immediately (spec
/// §6.5/§6.7, Part F "save immediately"): a successful completion — <see cref="RetrievalStatus.Verified"/>,
/// <see cref="RetrievalStatus.Closed"/>, <see cref="RetrievalStatus.NotPublished"/>, or
/// <see cref="RetrievalStatus.Unavailable"/>, per <see cref="RetrievalStatusExtensions.IsSuccessfulCompletion"/> —
/// becomes the new last-known-good value; an <see cref="RetrievalStatus.Error"/> only updates the
/// latest-attempt record, leaving any prior successful value untouched. Exception (pre-RC.15,
/// <see cref="VerifiedDatedPriceRetention"/>): for a dated pricing source, a payload-less
/// Unavailable/NotPublished answer after a verified payload for the same date updates only the
/// latest-attempt record, so post-closing refreshes keep that date's verified prices. The payload is
/// serialized to JSON for storage — <see cref="IDatedResultRepository"/>'s payload column is
/// deliberately opaque to persistence, sufficient only to round-trip whatever shape a later phase
/// reads back.
/// </summary>
public sealed class DatedSourceRegistration<TPayload> : SourceRegistration
{
    private readonly IOfficialDataSource<TPayload> _source;
    private readonly IDatedResultRepository _datedResultRepository;

    public DatedSourceRegistration(
        IOfficialDataSource<TPayload> source,
        IDatedResultRepository datedResultRepository,
        bool requiresBrowserSession = false)
        : base(source.SourceId, source.Category, requiresBrowserSession)
    {
        _source = source;
        _datedResultRepository = datedResultRepository;
    }

    public override SourceScope Scope => SourceScope.Dated;

    public override async Task<SourceOperationResult> ExecuteAsync(DateOnly? requestedDate, CancellationToken cancellationToken)
    {
        if (requestedDate is not { } date)
        {
            throw new ArgumentException("A dated source registration requires a requested date.", nameof(requestedDate));
        }

        var request = new RetrievalRequest(_source.SourceId, _source.Category, date);
        var result = await _source.RetrieveAsync(request, cancellationToken).ConfigureAwait(false);
        var key = new DatedResultKey(_source.Category, date);

        if (result.Evidence.Status.IsSuccessfulCompletion())
        {
            // Pre-RC.15: a payload-less "no longer offered" answer (the calendar drops today once its sales
            // window ends) must not erase prices already verified for this exact date. The stored row is
            // read only for that kind of answer from a dated pricing source, so other results cost nothing.
            if (VerifiedDatedPriceRetention.AppliesTo(_source.Category)
                && VerifiedDatedPriceRetention.IsPayloadlessNoOfferAnswer(result.Evidence.Status, result.Payload is not null))
            {
                var existing = await _datedResultRepository.GetAsync(key, cancellationToken).ConfigureAwait(false);
                if (VerifiedDatedPriceRetention.ShouldRetain(_source.Category, existing, result.Evidence.Status, result.Payload is not null))
                {
                    await _datedResultRepository.RecordNoOfferAttemptRetainingVerifiedAsync(key, result.Evidence, cancellationToken).ConfigureAwait(false);
                    return SourceOperationResult.Success(_source.SourceId, _source.Category, date, result.Evidence.Status, result.Evidence.Detail);
                }
            }

            var payloadJson = result.Payload is null ? null : JsonSerializer.Serialize(result.Payload);
            await _datedResultRepository.RecordSuccessAsync(key, result.Evidence, payloadJson, cancellationToken).ConfigureAwait(false);
            return SourceOperationResult.Success(_source.SourceId, _source.Category, date, result.Evidence.Status, result.Evidence.Detail);
        }

        await _datedResultRepository.RecordFailedAttemptAsync(key, result.Evidence, cancellationToken).ConfigureAwait(false);
        return SourceOperationResult.Failure(_source.SourceId, _source.Category, date, result.Evidence.Status, result.Evidence.Detail);
    }
}
