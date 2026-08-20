using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds season-pass add-ons catalog adapter (Phase 3F3; see
/// docs/retrieval/catalog-source-inventory.md for the live investigation this is built from, and
/// its own class doc comment for a narrow live revalidation performed immediately before writing
/// this adapter). Fetches `https://www.sixflags.com/carowinds/season-pass-add-ons` with a plain
/// HTTP GET and reuses <see cref="ProductCardHtmlParser"/>/<see cref="CatalogCardMapper"/> —
/// identical to <see cref="SeasonPassCatalogSource"/>'s reuse — since this page independently uses
/// the exact same `productv2-card` shape (docs: "same `productv2-card` markup as season passes").
/// Uses <see cref="RetrievalStrategy.Html"/> for the same reason: server-rendered, no Playwright
/// needed.
///
/// Distinct <see cref="SourceId"/> from every other adapter, but shares
/// <see cref="SourceCategory.AddOns"/> with a future general-add-ons adapter (spec §6.1: general
/// and season-pass add-ons share one category/cadence) — <see cref="ProductCardHtmlParser"/> and
/// <see cref="CatalogCardMapper"/> know nothing about that; category is only ever supplied by this
/// class.
/// </summary>
public sealed class SeasonPassAddOnsCatalogSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<CatalogPayload>
{
    private const string SourceName = "Carowinds Season Pass Add-Ons";

    private static readonly Uri PageUrl = new("https://www.sixflags.com/carowinds/season-pass-add-ons");

    public string SourceId => "season-pass-add-ons.official";

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
                    ? "1 season pass add-on confirmed."
                    : $"{products.Count} season pass add-ons confirmed.",
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
                "We couldn't check season pass add-ons right now.",
                exception));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
