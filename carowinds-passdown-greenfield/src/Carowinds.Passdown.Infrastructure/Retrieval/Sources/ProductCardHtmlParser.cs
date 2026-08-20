using System.Globalization;
using System.Net;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Extracts <see cref="ParsedProductCard"/>s from a raw `sixflags.com/carowinds/*` catalog page
/// response (Phase 3F1; see docs/retrieval/catalog-source-inventory.md for the live investigation
/// this is built from). The `data-ab="productv2-card"` component and its
/// `data-ab-product`/`data-ab="productv2-card-title"`/`data-ab="productv2-card-price"`/
/// `data-ab="productv2-card-price-strikethrough"`/`data-ab="productv2-card-price-before"`/
/// `data-accesso-keyword` attributes were independently confirmed byte-for-byte identical across
/// six separately fetched pages (season passes, daily tickets, special offers, Fast Lane, the
/// current promotion page, and season-pass add-ons) — not extracted from a single page's markup.
///
/// Deliberately does not know about <see cref="Domain.SourceCategory"/>, product filtering, seasonal
/// interpretation, or catalog identity — this is pure transport/markup shape, the same boundary
/// <c>AccessoKeywordCalendarApiResponse</c> keeps for the JSON sources (Phase 3C/3D/3E). A caller
/// combines a <see cref="ParsedProductCard"/> with its own category/source-name context to build a
/// real <see cref="Domain.Catalog.CatalogProductKey"/> — this type never does that itself.
///
/// Not a full HTML parser: this scans for the specific, well-proven repeating attribute markers
/// above using plain substring search rather than parsing a DOM tree. That is a deliberate,
/// documented tradeoff (see docs' "known limitations") rather than an oversight — introducing a
/// full HTML-parsing dependency was considered and deferred until an actual adapter needs more
/// robustness than this provides.
/// </summary>
internal static class ProductCardHtmlParser
{
    private const string CardMarker = "data-ab=\"productv2-card\"";
    private const string TitleMarker = "data-ab=\"productv2-card-title\"";
    private const string PriceMarker = "data-ab=\"productv2-card-price\"";
    private const string ComparisonPriceMarker = "data-ab=\"productv2-card-price-strikethrough\"";
    private const string QualifierMarker = "data-ab=\"productv2-card-price-before\"";
    private const string SanityIdAttribute = "data-ab-product";
    private const string AccessoKeywordAttribute = "data-accesso-keyword";

    /// <summary>
    /// Caps how far a card's own fragment can extend past its start marker. Needed only for
    /// whichever card happens to be *last* on a page — with no following card marker to bound it,
    /// its "fragment" would otherwise run to the end of the whole (often 700KB+) HTML response.
    /// Every real card's actual content (title through its Buy Now button) observed during
    /// investigation was well under 5,000 characters even with full image-srcset markup included;
    /// this is a generous multiple of that, not a tight fit.
    /// </summary>
    private const int MaxCardFragmentLength = 30_000;

    /// <summary>
    /// Parses every product card in <paramref name="html"/>. Returns an empty list when the page
    /// is well-formed but contains no recognized cards at all — a legitimate, non-error outcome
    /// (Phase 3F1 Part D: a successfully retrieved empty catalog must be distinguishable from a
    /// parse failure). Throws <see cref="FormatException"/> only when a card marker was found but
    /// its required title/price content could not be extracted from within it, signaling a
    /// truncated or restructured response rather than a genuinely empty catalog.
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
        var displayName = ExtractElementText(fragment, TitleMarker);
        var currentDisplayPrice = ExtractElementText(fragment, PriceMarker);

        if (displayName is null || currentDisplayPrice is null)
        {
            throw new FormatException(
                "A product card was found but its required title/price content could not be extracted — " +
                "the page structure may have changed or the response was truncated.");
        }

        var comparisonDisplayPrice = ExtractElementText(fragment, ComparisonPriceMarker);
        var priceQualifier = ExtractElementText(fragment, QualifierMarker);
        var sanityProductId = ExtractAttributeValue(fragment, SanityIdAttribute);
        var accessoKeyword = ExtractAttributeValue(fragment, AccessoKeywordAttribute);

        return new ParsedProductCard(
            SanityProductId: sanityProductId,
            AccessoKeyword: accessoKeyword,
            DisplayName: displayName,
            CurrentDisplayPrice: currentDisplayPrice,
            CurrentPriceAmount: ParseAmount(currentDisplayPrice),
            ComparisonDisplayPrice: comparisonDisplayPrice,
            ComparisonPriceAmount: ParseAmount(comparisonDisplayPrice),
            PriceQualifier: priceQualifier);
    }

    /// <summary>Finds `{exactMarker}...&gt;TEXT&lt;` and returns the decoded, trimmed TEXT — the marker must match exactly (e.g. `productv2-card-price"`, not `productv2-card-price-before"`) since callers rely on the closing quote to disambiguate sibling attributes with a shared prefix.</summary>
    private static string? ExtractElementText(string fragment, string exactMarker)
    {
        var markerIndex = fragment.IndexOf(exactMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var tagEnd = fragment.IndexOf('>', markerIndex);
        if (tagEnd < 0)
        {
            return null;
        }

        var textEnd = fragment.IndexOf('<', tagEnd);
        if (textEnd < 0)
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(fragment[(tagEnd + 1)..textEnd]).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Finds `{attributeName}="VALUE"` and returns the decoded VALUE, or null if the attribute
    /// isn't present or its value is empty. Internal rather than private, and taking the fragment/
    /// attribute name as plain parameters rather than referencing any of this class's own markup
    /// constants, so <see cref="GeneralAddOnHtmlParser"/> (Phase 3F5) can reuse this one generic
    /// "find `attr="`, read to the closing quote" utility for its own, entirely different attribute
    /// (`data-accesso-keyword`) instead of duplicating the same 15 lines. Reusing this is not the
    /// markup-shape reuse the project's "don't generalize the shared parser" rule is about — the two
    /// parsers still scan for entirely different card/title/price markers and know nothing about
    /// each other's shape; this is shared exactly the way <see cref="ParseAmount"/> is.
    /// </summary>
    internal static string? ExtractAttributeValue(string fragment, string attributeName)
    {
        var marker = $"{attributeName}=\"";
        var markerIndex = fragment.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var valueStart = markerIndex + marker.Length;
        var valueEnd = fragment.IndexOf('"', valueStart);
        if (valueEnd < 0)
        {
            return null;
        }

        var value = WebUtility.HtmlDecode(fragment[valueStart..valueEnd]);
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// Strips everything but digits and the decimal point (e.g. `"$1,299.00"` → `1299.00`); returns
    /// null rather than a guessed number when nothing parseable remains. Internal for the same
    /// reason as <see cref="ExtractAttributeValue"/> above.
    /// </summary>
    internal static decimal? ParseAmount(string? displayPrice)
    {
        if (displayPrice is null)
        {
            return null;
        }

        var digitsOnly = new string(displayPrice.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray());
        return decimal.TryParse(digitsOnly, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }
}

/// <summary>
/// One product card as extracted from a catalog page, before any category/identity interpretation.
/// <see cref="SanityProductId"/> (from `data-ab-product`) is the underlying CMS document id.
/// <b>Not assumed stable across different pages</b>: Phase 3F1 originally observed the same id for
/// the "2027 Gold Pass" card on both `season-passes` and `special-offers`, but Phase 3F4's live
/// re-check found `special-offers` now serves a *different* `data-ab-product` value for that
/// same-named card (see docs/retrieval/catalog-source-inventory.md's Phase 3F4/3F5 addendum) — so
/// this id must only be treated as stable for the one page/adapter that observed it, never used to
/// deduplicate or merge a product across two different adapters' retrievals. This is safe by
/// construction today: <see cref="Domain.Catalog.CatalogProductKey"/> scopes every id by
/// <see cref="Domain.SourceCategory"/>, so two adapters never share an identity space regardless.
/// <see cref="AccessoKeyword"/> (from `data-accesso-keyword`) is the checkout entry keyword — the
/// same kind of identifier `DatedAdmissionSource`/`DatedFastLaneSource`/`DatedHauntedSource` use as
/// a dated product's keyword, but here naming a catalog product line instead of a per-date package.
/// Either may be null: neither is guaranteed present on every card observed
/// (docs/retrieval/catalog-source-inventory.md). A caller without either falls back to source+name
/// identity via <see cref="Domain.Catalog.CatalogProductKey.Create"/> — this type does not build a
/// key itself.
/// </summary>
internal sealed record ParsedProductCard(
    string? SanityProductId,
    string? AccessoKeyword,
    string DisplayName,
    string CurrentDisplayPrice,
    decimal? CurrentPriceAmount,
    string? ComparisonDisplayPrice,
    decimal? ComparisonPriceAmount,
    string? PriceQualifier);
