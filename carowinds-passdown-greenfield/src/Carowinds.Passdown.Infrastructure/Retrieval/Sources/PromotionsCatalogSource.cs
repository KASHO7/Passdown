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
/// into an <see cref="RetrievalStatus.Error"/>. That is out of scope for this phase (only
/// `special-offers` was asked for) and is not addressed here; see this phase's report for the full
/// finding.
/// </summary>
public sealed class PromotionsCatalogSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<CatalogPayload>
{
    private const string SourceName = "Carowinds Promotions";

    private static readonly Uri PageUrl = new("https://www.sixflags.com/carowinds/special-offers");

    public string SourceId => "promotions.official";

    public SourceCategory Category => SourceCategory.Promotions;

    public async Task<SourceResult<CatalogPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);

        RetrievalHttpResponse response;
        try
        {
            response = await httpClient.GetAsync(PageUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(timeChecked, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Error(
                timeChecked,
                new InvalidOperationException($"GET {PageUrl} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(response.Body)}"));
        }

        IReadOnlyList<ParsedProductCard> cards;
        try
        {
            cards = ProductCardHtmlParser.Parse(response.Body);
        }
        catch (FormatException ex)
        {
            return Error(timeChecked, ex);
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

    private SourceResult<CatalogPayload> Error(DateTimeOffset timeChecked, Exception exception) =>
        SourceResult<CatalogPayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                SourceName,
                PageUrl,
                timeChecked,
                RetrievalStrategy.Html,
                "We couldn't check promotions right now.",
                exception));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
