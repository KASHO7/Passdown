using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds season-pass catalog adapter (Phase 3F2; see
/// docs/retrieval/catalog-source-inventory.md for the live investigation this is built from, and
/// its own class doc comment for a narrow live revalidation performed immediately before writing
/// this adapter). Fetches `https://www.sixflags.com/carowinds/season-passes` with a plain HTTP GET
/// and reuses <see cref="ProductCardHtmlParser"/> — proven live for this exact page during Phase
/// 3F1 and reconfirmed unchanged here — rather than re-implementing card extraction. Uses
/// <see cref="RetrievalStrategy.Html"/>: real product names/prices are server-rendered into the
/// response body (Phase 3F1), so no Playwright is needed and none was needed for this
/// revalidation either.
/// </summary>
public sealed class SeasonPassCatalogSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<CatalogPayload>
{
    private const string SourceName = "Carowinds Season Passes";

    private static readonly Uri PageUrl = new("https://www.sixflags.com/carowinds/season-passes");

    public string SourceId => "season-passes.official";

    public SourceCategory Category => SourceCategory.SeasonPasses;

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
                    ? "1 season pass confirmed."
                    : $"{products.Count} season passes confirmed.",
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
                "We couldn't check season passes right now.",
                exception));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
