using Carowinds.Passdown.Application.ProductDetails;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Promotions;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds promotions/short-term-bundles catalog adapter (Phase 3F4; see
/// docs/retrieval/catalog-source-inventory.md for the live investigation this is built from, and
/// its own live-revalidation addendum performed immediately before writing this adapter). Fetches
/// `https://www.sixflags.com/carowinds/special-offers` with a plain HTTP GET and reuses
/// <see cref="ProductCardHtmlParser"/>/<see cref="CatalogCardMapper"/> — re-confirmed live that this
/// page independently uses the exact same `productv2-card` shape — rather than re-implementing card
/// extraction. Uses <see cref="RetrievalStrategy.Html"/> for the same reason: server-rendered, no
/// Playwright needed.
///
/// Deliberately does NOT populate any product-level validity/expiration date. The only structured
/// validity field re-confirmed live on this page is the page-level `promoBar` component's
/// `dateWithTimezone` (present on some, but not all, of the page's several promoBar instances) — it
/// never appears nested inside a product card fragment, so there is still no live evidence tying it
/// to any individual product (same conclusion Phase 3F1 reached; independently re-verified rather
/// than assumed for this phase). Populating a per-product validity field from that page-level signal
/// would invent a linkage the evidence doesn't support (spec §5.2), so
/// <see cref="Application.Catalog.ObservedProduct.DateSpecific"/>/<c>ApplicableDate</c> stay false/null
/// here exactly as they do for every other catalog adapter.
///
/// Deliberately does NOT target `sixflags.com/carowinds/biggest-sale-of-the-year` (the docs'
/// secondary promotions URL): live revalidation found that page currently renders a `productv2-card`
/// instance with a title but genuinely no price content at all (a "Passholders save more!" marketing
/// CTA card, not a real product) — <see cref="ProductCardHtmlParser"/>'s all-or-nothing
/// title/price requirement would reject that as malformed and turn an otherwise-successful page load
/// into an <see cref="RetrievalStatus.Error"/>. Not addressed here; see this phase's report.
///
/// Phase 13C Part O/AG: also fetches `https://www.sixflags.com/carowinds/daily-tickets` — live
/// investigation (2026-08-27) found "Celebrate America! Bring-A-Friend Duo" (and, incidentally,
/// "Park Ticket"/"SCarowinds Bundles"/"2027 Gold Pass") only there, not on `special-offers`. Both
/// pages' cards are parsed by the same unmodified, name-agnostic <see cref="ProductCardHtmlParser"/>
/// and mapped together — no offer name is ever hardcoded, so a future new card on either page is
/// discovered automatically on its next successful refresh. "2027 Gold Pass" carries the identical
/// `data-ab-product` id on both pages (re-confirmed live), so <see cref="CatalogCardMapper"/>'s
/// existing dedup naturally merges it into one row; "Park Ticket"/"SCarowinds Bundles" have no
/// existing Promotions-category row, so they are accepted as new, additional rows under this
/// category — the same accepted multi-category duplication already established for "2027 Gold Pass"
/// appearing under both <see cref="SourceCategory.Promotions"/> and
/// <see cref="SourceCategory.SeasonPasses"/> (see docs/retrieval/catalog-source-inventory.md).
///
/// Phase 31: rebuilt around the pages' structured offer data (<see cref="OfficialOfferPageParser"/>)
/// after the official pages began publishing offer cards that sell through payment options instead of
/// a headline price (the 2026 "Black Friday Deal": Silver Pass $69 / Good Any Day Ticket $69). The
/// legacy markup parser rejected such a card as malformed, which turned every Promotions refresh into
/// an Error — so no promotion was updated, nothing was ever confirmed absent, and expired rows such as
/// "SCarowinds Bundles" stayed current. Now: structured cards first (legacy markup only for a page
/// without structured data); campaign pages linked from the offers page's own tabs (e.g.
/// <c>black-friday-deal</c>) are discovered and fetched, bounded by <see cref="MaxCampaignPages"/>;
/// a card without a readable title is reported unconfirmed instead of failing the page; prices are read
/// strictly (<see cref="Domain.Promotions.OfficialOfferText.ParseCurrencyAmount"/>, so "3rd Night FREE"
/// is never $3). The same check also yields the richer deals observation
/// (<see cref="CatalogPayload.Deals"/>). The two core pages remain required: if either fails the result
/// is an Error and nothing stored changes; a discovered campaign page failing is reported, and offers
/// last seen only there are kept, not expired.
/// </summary>
public sealed class PromotionsCatalogSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<CatalogPayload>
{
    private const string SourceName = "Carowinds Promotions";

    /// <summary>At most this many extra campaign pages per check (no crawling).</summary>
    public const int MaxCampaignPages = 3;

    private static readonly Uri PageUrl = new("https://www.sixflags.com/carowinds/special-offers");
    private static readonly Uri DailyTicketsPageUrl = new("https://www.sixflags.com/carowinds/daily-tickets");
    private static readonly Uri SiteBaseUrl = new("https://www.sixflags.com/carowinds/");

    /// <summary>Offer-page tabs already covered by this or another official source — never fetched as campaign pages.</summary>
    private static readonly HashSet<string> CoveredTabLinks = new(StringComparer.OrdinalIgnoreCase)
    {
        "special-offers", "daily-tickets", "memberships-and-season-passes", "season-passes",
        "military-discounts", "groups/tickets", "fast-lane", "add-ons", "season-pass-add-ons",
    };

    public string SourceId => "promotions.official";

    public SourceCategory Category => SourceCategory.Promotions;

    private sealed record FetchedPage(Uri Url, string Body, bool Required);

    public async Task<SourceResult<CatalogPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var observedOn = DateOnly.FromDateTime(timeChecked.DateTime);

        var pages = new List<FetchedPage>();
        foreach (var pageUrl in new[] { PageUrl, DailyTicketsPageUrl })
        {
            RetrievalHttpResponse response;
            try
            {
                response = await httpClient.GetAsync(pageUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Error(timeChecked, pageUrl, ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Error(
                    timeChecked,
                    pageUrl,
                    new InvalidOperationException($"GET {pageUrl} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(response.Body)}"));
            }

            pages.Add(new FetchedPage(pageUrl, response.Body, Required: true));
        }

        var warnings = new List<string>();
        var unreachable = new List<Uri>();
        foreach (var campaignUrl in DiscoverCampaignPages(pages[0].Body))
        {
            try
            {
                var response = await httpClient.GetAsync(campaignUrl, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    pages.Add(new FetchedPage(campaignUrl, response.Body, Required: false));
                    continue;
                }

                warnings.Add($"{campaignUrl} returned {(int)response.StatusCode}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"{campaignUrl} could not be checked: {ex.Message}");
            }

            unreachable.Add(campaignUrl);
        }

        var campaignStatements = pages
            .SelectMany(page => OfficialOfferPageParser.ParseCampaignStatements(page.Body, observedOn))
            .DistinctBy(statement => (statement.CampaignPhrase.ToLowerInvariant(), statement.End.Date))
            .ToList();

        var catalogCards = new List<ParsedProductCard>();
        var promotions = new Dictionary<string, OfficialPromotion>(StringComparer.Ordinal);
        var unreadableOfferIds = new List<string>();
        var unconfirmedKeys = new List<CatalogProductKey>();
        var details = new Dictionary<string, OfficialProductDetails>(StringComparer.Ordinal);
        var unreadableDetailIds = new List<string>();

        foreach (var page in pages)
        {
            var payload = NextFlightPayload.Extract(page.Body);
            var cards = payload is not null && payload.Contains(OfficialOfferPageParser.ProductMarker, StringComparison.Ordinal)
                ? OfficialOfferPageParser.ParseCards(payload)
                : null;

            if (cards is null)
            {
                IReadOnlyList<ParsedProductCard> htmlCards;
                try
                {
                    htmlCards = ProductCardHtmlParser.Parse(page.Body);
                }
                catch (FormatException ex)
                {
                    if (page.Required)
                    {
                        return Error(timeChecked, page.Url, ex);
                    }

                    warnings.Add($"{page.Url} could not be read: {ex.Message}");
                    unreachable.Add(page.Url);
                    continue;
                }

                catalogCards.AddRange(htmlCards);
                foreach (var htmlCard in htmlCards)
                {
                    AddPromotion(promotions, OfficialOfferMapper.FromHtmlCard(htmlCard, page.Url, timeChecked));
                }

                continue;
            }

            foreach (var card in cards)
            {
                if (card.Title is null)
                {
                    var id = card.OfficialId ?? card.Keyword;
                    warnings.Add($"An offer card on {page.Url} could not be read{(id is null ? string.Empty : $" ({id})")}.");
                    if (id is not null)
                    {
                        unreadableOfferIds.Add("offer:" + id);
                        unconfirmedKeys.Add(CatalogProductKey.FromOfficialId(Category, id));
                        unreadableDetailIds.Add(OfficialProductDetailsReader.DetailsIdFor(OfficialProductChannel.Promotions, card));
                    }

                    continue;
                }

                catalogCards.AddRange(OfficialOfferMapper.ToCatalogCards(card));
                AddPromotion(promotions, OfficialOfferMapper.ToPromotion(card, page.Url, timeChecked, campaignStatements));

                // Phase 31B: the same card's important details and deadlines (page campaign banners included).
                var cardDetails = OfficialProductDetailsReader.FromCard(
                    card, page.Url, timeChecked,
                    campaignStatements.Select(c => new CampaignDeadline(c.CampaignPhrase, c.End)).ToList(),
                    OfficialProductChannel.Promotions);
                details[cardDetails.DetailsId] = details.TryGetValue(cardDetails.DetailsId, out var existingDetails)
                    ? OfficialProductDetailsReader.MergeDuplicate(existingDetails, cardDetails)
                    : cardDetails;
            }
        }

        var products = CatalogCardMapper.ToObservedProducts(catalogCards, Category, SourceName);
        var deals = new OfficialDealsObservation(promotions.Values.ToList())
        {
            UnreadableOfferIds = unreadableOfferIds,
            UnreachablePageUrls = unreachable,
            Warnings = warnings,
        };

        return SourceResult<CatalogPayload>.Success(
            SourceId,
            new SourceEvidence(
                SourceName,
                PageUrl,
                timeChecked,
                RetrievalStatus.Verified,
                products.Count == 1
                    ? "1 promotion confirmed."
                    : $"{products.Count} promotions confirmed.",
                RetrievalStrategy.Html,
                requestedDate: null,
                technicalDetail: warnings.Count == 0 ? null : string.Join(" ", warnings)),
            new CatalogPayload(products)
            {
                UnconfirmedProductKeys = unconfirmedKeys,
                Deals = deals,
                ProductDetails = new OfficialProductDetailsObservation(OfficialProductChannel.Promotions, details.Values.ToList())
                {
                    UnreadableDetailIds = unreadableDetailIds.Distinct(StringComparer.Ordinal).ToList(),
                    UnreachablePageUrls = unreachable,
                },
            });
    }

    private static IEnumerable<Uri> DiscoverCampaignPages(string offersPageBody)
    {
        var payload = NextFlightPayload.Extract(offersPageBody);
        if (payload is null)
        {
            return [];
        }

        return OfficialOfferPageParser.ParseTabLinks(payload)
            .Where(link => !CoveredTabLinks.Contains(link))
            .Take(MaxCampaignPages)
            .Select(link => new Uri(SiteBaseUrl, link));
    }

    private static void AddPromotion(Dictionary<string, OfficialPromotion> promotions, OfficialPromotion promotion) =>
        promotions[promotion.PromotionId] = promotions.TryGetValue(promotion.PromotionId, out var existing)
            ? OfficialOfferMapper.MergeDuplicate(existing, promotion)
            : promotion;

    private SourceResult<CatalogPayload> Error(DateTimeOffset timeChecked, Uri pageUrl, Exception exception) =>
        SourceResult<CatalogPayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                SourceName,
                pageUrl,
                timeChecked,
                RetrievalStrategy.Html,
                "We couldn't check promotions right now.",
                exception));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
