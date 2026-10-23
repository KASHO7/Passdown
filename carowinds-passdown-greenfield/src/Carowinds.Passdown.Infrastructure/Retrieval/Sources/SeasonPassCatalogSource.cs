using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds Season Pass &amp; Membership catalog adapter (Phase 3F2; rebuilt in Phase 25 —
/// see docs/phase25-memberships-season-passes.md). The official site replaced the former
/// <c>/carowinds/season-passes</c> page (which now redirects here) with
/// <c>/carowinds/memberships-and-season-passes</c>: one card per tier (Silver/Gold/Prestige), each
/// with a "Choose your way" choice between a one-time Season Pass and a recurring Membership. The new
/// cards no longer carry the <c>productv2-card-price</c> element the Phase 3F1 card parser required,
/// so every check failed with a parse error and the source showed as unavailable.
///
/// One plain HTTP GET (<see cref="RetrievalStrategy.Html"/>, no browser): the tier data is
/// server-rendered as structured page data (<see cref="PassTierPageParser"/>) and interpreted into
/// separate Season Pass, renewal, Membership and initiation-fee offers
/// (<see cref="PassOfferInterpreter"/>). The source id and source name are unchanged so the source's
/// freshness state and the catalog's absence scoping continue across the redesign.
///
/// Outcomes:
/// <list type="bullet">
/// <item>transport failure, non-success status, unreadable page data, tier cards rendered with no
/// readable tier data, or tiers with no verifiable offer at all → <see cref="RetrievalStatus.Error"/>
/// (nothing is reconciled; last-known-good stays);</item>
/// <item>some offers verified, some not → <see cref="RetrievalStatus.Verified"/> with a
/// "Partially verified" detail, and the unverifiable offers passed as
/// <see cref="CatalogPayload.UnconfirmedProductKeys"/> so their last verified values are kept;</item>
/// <item>a well-formed page with no pass tiers and no tier cards → a verified empty catalog.</item>
/// </list>
/// </summary>
public sealed class SeasonPassCatalogSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<CatalogPayload>
{
    private const string SourceName = "Carowinds Season Passes";

    /// <summary>The official redesigned page. The former <c>/carowinds/season-passes</c> address redirects here.</summary>
    public static readonly Uri PageUrl = new("https://www.sixflags.com/carowinds/memberships-and-season-passes");

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

        ParsedPassPage page;
        try
        {
            page = PassTierPageParser.Parse(response.Body);
        }
        catch (FormatException ex)
        {
            return Error(timeChecked, ex);
        }

        if (!page.HasStructuredData)
        {
            return Error(timeChecked, new FormatException(
                "The page did not contain the expected embedded Season Pass/Membership data — the page format may have changed."));
        }

        if (page.Tiers.Count == 0)
        {
            if (page.HasLegacyCardMarkup)
            {
                return Error(timeChecked, new FormatException(
                    "Pass cards are shown on the page but their tier data could not be found — the page format may have changed."));
            }

            return SourceResult<CatalogPayload>.Success(
                SourceId,
                Evidence(timeChecked, "No Season Passes or Memberships are currently listed.", technicalDetail: null),
                new CatalogPayload([]));
        }

        var interpretation = PassOfferInterpreter.Interpret(page.Tiers);
        if (interpretation.Products.Count == 0)
        {
            return Error(timeChecked, new FormatException(
                "No Season Pass or Membership offer could be verified. " + string.Join(" ", interpretation.Warnings)));
        }

        var detail = Summarize(interpretation);
        var technicalDetail = interpretation.Warnings.Count == 0 ? null : string.Join(Environment.NewLine, interpretation.Warnings);

        return SourceResult<CatalogPayload>.Success(
            SourceId,
            Evidence(timeChecked, detail, technicalDetail),
            new CatalogPayload(interpretation.Products) { UnconfirmedProductKeys = interpretation.UnconfirmedProductKeys });
    }

    /// <summary>
    /// "Verified 3 Season Passes, 2 renewal offers, 2 Memberships (1 not available). Official note: *Plus
    /// processing fee of up to $9.99 &amp; applicable taxes." — prefixed "Partially verified" (with what
    /// could not be read) whenever any listed offer was unverifiable, so the source is never reported as
    /// fully verified with required fields missing.
    /// </summary>
    private static string Summarize(PassOfferInterpretation interpretation)
    {
        var offers = interpretation.Products
            .Select(product => (Product: product, Offer: PassOffer.FromKey(product.Key.ToString())))
            .Where(entry => entry.Offer is not null)
            .ToList();

        int Count(PassPriceType type) => offers.Count(entry => entry.Offer!.PriceType == type && entry.Product.CurrentPriceAmount is not null);
        var unavailable = offers.Count(entry => entry.Product.CurrentPriceAmount is null);

        var parts = new List<string>
        {
            Plural(Count(PassPriceType.NewPurchase), "Season Pass", "Season Passes"),
            Plural(Count(PassPriceType.Renewal), "renewal offer", "renewal offers"),
            Plural(Count(PassPriceType.MonthlyRate), "Membership", "Memberships"),
        };
        var summary = string.Join(", ", parts);
        if (unavailable > 0)
        {
            summary += $" ({unavailable} not available)";
        }

        var unconfirmed = interpretation.UnconfirmedProductKeys
            .Select(key => PassOffer.FromKey(key.ToString())?.DisplayName)
            .Where(name => name is not null)
            .ToList();
        var text = unconfirmed.Count == 0
            ? $"Verified {summary}."
            : $"Partially verified: {summary}. Could not verify {string.Join(", ", unconfirmed)} — showing the last verified price.";

        if (interpretation.FeesDisclosures.Count > 0)
        {
            text += " Official note: " + string.Join(" ", interpretation.FeesDisclosures);
        }

        return text;
    }

    private static string Plural(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";

    private static SourceEvidence Evidence(DateTimeOffset timeChecked, string detail, string? technicalDetail) => new(
        SourceName,
        PageUrl,
        timeChecked,
        RetrievalStatus.Verified,
        detail,
        RetrievalStrategy.Html,
        requestedDate: null,
        technicalDetail: technicalDetail);

    private SourceResult<CatalogPayload> Error(DateTimeOffset timeChecked, Exception exception) =>
        SourceResult<CatalogPayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                SourceName,
                PageUrl,
                timeChecked,
                RetrievalStrategy.Html,
                "We couldn't check season passes and memberships right now.",
                exception));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
