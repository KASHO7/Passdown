using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Catalog;

/// <summary>One tracked season product the Price History screen can show — active or retired.</summary>
/// <param name="ProductKey">The stable <see cref="CatalogProductKey"/> text (official id &gt; URL &gt; source+category+name).</param>
/// <param name="DisplayName">The current official name; a rename never breaks history because history is keyed by <paramref name="ProductKey"/>.</param>
/// <param name="IsActive">False once the product is retired (archived/expired) from the current catalog.</param>
public sealed record PriceHistoryProduct(
    string ProductKey,
    string DisplayName,
    PriceHistoryFamily Family,
    bool IsActive,
    int StateCount)
{
    /// <summary>Phase 25: the unit every price in this product's history is quoted in ("$7.00/month" for a Membership).</summary>
    public PassPriceUnit Unit => PriceHistoryTrackingPolicy.UnitOf(Family);
}

/// <summary>One interval plus its derived difference from the interval before it (never stored).</summary>
/// <param name="ChangeFromPrevious"><see langword="null"/> for the first (baseline) interval.</param>
public sealed record PriceHistoryEntry(SeasonPriceHistoryInterval Interval, SeasonPriceChange? ChangeFromPrevious)
{
    /// <summary>
    /// The same state as the previous interval — only possible when the product was retired and then
    /// published again at the same price (a new availability interval, not a price change).
    /// </summary>
    public bool IsReappearanceAtSamePrice => ChangeFromPrevious is { AmountChanged: false, KindChanged: false };
}

public interface ISeasonPriceHistoryService
{
    /// <summary>Every tracked Season Pass, renewal offer, Membership, initiation fee and Season Pass Add-On, including retired ones, by family then name.</summary>
    Task<IReadOnlyList<PriceHistoryProduct>> ListTrackedProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>One product's intervals, oldest first, each with its change from the previous interval.</summary>
    Task<IReadOnlyList<PriceHistoryEntry>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Phase 20A read side of season price history. Local-only: reads <see cref="ICatalogProductRepository"/>
/// and <see cref="ISeasonPriceHistoryRepository"/>, never retrieves anything online.
/// </summary>
public sealed class SeasonPriceHistoryService(
    ICatalogProductRepository catalogRepository,
    ISeasonPriceHistoryRepository historyRepository) : ISeasonPriceHistoryService
{
    public async Task<IReadOnlyList<PriceHistoryProduct>> ListTrackedProductsAsync(CancellationToken cancellationToken = default)
    {
        var trackedKeys = await historyRepository.GetTrackedProductKeysAsync(cancellationToken).ConfigureAwait(false);
        var products = new List<PriceHistoryProduct>();

        foreach (var category in PriceHistoryTrackingPolicy.CandidateCategories)
        {
            var records = await catalogRepository.GetByCategoryAsync(category, lifecycleState: null, cancellationToken).ConfigureAwait(false);
            foreach (var record in records)
            {
                var hasHistory = trackedKeys.Contains(record.ProductKey);
                var family = PriceHistoryTrackingPolicy.ClassifyStoredProduct(
                    record.Category, record.LastEvidence.SourceName, record.DisplayName, record.DateSpecific, hasHistory, record.ProductKey);
                if (family is not { } trackedFamily)
                {
                    continue;
                }

                // Phase 25: an option the source lists as unavailable ("Membership — Not Available") has no
                // price to show; it appears here only once a real price was ever recorded for it.
                if (!hasHistory && record.CurrentPriceAmount is null && record.Category == Domain.SourceCategory.SeasonPasses)
                {
                    continue;
                }

                var stateCount = hasHistory
                    ? (await historyRepository.GetHistoryAsync(record.ProductKey, cancellationToken).ConfigureAwait(false)).Count
                    : 0;
                var isActive = record.LifecycleState is ProductLifecycleState.Active or ProductLifecycleState.PendingConfirmation;
                products.Add(new PriceHistoryProduct(record.ProductKey, record.DisplayName, trackedFamily, isActive, stateCount));
            }
        }

        return products
            .OrderBy(p => p.Family)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.ProductKey, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<PriceHistoryEntry>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productKey);

        var intervals = await historyRepository.GetHistoryAsync(productKey, cancellationToken).ConfigureAwait(false);
        var entries = new List<PriceHistoryEntry>(intervals.Count);
        SeasonPriceHistoryInterval? previous = null;
        foreach (var interval in intervals.OrderBy(i => i.Sequence))
        {
            entries.Add(new PriceHistoryEntry(interval, previous is null ? null : SeasonPriceChange.Between(previous.State, interval.State)));
            previous = interval;
        }

        return entries;
    }
}
