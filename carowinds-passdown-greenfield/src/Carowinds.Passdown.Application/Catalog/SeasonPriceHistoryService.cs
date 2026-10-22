using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
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

    /// <summary>
    /// Phase 26: the product's history is known on this computer only through Personal Sync — it is not
    /// in this computer's own catalog (for example, it was retired before this computer began checking).
    /// </summary>
    public bool IsImportedOnly { get; init; }

    /// <summary>Phase 26: the official page this computer last verified the product on, when known (validated before it is ever opened).</summary>
    public Uri? OfficialSourceUrl { get; init; }
}

/// <summary>One interval plus its derived difference from the interval before it (never stored).</summary>
/// <param name="ChangeFromPrevious"><see langword="null"/> for the first (baseline) interval.</param>
public sealed record PriceHistoryEntry(SeasonPriceHistoryInterval Interval, SeasonPriceChange? ChangeFromPrevious)
{
    /// <summary>
    /// Phase 26: whose official checks this row rests on. An imported row is still an official
    /// historical observation — it is never proof that this computer checked the website then.
    /// </summary>
    public PriceHistoryProvenance Provenance { get; init; } = PriceHistoryProvenance.CollectedHere;

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
/// and <see cref="ISeasonPriceHistoryRepository"/>, never retrieves anything online. Phase 26: when
/// <paramref name="syncedRepository"/> is supplied, observations merged in through Personal Sync are
/// combined with this computer's own intervals by <see cref="PriceHistoryTimelineMerge"/> — a read-time
/// view only; neither store is rewritten, and the current catalog price is never derived from it.
/// </summary>
public sealed class SeasonPriceHistoryService(
    ICatalogProductRepository catalogRepository,
    ISeasonPriceHistoryRepository historyRepository,
    ISyncedPriceObservationRepository? syncedRepository = null) : ISeasonPriceHistoryService
{
    public async Task<IReadOnlyList<PriceHistoryProduct>> ListTrackedProductsAsync(CancellationToken cancellationToken = default)
    {
        var localKeys = await historyRepository.GetTrackedProductKeysAsync(cancellationToken).ConfigureAwait(false);
        var importedByProduct = await LoadImportedByProductAsync(cancellationToken).ConfigureAwait(false);
        var trackedKeys = new HashSet<string>(localKeys, StringComparer.Ordinal);
        trackedKeys.UnionWith(importedByProduct.Keys);
        var products = new List<PriceHistoryProduct>();
        var listedKeys = new HashSet<string>(StringComparer.Ordinal);

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
                    ? (await GetHistoryAsync(record.ProductKey, importedByProduct, cancellationToken).ConfigureAwait(false)).Count
                    : 0;
                var isActive = record.LifecycleState is ProductLifecycleState.Active or ProductLifecycleState.PendingConfirmation;
                products.Add(new PriceHistoryProduct(record.ProductKey, record.DisplayName, trackedFamily, isActive, stateCount)
                {
                    OfficialSourceUrl = record.LastEvidence.SourceUrl,
                });
                listedKeys.Add(record.ProductKey);
            }
        }

        // Phase 26: a product this computer never had in its catalog (retired before it began checking)
        // is still listed when another computer's verified history for it was merged in — as a retired,
        // imported-only product named by its most recent observation.
        foreach (var (productKey, observations) in importedByProduct)
        {
            if (listedKeys.Contains(productKey))
            {
                continue;
            }

            var newest = observations
                .OrderByDescending(o => o.LastConfirmedUtc.UtcTicks)
                .ThenBy(o => o.ObservationId, StringComparer.Ordinal)
                .First();
            if (!OfficialPriceObservationValidator.TryClassify(productKey, newest.SourceName, newest.DisplayName, out var family))
            {
                continue;
            }

            var stateCount = (await GetHistoryAsync(productKey, importedByProduct, cancellationToken).ConfigureAwait(false)).Count;
            products.Add(new PriceHistoryProduct(productKey, newest.DisplayName, family, IsActive: false, stateCount) { IsImportedOnly = true });
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

        if (syncedRepository is not null)
        {
            var imported = await syncedRepository.GetForProductAsync(productKey, cancellationToken).ConfigureAwait(false);
            if (imported.Count > 0)
            {
                var local = await historyRepository.GetHistoryAsync(productKey, cancellationToken).ConfigureAwait(false);
                return PriceHistoryTimelineMerge.Merge(productKey, local, imported.Select(i => i.Observation).ToList());
            }
        }

        return await GetLocalHistoryAsync(productKey, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PriceHistoryEntry>> GetHistoryAsync(
        string productKey,
        IReadOnlyDictionary<string, List<OfficialPriceObservation>> importedByProduct,
        CancellationToken cancellationToken)
    {
        if (!importedByProduct.TryGetValue(productKey, out var imported))
        {
            return await GetLocalHistoryAsync(productKey, cancellationToken).ConfigureAwait(false);
        }

        var local = await historyRepository.GetHistoryAsync(productKey, cancellationToken).ConfigureAwait(false);
        return PriceHistoryTimelineMerge.Merge(productKey, local, imported);
    }

    private async Task<Dictionary<string, List<OfficialPriceObservation>>> LoadImportedByProductAsync(CancellationToken cancellationToken)
    {
        if (syncedRepository is null)
        {
            return new Dictionary<string, List<OfficialPriceObservation>>(StringComparer.Ordinal);
        }

        var all = await syncedRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Select(item => item.Observation)
            .GroupBy(o => o.ProductKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
    }

    private async Task<IReadOnlyList<PriceHistoryEntry>> GetLocalHistoryAsync(string productKey, CancellationToken cancellationToken)
    {
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
