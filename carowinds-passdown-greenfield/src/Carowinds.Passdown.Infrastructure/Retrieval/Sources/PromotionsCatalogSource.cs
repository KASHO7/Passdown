using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
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
/// </summary>
public sealed class PromotionsCatalogSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<CatalogPayload>
{
    private const string SourceName = "Carowinds Promotions";

    private static readonly Uri PageUrl = new("https://www.sixflags.com/carowinds/special-offers");
    private static readonly Uri DailyTicketsPageUrl = new("https://www.sixflags.com/carowinds/daily-tickets");

    public string SourceId => "promotions.official";

    public SourceCategory Category => SourceCategory.Promotions;

    public async Task<SourceResult<CatalogPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);

        var cards = new List<ParsedProductCard>();
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

            try
            {
                cards.AddRange(ProductCardHtmlParser.Parse(response.Body));
            }
            catch (FormatException ex)
            {
                return Error(timeChecked, pageUrl, ex);
            }
        }

        var products = CatalogCardMapper.ToObservedProducts(cards, Category, SourceName);

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
                requestedDate: null),
            new CatalogPayload(products));
    }

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
