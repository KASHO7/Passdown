using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Catalog;

/// <summary>
/// Phase 26 correction (found on RC.9): the read-time counterpart of the Phase 25 legacy pass identity
/// rule. A computer that checked the pre-redesign page keeps a Season Pass under its legacy key
/// ("2027 Gold Pass" → <c>SeasonPasses:id:550e3db6-…</c>), while a computer that started after the
/// redesign files the same real product under its pass-offer key
/// (<c>SeasonPasses:pass-offer:carowinds|gold|season-pass|new-purchase|2027</c>). Observations merged
/// in through Personal Sync arrive under the contributor's key, so without this rule they would sit
/// apart from this computer's product as a separate, retired "imported history" entry.
/// <para>
/// Returns imported key → this computer's catalog key, only when all of these hold (the same
/// conditions reconciliation uses to continue a legacy identity): both denote a Season Pass
/// <b>new-purchase</b> offer with a stated season year; the imported key is not in this computer's
/// catalog; exactly one catalog record denotes that offer; at most one legacy (non pass-offer) key
/// for that offer is known locally or through sync; and every imported observation under a legacy key
/// names the same tier and year. A yearless "Gold Pass", a renewal, a Membership or an initiation fee is
/// never folded, and an ambiguous match folds nothing. Display only — no stored key is rewritten.
/// </para>
/// </summary>
public static class ImportedPassIdentityFolding
{
    public static IReadOnlyDictionary<string, string> Resolve(
        IEnumerable<CatalogProductRecord> catalogRecords,
        IReadOnlyDictionary<string, List<OfficialPriceObservation>> importedByProduct)
    {
        ArgumentNullException.ThrowIfNull(catalogRecords);
        ArgumentNullException.ThrowIfNull(importedByProduct);

        var catalog = catalogRecords.Where(r => r.Category == SourceCategory.SeasonPasses).ToList();
        var catalogKeys = catalog.Select(r => r.ProductKey).ToHashSet(StringComparer.Ordinal);

        var catalogByOffer = catalog
            .Select(r => (Record: r, Offer: NewPurchaseOffer(r.ProductKey, [r.DisplayName])))
            .Where(entry => entry.Offer is not null)
            .GroupBy(entry => entry.Offer!, entry => entry.Record)
            .ToDictionary(group => group.Key, group => group.ToList());

        var importedOffers = importedByProduct
            .Where(pair => !catalogKeys.Contains(pair.Key))
            .Select(pair => (Key: pair.Key, Offer: NewPurchaseOffer(pair.Key, pair.Value.Select(o => o.DisplayName))))
            .Where(entry => entry.Offer is not null)
            .ToList();

        var legacyKeysByOffer = catalogByOffer
            .SelectMany(pair => pair.Value.Select(r => (Offer: pair.Key, r.ProductKey)))
            .Concat(importedOffers.Select(entry => (Offer: entry.Offer!, ProductKey: entry.Key)))
            .Where(entry => PassOffer.FromKey(entry.ProductKey) is null)
            .GroupBy(entry => entry.Offer, entry => entry.ProductKey)
            .ToDictionary(group => group.Key, group => group.Distinct(StringComparer.Ordinal).Count());

        var folded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, offer) in importedOffers)
        {
            if (catalogByOffer.TryGetValue(offer!, out var records) && records.Count == 1
                && legacyKeysByOffer.GetValueOrDefault(offer!) <= 1)
            {
                folded[key] = records[0].ProductKey;
            }
        }

        return folded;
    }

    /// <summary>
    /// The Season Pass new-purchase offer (with a year) a key denotes: its pass-offer key, or — for a
    /// legacy key — the legacy-name rule applied to every given name, which must all agree.
    /// </summary>
    private static PassOffer? NewPurchaseOffer(string productKey, IEnumerable<string> displayNames)
    {
        if (!productKey.StartsWith($"{SourceCategory.SeasonPasses}:", StringComparison.Ordinal))
        {
            return null;
        }

        var offer = PassOffer.FromKey(productKey);
        if (offer is null)
        {
            var named = displayNames.Select(PassOffer.FromLegacyName).Distinct().ToList();
            offer = named is [{ } single] ? single : null;
        }

        return offer is { Product: PassProductType.SeasonPass, PriceType: PassPriceType.NewPurchase, SeasonYear: not null } ? offer : null;
    }
}
