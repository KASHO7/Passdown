namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// A small, explicit equivalence key for deciding whether two <see cref="AssociateProduct"/> rows
/// represent the same real-world commercial offer for display purposes (spec Phase 17A §9) — e.g. an
/// exact dated "Park Ticket" admission price and a generic Promotions catalog "Park Ticket" Starting
/// From card, scraped from two different pages/categories but naming the identical product. Deliberately
/// just the normalized display name today (the only signal every current adapter reliably shares for
/// the same product across categories — see <c>AssociatePassdownService.SuppressGenericFallbacksSupersededByExactDated</c>
/// and the existing Bring-A-Friend merge this generalizes from); kept as its own named type, not a raw
/// string compare, so a future dimension (bundle type, audience) can be added here without every call
/// site re-deriving its own notion of "same product."
/// </summary>
public readonly record struct ProductDisplayIdentity(string NormalizedDisplayName)
{
    public static ProductDisplayIdentity FromDisplayName(string displayName) =>
        new(Normalize(displayName));

    /// <summary>Trims, lowercases, and collapses internal whitespace — the same normalization <see cref="Domain.Catalog.CatalogProductKey"/> uses, so cosmetic source variation never creates a false mismatch.</summary>
    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
