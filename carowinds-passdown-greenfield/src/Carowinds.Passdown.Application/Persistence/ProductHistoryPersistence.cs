using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// One traceability snapshot of a catalog product at a point in time (spec §7.2/§7.3).
/// <see cref="ProductKey"/> is the same raw stored identity text used by
/// <see cref="CatalogProductRecord"/>. Append-only: nothing in this project updates
/// or deletes a history row.
/// </summary>
public sealed record ProductHistoryEntry(
    string ProductKey,
    DateTimeOffset RecordedAt,
    string DisplayName,
    ProductLifecycleState LifecycleState,
    decimal? CurrentPriceAmount,
    string? CurrentDisplayPrice,
    decimal? ComparisonPriceAmount,
    string? PriceQualifier,
    string? ChangeReason);

public interface IProductHistoryRepository
{
    /// <summary>
    /// Appends one snapshot. Pass <paramref name="unitOfWork"/> to commit this
    /// write atomically together with a related catalog-row upsert.
    /// </summary>
    Task AppendAsync(ProductHistoryEntry entry, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default);

    /// <summary>History for one product key, oldest first.</summary>
    Task<IReadOnlyList<ProductHistoryEntry>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default);
}
