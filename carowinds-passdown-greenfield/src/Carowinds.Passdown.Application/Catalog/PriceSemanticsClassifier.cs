using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Catalog;

/// <summary>
/// Computes <see cref="PriceKind"/> and, where the source's own qualifier text describes a
/// multi-person bundle, a derived per-person breakdown — entirely from fields already persisted on
/// <see cref="Domain.Retrieval.SourceEvidence"/>-backed catalog rows (<c>PriceQualifier</c>,
/// comparison price, current price). Phase 13C Part AM/AN: deliberately computed here, at
/// composition time, rather than stored — no new column/schema migration was needed, and every
/// existing cached row (whose qualifier text already survived unchanged, per
/// docs/retrieval/pricing-semantics.md) classifies correctly the first time it is composed, with no
/// backfill.
/// </summary>
public static class PriceSemanticsClassifier
{
    private const string StartingFromQualifier = "Starting From";
    private const string SaleQualifier = "Sale Price";

    /// <summary>
    /// Classifies a non-date-specific catalog product's current price. Never called for dated
    /// products — those are always <see cref="PriceKind.ExactDated"/> by construction
    /// (<c>DateSpecific = true</c> is already the authoritative "is this exact" signal per
    /// docs/retrieval/pricing-semantics.md).
    /// </summary>
    public static PriceKind ClassifyCatalog(string? priceQualifier, decimal? comparisonPriceAmount)
    {
        var trimmedQualifier = string.IsNullOrWhiteSpace(priceQualifier) ? null : priceQualifier.Trim();

        if (trimmedQualifier is null)
        {
            return comparisonPriceAmount is not null ? PriceKind.Sale : PriceKind.StandardCatalog;
        }

        if (string.Equals(trimmedQualifier, StartingFromQualifier, StringComparison.OrdinalIgnoreCase))
        {
            return PriceKind.StartingFrom;
        }

        if (string.Equals(trimmedQualifier, SaleQualifier, StringComparison.OrdinalIgnoreCase))
        {
            return PriceKind.Sale;
        }

        if (comparisonPriceAmount is null && TryParseBundle(trimmedQualifier, currentPriceAmount: 1m) is not null)
        {
            return PriceKind.Bundle;
        }

        return PriceKind.StandardCatalog;
    }

    /// <summary>
    /// Recognizes the structural shape "&lt;number word&gt; &lt;noun&gt; Just" (e.g. "Two Friends
    /// Just", observed live on "Celebrate America! Bring-A-Friend Duo") — a generic, name-agnostic
    /// pattern (Phase 13C Part O: parsers must discover new deals by shape, never by hardcoding an
    /// offer's own name) rather than a lookup keyed on "Bring-A-Friend Duo" specifically. Returns
    /// <c>null</c> (never guesses) when the qualifier doesn't match this exact shape or
    /// <paramref name="currentPriceAmount"/> is unavailable — per Phase 13C Part I, an undetectable
    /// bundle shape must never be forced into a per-person figure.
    /// </summary>
    public static BundleBreakdown? TryParseBundle(string? priceQualifier, decimal? currentPriceAmount)
    {
        if (string.IsNullOrWhiteSpace(priceQualifier) || currentPriceAmount is not { } total)
        {
            return null;
        }

        var words = priceQualifier.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3 || !string.Equals(words[^1], "Just", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!TryParseNumberWord(words[0], out var quantity))
        {
            return null;
        }

        return new BundleBreakdown(quantity, Math.Round(total / quantity, 2, MidpointRounding.AwayFromZero));
    }

    private static bool TryParseNumberWord(string word, out int value)
    {
        value = word.ToLowerInvariant() switch
        {
            "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            "five" => 5,
            "six" => 6,
            _ => 0,
        };

        return value > 0;
    }
}

/// <summary>A source-described multi-person bundle's per-person breakdown, derived from its stated quantity and total price.</summary>
public sealed record BundleBreakdown(int Quantity, decimal PerPersonPriceAmount);
