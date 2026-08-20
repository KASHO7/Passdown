using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class InMemoryCatalogProductRepository : ICatalogProductRepository
{
    private readonly Dictionary<string, CatalogProductRecord> _products = new();

    public Task<CatalogProductRecord?> GetAsync(CatalogProductKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_products.GetValueOrDefault(key.ToString()));

    public Task UpsertAsync(CatalogProductUpsert product, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _products[product.Key.ToString()] = new CatalogProductRecord(
            ProductKey: product.Key.ToString(),
            Category: product.Key.Category,
            DisplayName: product.DisplayName,
            LifecycleState: product.LifecycleState,
            FirstSeenAt: product.FirstSeenAt,
            LastSeenAt: product.LastSeenAt,
            LastVerifiedAt: product.LastVerifiedAt,
            ConsecutiveConfirmedAbsenceCount: product.ConsecutiveConfirmedAbsenceCount,
            ArchivedAt: product.ArchivedAt,
            ArchivedReason: product.ArchivedReason,
            CurrentPriceAmount: product.CurrentPriceAmount,
            CurrentDisplayPrice: product.CurrentDisplayPrice,
            ComparisonPriceAmount: product.ComparisonPriceAmount,
            PriceQualifier: product.PriceQualifier,
            DateSpecific: product.DateSpecific,
            ApplicableDate: product.ApplicableDate,
            LastEvidence: product.LastEvidence);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CatalogProductRecord>> GetByCategoryAsync(
        SourceCategory category, ProductLifecycleState? lifecycleState = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<CatalogProductRecord> query = _products.Values.Where(p => p.Category == category);
        if (lifecycleState is { } state)
        {
            query = query.Where(p => p.LifecycleState == state);
        }

        return Task.FromResult<IReadOnlyList<CatalogProductRecord>>(query.OrderBy(p => p.ProductKey).ToList());
    }
}
