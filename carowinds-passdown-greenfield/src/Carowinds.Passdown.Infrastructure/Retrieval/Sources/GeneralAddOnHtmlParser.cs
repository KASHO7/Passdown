using System.Net;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Extracts <see cref="ParsedProductCard"/>s from a raw `sixflags.com/carowinds/add-ons` response
/// (Phase 3F5; see docs/retrieval/catalog-source-inventory.md for the Phase 3F1 finding this
/// confirms and this class's own doc comment / catalog-source-inventory.md Phase 3F4/3F5 addendum
/// for the narrow live revalidation performed immediately before writing this parser). The page
/// does not use `productv2-card` at all — re-confirmed live, zero matches, same as Phase 3F1 — so
/// this is a small, separately evidenced extraction for its own simpler card shape rather than a
/// generalization of <see cref="ProductCardHtmlParser"/>.
///
/// Returns the same shared <see cref="ParsedProductCard"/> record <see cref="ProductCardHtmlParser"/>
/// produces so a caller can hand its output straight to the existing
/// <see cref="CatalogCardMapper"/> — no second product-identity system.
/// <see cref="ParsedProductCard.SanityProductId"/> is always null here: this page's cards carry no
/// Sanity CMS document id (only `data-accesso-keyword`), confirmed live and by
/// docs/retrieval/catalog-source-inventory.md's Phase 3F1 row for this page.
/// <see cref="ParsedProductCard.ComparisonDisplayPrice"/>/<see cref="ParsedProductCard.PriceQualifier"/>
/// are always null: no comparison-price element and no qualifier label exist anywhere in this
/// page's markup for either real product observed, re-confirmed live during this phase — extracting
/// them would mean guessing at a markup shape with zero evidence, which the project's
/// never-fabricate rule (spec §5.2) already forbids. If a later revalidation finds such a field, add
/// extraction for it then, grounded in that evidence — same discipline already applied to the
/// promo-bar validity date investigated (and rejected) in Phase 3F1/3F4.
/// </summary>
internal static class GeneralAddOnHtmlParser
{
    private const string CardMarker = "<h3 class=\"font-medium text-blue-dark type-20-600 break-words\">";
    private const string PriceMarker = "<span class=\"type-20-600\">";
    private const string AccessoKeywordAttribute = "data-accesso-keyword";

    /// <summary>
    /// Caps how far a card's own fragment can extend past its start marker, for whichever card is
    /// last on the page (no following card marker to bound it). Every real card's actual content
    /// (title through its Buy Now button) observed during investigation was well under 2,000
    /// characters; this is a generous multiple of that, matching the same tradeoff
    /// <see cref="ProductCardHtmlParser"/> makes for its own last-card case.
    /// </summary>
    private const int MaxCardFragmentLength = 20_000;

    /// <summary>
    /// Parses every add-on card in <paramref name="html"/>. Returns an empty list when the page is
    /// well-formed but contains no recognized cards at all — a legitimate, non-error outcome, same
    /// convention as <see cref="ProductCardHtmlParser.Parse"/>. Throws <see cref="FormatException"/>
    /// only when a card's title marker was found but its price content could not be extracted,
    /// signaling a truncated or restructured response rather than a genuinely empty catalog.
    /// </summary>
    public static IReadOnlyList<ParsedProductCard> Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var cards = new List<ParsedProductCard>();
        var index = html.IndexOf(CardMarker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var nextIndex = html.IndexOf(CardMarker, index + CardMarker.Length, StringComparison.Ordinal);
            var fragmentEnd = nextIndex >= 0 ? nextIndex : Math.Min(html.Length, index + MaxCardFragmentLength);
            var fragment = html[index..fragmentEnd];

            cards.Add(ParseCard(fragment));

            index = nextIndex;
        }

        return cards;
    }

    private static ParsedProductCard ParseCard(string fragment)
    {
        var displayName = ExtractElementText(fragment, CardMarker);
        var currentDisplayPrice = ExtractElementText(fragment, PriceMarker);

        if (displayName is null || currentDisplayPrice is null)
        {
            throw new FormatException(
                "An add-on card was found but its required title/price content could not be extracted — " +
                "the page structure may have changed or the response was truncated.");
        }

        var accessoKeyword = ProductCardHtmlParser.ExtractAttributeValue(fragment, AccessoKeywordAttribute);

        return new ParsedProductCard(
            SanityProductId: null,
            AccessoKeyword: accessoKeyword,
            DisplayName: displayName,
            CurrentDisplayPrice: currentDisplayPrice,
            CurrentPriceAmount: ProductCardHtmlParser.ParseAmount(currentDisplayPrice),
            ComparisonDisplayPrice: null,
            ComparisonPriceAmount: null,
            PriceQualifier: null);
    }

    /// <summary>
    /// Finds `{marker}TEXT&lt;` and returns the decoded, trimmed TEXT. Unlike
    /// <c>ProductCardHtmlParser.ExtractElementText</c>, this page's markers (a full literal opening
    /// tag with no attribute in between) already end in `&gt;`, so there is no separate "find the
    /// attribute, then find the tag's closing angle bracket" step.
    /// </summary>
    private static string? ExtractElementText(string fragment, string marker)
    {
        var markerIndex = fragment.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var textStart = markerIndex + marker.Length;
        var textEnd = fragment.IndexOf('<', textStart);
        if (textEnd < 0)
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(fragment[textStart..textEnd]).Trim();
        return text.Length == 0 ? null : text;
    }
}
