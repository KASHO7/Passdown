using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// Builds the stable identity string a per-draft <see cref="ProductDraftOverride"/> is keyed by
/// (spec §8.3's "reconcile ... per-draft include/exclude and display overrides"). Dated products
/// (park hours has none; admission/Fast Lane/haunted do) have no <c>CatalogProductKey</c> of their
/// own — they are scoped to one already-selected operating date by construction, so a per-category
/// package id is sufficient identity without also encoding the date. Catalog products already carry
/// a globally stable <c>CatalogProductKey</c> (spec §7.2), reused as-is. The two prefixes
/// (<c>dated|</c>/<c>catalog|</c>) guarantee the two families can never collide.
/// </summary>
public static class AssociateProductIdentity
{
    public static string ForDatedProduct(SourceCategory category, string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return $"dated|{category}|{packageId.Trim()}";
    }

    /// <param name="catalogProductKeyText">A <c>Domain.Catalog.CatalogProductKey.ToString()</c> value, or the equivalent stored <c>ProductKey</c> text.</param>
    public static string ForCatalogProduct(string catalogProductKeyText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogProductKeyText);
        return $"catalog|{catalogProductKeyText}";
    }
}
