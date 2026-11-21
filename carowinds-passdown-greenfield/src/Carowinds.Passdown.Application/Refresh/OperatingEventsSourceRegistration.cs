using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Domain.Seasons;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// Phase 31: refresh registration for the official event schedule. Runs on the existing orchestrator
/// as a catalog-scoped (date-independent) source on the same daily cadence — no new polling engine,
/// and never on selected-date navigation. A verified check is merged into the last-known-good
/// schedule (occurrences no longer listed are kept, never treated as cancelled); any other outcome
/// records only the attempt and its error.
/// </summary>
public sealed class OperatingEventsSourceRegistration(
    IOfficialDataSource<OperatingEventsPayload> source,
    IOfficialReferenceDocumentRepository documents)
    : SourceRegistration(source.SourceId, source.Category, requiresBrowserSession: false)
{
    public override SourceScope Scope => SourceScope.Catalog;

    public override async Task<SourceOperationResult> ExecuteAsync(DateOnly? requestedDate, CancellationToken cancellationToken)
    {
        if (requestedDate is not null)
        {
            throw new ArgumentException("The event schedule registration does not take a requested date.", nameof(requestedDate));
        }

        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), cancellationToken).ConfigureAwait(false);

        if (result.Evidence.Status == RetrievalStatus.Verified && result.Payload is { } payload)
        {
            var previousRecord = await documents.GetAsync(OfficialReferenceDocumentIds.OperatingEvents, cancellationToken).ConfigureAwait(false);
            var previous = OfficialSeasonJson.TryDeserialize<OperatingEventSchedule>(previousRecord?.SuccessPayloadJson) ?? OperatingEventSchedule.Empty;
            var merged = previous.Merge(payload.Events);
            await documents.SaveSuccessAsync(OfficialReferenceDocumentIds.OperatingEvents, result.Evidence, OfficialSeasonJson.Serialize(merged), cancellationToken).ConfigureAwait(false);
            return SourceOperationResult.Success(source.SourceId, source.Category, requestedDate: null, result.Evidence.Status, result.Evidence.Detail);
        }

        await documents.SaveAttemptAsync(OfficialReferenceDocumentIds.OperatingEvents, result.Evidence, cancellationToken).ConfigureAwait(false);
        return SourceOperationResult.Failure(source.SourceId, source.Category, requestedDate: null, result.Evidence.Status, result.Evidence.Detail);
    }
}
