namespace Carowinds.Passdown.Domain.Catalog;

using Carowinds.Passdown.Domain;

/// <summary>
/// Stable normalized identity for a catalog product, per spec §7.2's priority
/// order: official product/package id first, then official URL, then normalized
/// source + category + product name. Never identifies a product by display price
/// or unnormalized heading alone.
/// </summary>
public sealed record CatalogProductKey
{
    public SourceCategory Category { get; }
    public string Value { get; }

    private CatalogProductKey(SourceCategory category, string value)
    {
        Category = category;
        Value = value;
    }

    public static CatalogProductKey FromOfficialId(SourceCategory category, string officialProductId)
    {
        if (string.IsNullOrWhiteSpace(officialProductId))
            throw new ArgumentException("Official product id is required.", nameof(officialProductId));

        return new CatalogProductKey(category, $"id:{Normalize(officialProductId)}");
    }

    public static CatalogProductKey FromOfficialUrl(SourceCategory category, Uri officialUrl)
    {
        ArgumentNullException.ThrowIfNull(officialUrl);

        return new CatalogProductKey(category, $"url:{Normalize(officialUrl.ToString())}");
    }

    public static CatalogProductKey FromSourceAndName(SourceCategory category, string sourceName, string productName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("Source name is required.", nameof(sourceName));
        if (string.IsNullOrWhiteSpace(productName))
            throw new ArgumentException("Product name is required.", nameof(productName));

        return new CatalogProductKey(category, $"name:{Normalize(sourceName)}|{Normalize(productName)}");
    }

    /// <summary>
    /// Picks the most stable identity available for a retrieved product, following
    /// spec §7.2's priority order.
    /// </summary>
    public static CatalogProductKey Create(
        SourceCategory category,
        string sourceName,
        string productName,
        string? officialProductId = null,
        Uri? officialUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(officialProductId))
            return FromOfficialId(category, officialProductId);

        if (officialUrl is not null)
            return FromOfficialUrl(category, officialUrl);

        return FromSourceAndName(category, sourceName, productName);
    }

    /// <summary>Trims, lowercases, and collapses internal whitespace so cosmetic source variation does not mint a new identity.</summary>
    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Reconstitutes a key from its own <see cref="ToString"/> output — the raw
    /// identity text persisted as storage's product-key column. For callers (e.g.
    /// reconciliation) that loaded a product's identity from storage and need a
    /// real key to write an updated record back with. Never used to mint a *new*
    /// identity from freshly retrieved product data — see <see cref="Create"/> for that.
    /// </summary>
    public static CatalogProductKey Parse(string storedIdentity)
    {
        if (string.IsNullOrWhiteSpace(storedIdentity))
            throw new ArgumentException("Stored identity text is required.", nameof(storedIdentity));

        var separatorIndex = storedIdentity.IndexOf(':');
        if (separatorIndex < 0 || !Enum.TryParse<SourceCategory>(storedIdentity[..separatorIndex], out var category))
        {
            throw new ArgumentException(
                $"'{storedIdentity}' is not a recognized stored catalog product identity.",
                nameof(storedIdentity));
        }

        return new CatalogProductKey(category, storedIdentity[(separatorIndex + 1)..]);
    }

    /// <summary>
    /// Phase 21A: a distinct identity for a different pass validity year under the same official
    /// id/url/name tier — <c>{Value}|pass-year:{year}</c>. Used only when the stored product under
    /// this key states a different year than the observation (see <see cref="PassValidityYear"/>), so
    /// every existing key stays unchanged and no stored row is rewritten.
    /// </summary>
    public CatalogProductKey WithPassYear(int passYear) =>
        new(Category, $"{Value}|pass-year:{passYear.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    public override string ToString() => $"{Category}:{Value}";
}
