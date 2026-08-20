using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds general add-ons catalog adapter (Phase 3F5; see
/// docs/retrieval/catalog-source-inventory.md for the Phase 3F1 investigation this is built from,
/// and its own live-revalidation addendum performed immediately before writing this adapter).
/// Fetches `https://www.sixflags.com/carowinds/add-ons` with a plain HTTP GET and uses
/// <see cref="GeneralAddOnHtmlParser"/> — a small, separately evidenced parser for this page's own
/// simpler card shape (re-confirmed live: no `productv2-card` markup here) — then reuses
/// <see cref="CatalogCardMapper"/> unchanged for identity/mapping, same as every other catalog
/// adapter. Uses <see cref="RetrievalStrategy.Html"/>: real product names/prices are server-rendered
/// into the response body, so no Playwright is needed.
///
/// Distinct <see cref="SourceId"/> from <see cref="SeasonPassAddOnsCatalogSource"/>, but shares
/// <see cref="SourceCategory.AddOns"/> with it (spec §6.1: general and season-pass add-ons share one
/// cadence/category) — reconciliation and the freshness scheduler treat both sources' products as
/// one combined add-ons catalog.
/// </summary>
public sealed class GeneralAddOnsCatalogSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<CatalogPayload>
{
    private const string SourceName = "Carowinds General Add-Ons";

    private static readonly Uri PageUrl = new("https://www.sixflags.com/carowinds/add-ons");

    public string SourceId => "general-add-ons.official";

    public SourceCategory Category => SourceCategory.AddOns;

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
            cards = GeneralAddOnHtmlParser.Parse(response.Body);
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
                    ? "1 general add-on confirmed."
                    : $"{products.Count} general add-ons confirmed.",
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
                "We couldn't check general add-ons right now.",
                exception));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
