using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// Phase 32: refresh registration for the official Carowinds Pass Perks. Runs on the existing orchestrator as
/// a catalog-scoped (date-independent) source on the daily cadence — startup catch-up, Check Online and the
/// scheduled refresh — with no polling engine of its own and never on date or month navigation. A verified
/// check is merged into the last-known-good document (history kept, removals only from a complete check);
/// any other outcome records only the attempt and its error, so a failed refresh never erases verified perks.
/// </summary>
public sealed class PassPerksSourceRegistration(
    IOfficialDataSource<PassPerksObservation> source,
    IOfficialReferenceDocumentRepository documents)
    : SourceRegistration(source.SourceId, source.Category, requiresBrowserSession: false)
{
    public override SourceScope Scope => SourceScope.Catalog;

    public override async Task<SourceOperationResult> ExecuteAsync(DateOnly? requestedDate, CancellationToken cancellationToken)
    {
        if (requestedDate is not null)
        {
            throw new ArgumentException("The Pass Perks registration does not take a requested date.", nameof(requestedDate));
        }

        var result = await source.RetrieveAsync(new RetrievalRequest(source.SourceId, source.Category), cancellationToken).ConfigureAwait(false);

        if (result.Evidence.Status == RetrievalStatus.Verified && result.Payload is { Perks.Count: > 0 } observation)
        {
            var previousRecord = await documents.GetAsync(PassPerksDocumentIds.PassPerks, cancellationToken).ConfigureAwait(false);
            var previous = OfficialSeasonJson.TryDeserialize<PassPerksDocument>(previousRecord?.SuccessPayloadJson) ?? PassPerksDocument.Empty;
            var merged = previous.Merge(observation, result.Evidence.TimeChecked);
            await documents.SaveSuccessAsync(PassPerksDocumentIds.PassPerks, result.Evidence, OfficialSeasonJson.Serialize(merged), cancellationToken).ConfigureAwait(false);
            return SourceOperationResult.Success(source.SourceId, source.Category, requestedDate: null, result.Evidence.Status, result.Evidence.Detail);
        }

        await documents.SaveAttemptAsync(PassPerksDocumentIds.PassPerks, result.Evidence, cancellationToken).ConfigureAwait(false);
        return SourceOperationResult.Failure(source.SourceId, source.Category, requestedDate: null, result.Evidence.Status, result.Evidence.Detail);
    }
}
