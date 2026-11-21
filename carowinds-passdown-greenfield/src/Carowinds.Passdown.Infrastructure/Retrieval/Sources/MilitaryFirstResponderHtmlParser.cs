using System.Net;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Extracts <see cref="ParsedProductCard"/>s from a raw `sixflags.com/carowinds/military-discounts`
/// response (Phase 13C; see docs/retrieval/special-ticket-sources.md for the live investigation this
/// is built from, 2026-08-27). This page uses neither the `productv2-card` shape
/// (<see cref="ProductCardHtmlParser"/>) nor the plain add-ons shape
/// (<see cref="GeneralAddOnHtmlParser"/>) exactly — its title/price element classes match
/// <see cref="GeneralAddOnHtmlParser"/>'s (<c>h3.font-medium.text-blue-dark.type-20-600</c> /
/// <c>span.type-20-600</c>), but it additionally carries a qualifier label ("Starting From") and a
/// "Gate Price" comparison sub-block neither of that page's cards ever had. Evidenced on exactly one
/// page, so per the existing "only extract into a shared helper once proven on 2+ pages" convention
/// (docs/retrieval/catalog-source-inventory.md Part B) this is its own small, separately evidenced
/// extraction — it reuses <see cref="ProductCardHtmlParser"/>'s already-shared, already-public
/// utilities (<see cref="ProductCardHtmlParser.ExtractAttributeValue"/>/
/// <see cref="ProductCardHtmlParser.ParseAmount"/>) rather than duplicating them.
///
/// Returns the same shared <see cref="ParsedProductCard"/> record every other parser produces, so
/// the result can go straight through the existing <see cref="CatalogCardMapper"/>.
/// <see cref="ParsedProductCard.SanityProductId"/> is always null: no `data-ab-product`/Sanity CMS id
/// exists anywhere on this page (only `data-accesso-keyword`), confirmed live.
/// </summary>
internal static class MilitaryFirstResponderHtmlParser
{
    private const string CardMarker = "<h3 class=\"font-medium text-blue-dark type-20-600 break-words\">";
    private const string QualifierMarker = "<div class=\"text-gray type-14-400\">";
    private const string PriceMarker = "<span class=\"type-20-600\">";
    private const string ComparisonPriceMarker = "<span class=\"type-20-600 line-through text-black\">";
    private const string AccessoKeywordAttribute = "data-accesso-keyword";

    /// <summary>Same rationale/order-of-magnitude as the other card parsers' own caps for the last (unbounded) card on a page.</summary>
    private const int MaxCardFragmentLength = 20_000;

    /// <summary>
    /// Parses every card in <paramref name="html"/>. Returns an empty list for a well-formed page
    /// with no recognized cards (a legitimate, non-error outcome — same convention as every other
    /// parser in this project). Throws <see cref="FormatException"/> only when a card's title marker
    /// was found but its price content could not be extracted.
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

    /// <summary>Generous cap for the last current-layout card (image srcsets put ~26 KB before its price on the live page).</summary>
    private const int MaxCurrentLayoutFragmentLength = 120_000;

    /// <summary>
    /// Phase 31B: parses the whole page in either layout. The official page moved to the shared
    /// <c>productv2-card</c> component in November 2026; the original layout is still understood.
    /// Current-layout cards keep the established identity — the accesso keyword
    /// (<c>MilitaryWizard</c>, <c>FallMilitaryGoldSP2027</c>), never the card's CMS id — so existing
    /// catalog rows, overrides and price history continue unchanged. One unreadable current-layout card
    /// is isolated (its keyword reported) rather than failing the page; a malformed original-layout card
    /// still throws <see cref="FormatException"/> as before.
    /// </summary>
    public static MilitaryPageParseResult ParsePage(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var cards = new List<ParsedProductCard>(Parse(html));
        var unreadableKeywords = new List<string>();
        var unidentifiedUnreadable = 0;

        var index = html.IndexOf(ProductCardHtmlParser.CardMarker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var nextIndex = html.IndexOf(ProductCardHtmlParser.CardMarker, index + ProductCardHtmlParser.CardMarker.Length, StringComparison.Ordinal);
            var fragmentEnd = nextIndex >= 0 ? nextIndex : Math.Min(html.Length, index + MaxCurrentLayoutFragmentLength);
            var fragment = html[index..fragmentEnd];

            var card = ProductCardHtmlParser.TryParseCard(fragment);
            var keyword = card?.AccessoKeyword ?? ProductCardHtmlParser.ExtractAttributeValue(fragment, AccessoKeywordAttribute);
            if (card is not null)
            {
                cards.Add(card with { SanityProductId = null });
            }
            else if (keyword is not null)
            {
                unreadableKeywords.Add(keyword);
            }
            else
            {
                unidentifiedUnreadable++;
            }

            index = nextIndex;
        }

        return new MilitaryPageParseResult(
            cards,
            unreadableKeywords.Distinct(StringComparer.Ordinal).ToList(),
            unidentifiedUnreadable,
            HasPurchaseMarkup: html.Contains(AccessoKeywordAttribute + "=\"", StringComparison.Ordinal));
    }

    private static ParsedProductCard ParseCard(string fragment)
    {
        var displayName = ExtractElementText(fragment, CardMarker);
        var currentDisplayPrice = ExtractElementText(fragment, PriceMarker);

        if (displayName is null || currentDisplayPrice is null)
        {
            throw new FormatException(
                "A Military/First Responder card was found but its required title/price content could not " +
                "be extracted — the page structure may have changed or the response was truncated.");
        }

        // The qualifier marker's class also precedes the "Gate Price" label — only the *first*
        // occurrence in the fragment is the actual price qualifier (the "Starting From" text
        // appearing directly above the current price); the second is a label, not a value this
        // parser reads.
        var priceQualifier = ExtractElementText(fragment, QualifierMarker);
        var comparisonDisplayPrice = ExtractElementText(fragment, ComparisonPriceMarker);
        var accessoKeyword = ProductCardHtmlParser.ExtractAttributeValue(fragment, AccessoKeywordAttribute);

        return new ParsedProductCard(
            SanityProductId: null,
            AccessoKeyword: accessoKeyword,
            DisplayName: displayName,
            CurrentDisplayPrice: currentDisplayPrice,
            CurrentPriceAmount: ProductCardHtmlParser.ParseAmount(currentDisplayPrice),
            ComparisonDisplayPrice: comparisonDisplayPrice,
            ComparisonPriceAmount: ProductCardHtmlParser.ParseAmount(comparisonDisplayPrice),
            PriceQualifier: priceQualifier);
    }

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

/// <summary>
/// Phase 31B: one parse of the official Military &amp; First Responder page.
/// <see cref="HasPurchaseMarkup"/> is true when the page carries any official Buy Now keyword at all —
/// so "no readable cards" on such a page is a layout change, never evidence that the offers are gone.
/// </summary>
internal sealed record MilitaryPageParseResult(
    IReadOnlyList<ParsedProductCard> Cards,
    IReadOnlyList<string> UnreadableCardKeywords,
    int UnidentifiedUnreadableCardCount,
    bool HasPurchaseMarkup);
