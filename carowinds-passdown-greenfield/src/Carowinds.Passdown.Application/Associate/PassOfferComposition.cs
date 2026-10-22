using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// Phase 25: presents a renewal offer and an initiation fee where an associate reads them — on their
/// own Season Pass / Membership row ("Gold Season Pass — 2027 · $99.00 · Renewal offer $89.00";
/// "Gold Membership · $7.00/month · $20.00 initiation fee") — instead of as separate rows. The
/// catalog and price history keep them as separate products with separate identities; only this
/// composed view folds them. A companion whose parent row is not present (or has no verified amount)
/// stays as its own row, so no published price is ever dropped.
/// </summary>
public static class PassOfferComposition
{
    public static void FoldCompanions(List<AssociateProduct> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        for (var i = products.Count - 1; i >= 0; i--)
        {
            var companion = products[i];
            if (companion.IsCustom || companion.PassOffer?.Parent is not { } parentOffer || companion.OfficialPriceAmount is not { } amount)
            {
                continue;
            }

            var parentIndex = products.FindIndex(product => !product.IsCustom && parentOffer.Equals(product.PassOffer));
            if (parentIndex < 0)
            {
                continue;
            }

            var parent = products[parentIndex];
            products[parentIndex] = companion.PassOffer.PriceType == PassPriceType.Renewal
                ? parent with { RenewalPriceAmount = amount }
                : parent with { InitiationFeeAmount = amount };
            products.RemoveAt(i);
        }
    }
}
