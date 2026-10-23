using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Retrieval;
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

    public CatalogSourceRegistration(
        IOfficialDataSource<CatalogPayload> source,
        IProductReconciliationService reconciliationService,
        bool requiresBrowserSession = false)
        : base(source.SourceId, source.Category, requiresBrowserSession)
    {
        _source = source;
        _reconciliationService = reconciliationService;
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
            return SourceOperationResult.Success(_source.SourceId, _source.Category, requestedDate: null, result.Evidence.Status, result.Evidence.Detail);
        }

        return SourceOperationResult.Failure(_source.SourceId, _source.Category, requestedDate: null, result.Evidence.Status, result.Evidence.Detail);
    }
}
