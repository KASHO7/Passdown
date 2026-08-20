using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Turns <see cref="ParsedProductCard"/>s into <see cref="ObservedProduct"/>s — the one piece of
/// mapping logic genuinely shared between <see cref="SeasonPassCatalogSource"/> and
/// <see cref="SeasonPassAddOnsCatalogSource"/> (Phase 3F2/3F3), both of which read the same
/// `productv2-card` shape and need the identical id-priority/deduplication rules applied. Category
/// interpretation, price/status decisions, and anything else adapter-specific stay out of this —
/// callers supply <c>category</c> and <c>sourceName</c>; this only decides how one already-parsed
/// card becomes one <see cref="ObservedProduct"/>.
/// </summary>
internal static class CatalogCardMapper
{
    /// <summary>
    /// Builds identity using spec §7.2's existing priority order via
    /// <see cref="CatalogProductKey.Create"/>: an official id first, then a source+name fallback.
    /// No product URL was ever observed on these pages (docs/retrieval/catalog-source-inventory.md
    /// — "View Details" opens a modal, not a link), so the URL tier is never populated here rather
    /// than being filled with a fabricated address. <see cref="ParsedProductCard.SanityProductId"/>
    /// is preferred over <see cref="ParsedProductCard.AccessoKeyword"/> as the official id when both
    /// are present, because the accesso keyword can embed a plan year (e.g. `FallGoldSP2027`) that
    /// may change season to season while the Sanity id does not. This preference is scoped to
    /// picking the better id <i>within one adapter's own retrieval</i>, not a claim that a Sanity id
    /// is stable across different pages/adapters — Phase 3F4 found live evidence it is not (see
    /// <see cref="ParsedProductCard"/>'s doc comment and
    /// docs/retrieval/catalog-source-inventory.md's Phase 3F4/3F5 addendum); nothing here compares
    /// or merges ids across two different `ToObservedProducts` calls, so that instability doesn't
    /// affect this method. Either id, when present alone, is still a genuine official id — not a
    /// guess.
    ///
    /// Deduplicates deterministically: if the same identity appears more than once in
    /// <paramref name="cards"/> (e.g. a page rendering the same card for two responsive
    /// breakpoints), the first occurrence in document order is kept and later ones are dropped,
    /// rather than letting a duplicate reach <see cref="IProductReconciliationService"/> and risk
    /// two conflicting reconciliation outcomes for one key in a single batch.
    /// </summary>
    public static IReadOnlyList<ObservedProduct> ToObservedProducts(
        IReadOnlyList<ParsedProductCard> cards,
        SourceCategory category,
        string sourceName)
    {
        var seenKeys = new HashSet<string>();
        var products = new List<ObservedProduct>(cards.Count);

        foreach (var card in cards)
        {
            var key = CatalogProductKey.Create(
                category,
                sourceName,
                card.DisplayName,
                officialProductId: card.SanityProductId ?? card.AccessoKeyword);

            if (!seenKeys.Add(key.ToString()))
            {
                continue;
            }

            products.Add(new ObservedProduct(
                Key: key,
                DisplayName: card.DisplayName,
                CurrentPriceAmount: card.CurrentPriceAmount,
                CurrentDisplayPrice: card.CurrentDisplayPrice,
                ComparisonPriceAmount: card.ComparisonPriceAmount,
                PriceQualifier: card.PriceQualifier,
                DateSpecific: false,
                ApplicableDate: null));
        }

        return products;
    }
}
