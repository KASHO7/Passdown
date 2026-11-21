using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// <see cref="SourceRegistration"/> for a catalog-scoped source (season passes, season-pass
/// add-ons, general add-ons, promotions). Reconciles through
/// <see cref="IProductReconciliationService"/> only on a <see cref="RetrievalStatus.Verified"/>
/// result (Part F "on Verified successful retrieval: reconcile"); any other outcome — including
/// <see cref="RetrievalStatus.Error"/> — retains the current catalog state exactly as-is and
/// performs no write at all, per Part F "do not reconcile as an empty catalog" and
/// <see cref="IProductReconciliationService.ReconcileAsync"/>'s own Verified-only guard.
/// </summary>
public sealed class CatalogSourceRegistration : SourceRegistration
{
    private readonly IOfficialDataSource<CatalogPayload> _source;
    private readonly IProductReconciliationService _reconciliationService;
    private readonly IOfficialReferenceDocumentRepository? _dealsDocuments;

    /// <param name="dealsDocuments">
    /// Phase 31: when supplied, a source that reports <see cref="CatalogPayload.Deals"/> also updates
    /// the deals reference document (merged into its last-known-good copy), and a failed check records
    /// its error there without touching the verified copy. Optional so every other catalog source is
    /// unchanged.
    /// </param>
    public CatalogSourceRegistration(
        IOfficialDataSource<CatalogPayload> source,
        IProductReconciliationService reconciliationService,
        bool requiresBrowserSession = false,
        IOfficialReferenceDocumentRepository? dealsDocuments = null)
        : base(source.SourceId, source.Category, requiresBrowserSession)
    {
        _source = source;
        _reconciliationService = reconciliationService;
        _dealsDocuments = dealsDocuments;
    }

    public override SourceScope Scope => SourceScope.Catalog;

    public override async Task<SourceOperationResult> ExecuteAsync(DateOnly? requestedDate, CancellationToken cancellationToken)
    {
        if (requestedDate is not null)
        {
            throw new ArgumentException("A catalog source registration does not take a requested date.", nameof(requestedDate));
        }

        var request = new RetrievalRequest(_source.SourceId, _source.Category);
        var result = await _source.RetrieveAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.Evidence.Status == RetrievalStatus.Verified)
        {
            await _reconciliationService.ReconcileAsync(
                    _source.Category, result.Evidence, result.Payload!.Products, cancellationToken, result.Payload.UnconfirmedProductKeys)
                .ConfigureAwait(false);
            await SaveDealsAsync(result.Evidence, result.Payload.Deals, cancellationToken).ConfigureAwait(false);
            return SourceOperationResult.Success(_source.SourceId, _source.Category, requestedDate: null, result.Evidence.Status, result.Evidence.Detail);
        }

        if (_dealsDocuments is not null)
        {
            await _dealsDocuments.SaveAttemptAsync(OfficialReferenceDocumentIds.Deals, result.Evidence, cancellationToken).ConfigureAwait(false);
        }

        return SourceOperationResult.Failure(_source.SourceId, _source.Category, requestedDate: null, result.Evidence.Status, result.Evidence.Detail);
    }

    private async Task SaveDealsAsync(Domain.Retrieval.SourceEvidence evidence, OfficialDealsObservation? observation, CancellationToken cancellationToken)
    {
        if (_dealsDocuments is null || observation is null)
        {
            return;
        }

        var previousRecord = await _dealsDocuments.GetAsync(OfficialReferenceDocumentIds.Deals, cancellationToken).ConfigureAwait(false);
        var previous = OfficialSeasonJson.TryDeserialize<OfficialDealsDocument>(previousRecord?.SuccessPayloadJson) ?? OfficialDealsDocument.Empty;
        var merged = previous.Merge(observation, evidence.TimeChecked);
        await _dealsDocuments.SaveSuccessAsync(OfficialReferenceDocumentIds.Deals, evidence, OfficialSeasonJson.Serialize(merged), cancellationToken).ConfigureAwait(false);
    }
}
