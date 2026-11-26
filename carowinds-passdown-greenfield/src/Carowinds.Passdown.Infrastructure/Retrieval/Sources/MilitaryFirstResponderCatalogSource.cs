using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Promotions;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds Military/First Responder discounted-admission catalog adapter (Phase 13C; see
/// docs/retrieval/special-ticket-sources.md for the live investigation this is built from). Fetches
/// `https://www.sixflags.com/carowinds/military-discounts` with a plain HTTP GET — same anonymous,
/// no-cookie, no-login access every other catalog page uses; the page reachable via the site's own
/// "Military &amp; First Responders" nav tab, discovered live rather than guessed.
///
/// Tracked under its own <see cref="SourceCategory.MilitaryFirstResponder"/> — never
/// <see cref="SourceCategory.DatedAdmissionTickets"/> — so an eligibility-restricted discount can
/// never overwrite or be confused with ordinary Daily Admission (Phase 13C Part J/K). The page's own
/// bullet copy ("Exclusive discounts for Military (active &amp; retired) &amp; First Responders...
/// Purchase up to 6 tickets for family &amp; friends. Valid on the date selected.") is not
/// individually parsed — this adapter preserves eligibility by keeping this product under its own
/// distinct category/display name rather than inventing a structured eligibility field the source
/// doesn't expose per-card; the page's separate prose section (ID.me verification requirement) is
/// page-level, not attached to any one card, so it is not fabricated onto this product either (spec
/// §5.2 "never invent data").
///
/// Deliberately does NOT populate <see cref="Application.Catalog.ObservedProduct.DateSpecific"/>/
/// <c>ApplicableDate</c>: this is a non-date-specific "Starting From" catalog price, same convention
/// as every other catalog adapter.
/// </summary>
public sealed class MilitaryFirstResponderCatalogSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<CatalogPayload>
{
    private const string SourceName = "Carowinds Military & First Responder Tickets";

    private static readonly Uri PageUrl = new("https://www.sixflags.com/carowinds/military-discounts");

    public string SourceId => "military-first-responder.official";

    public SourceCategory Category => SourceCategory.MilitaryFirstResponder;

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

        MilitaryPageParseResult page;
        try
        {
            page = MilitaryFirstResponderHtmlParser.ParsePage(response.Body);
        }
        catch (FormatException ex)
        {
            return Error(timeChecked, ex);
        }

        // Phase 31B: a page that shows official Buy Now offers but yields no readable card is a layout
        // change, never proof the offers ended — the November 2026 redesign was misread this way and
        // the 2027 Military Gold Pass was retired after two "successful" empty checks. Likewise a card
        // that cannot be read or identified could be any product, so nothing is reconciled.
        if (page.Cards.Count == 0 && (page.HasPurchaseMarkup || page.UnreadableCardKeywords.Count > 0 || page.UnidentifiedUnreadableCardCount > 0))
        {
            return Error(timeChecked, new FormatException(
                "Military & First Responder offers are shown on the page but none could be read — the page format may have changed."));
        }

        if (page.UnidentifiedUnreadableCardCount > 0)
        {
            return Error(timeChecked, new FormatException(
                $"{page.UnidentifiedUnreadableCardCount} Military & First Responder offer(s) on the page could not be read or identified — the page format may have changed."));
        }

        var products = CatalogCardMapper.ToObservedProducts(page.Cards, Category, SourceName);
        var unconfirmed = page.UnreadableCardKeywords
            .Select(keyword => CatalogProductKey.FromOfficialId(Category, keyword))
            .ToList();

        var detail = products.Count == 1
            ? "1 Military/First Responder offer confirmed."
            : $"{products.Count} Military/First Responder offers confirmed.";
        if (unconfirmed.Count > 0)
        {
            detail = $"Partially verified: {detail} {unconfirmed.Count} offer(s) could not be read and keep their last verified details.";
        }

        return SourceResult<CatalogPayload>.Success(
            SourceId,
            new SourceEvidence(
                SourceName,
                PageUrl,
                timeChecked,
                RetrievalStatus.Verified,
                detail,
                RetrievalStrategy.Html,
                requestedDate: null),
            new CatalogPayload(products)
            {
                UnconfirmedProductKeys = unconfirmed,
                ProductDetails = OfficialProductDetailsReader.TryRead(response.Body, PageUrl, timeChecked, OfficialProductChannel.MilitaryFirstResponder),
            });
    }

    private SourceResult<CatalogPayload> Error(DateTimeOffset timeChecked, Exception exception) =>
        SourceResult<CatalogPayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                SourceName,
                PageUrl,
                timeChecked,
                RetrievalStrategy.Html,
                "We couldn't check Military/First Responder tickets right now.",
                exception));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
