using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds exact-date "Celebrate America! Bring-A-Friend Duo Offer" adapter (Phase 13C;
/// see docs/retrieval/special-ticket-sources.md for the live investigation this is built from).
/// Calls the same accesso Passport <c>getkeywordcalendar</c> endpoint <see cref="DatedFastLaneSource"/>
/// uses, for the <c>SUBAF50</c> keyword — discovered live on 2026-08-27 from the catalog card's own
/// `data-accesso-keyword` attribute (see <c>fixtures/promotions/daily-tickets-page.html</c>), then
/// independently confirmed against the calendar endpoint itself with a direct anonymous request.
///
/// The live response's single package (`"Celebrate America! Bring-A-Friend Duo Offer "`) prices one
/// passholder-accompanied friend ticket — the <em>per-person</em> figure within the catalog card's
/// own "2 friends for $50" bundle framing, not a bundle total (Phase 13C Part H/I worked example;
/// <see cref="DatedBringAFriendDuoProduct.PerPersonPrice"/> is named accordingly, never repurposing a
/// bundle-total field). Structurally mirrors <see cref="DatedFastLaneSource"/> exactly (primary
/// single-day request, horizon fallback only when that comes back empty) — same keyword-calendar
/// contract, independently re-verified for this keyword rather than assumed from Fast Lane's.
///
/// Ordinary (non-Duo) Bring-A-Friend has no analogous dated keyword: its catalog "Buy Now" button
/// uses the shared <c>SnapWizard</c> keyword (the same one <see cref="DatedAdmissionSource"/>'s
/// underlying checkout uses for plain Daily Admission), and its passholder-exclusive discount price
/// is only ever shown after an authenticated passholder login inside that checkout flow — not
/// reachable by any anonymous official request this app is allowed to make (CLAUDE.md prohibits
/// entering credentials). This is a confirmed, documented limitation, not an oversight — see
/// docs/retrieval/special-ticket-sources.md.
/// </summary>
public sealed class DatedBringAFriendDuoSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<DatedBringAFriendDuoPayload>
{
    private const string Keyword = "SUBAF50";

    /// <summary>Directly observed live for this keyword (docs/retrieval/special-ticket-sources.md) — matches dated admission's convention, independently re-confirmed rather than copied.</summary>
    private const string DisplayZeroCapacity = "1";

    private const int HorizonPaddingDays = 120;

    private static readonly Uri OfficialPageUrl = new("https://www.sixflags.com/carowinds/daily-tickets");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string SourceId => "dated-bring-a-friend-duo.official";

    public SourceCategory Category => SourceCategory.BringAFriendDuo;

    public async Task<SourceResult<DatedBringAFriendDuoPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestedDate is not { } requestedDate)
        {
            throw new ArgumentException("Dated Bring-A-Friend Duo retrieval requires a requested date.", nameof(request));
        }

        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var today = DateOnly.FromDateTime(timeChecked.DateTime);

        if (requestedDate < today)
        {
            return Unavailable(timeChecked, requestedDate, "That date has already passed.");
        }

        var primaryBody = AccessoKeywordCalendarRequest.BuildBody(Keyword, DisplayZeroCapacity, requestedDate, requestedDate);
        var primaryResult = await CallCalendarAsync(primaryBody, timeChecked, requestedDate, cancellationToken).ConfigureAwait(false);
        if (primaryResult.IsError)
        {
            return primaryResult.Error!;
        }

        var primaryService = primaryResult.Service!;
        if (string.Equals(primaryService.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            var entries = primaryService.Dates?.Entries ?? [];
            if (entries.Count != 1)
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException(
                        $"Single-day getkeywordcalendar request for {requestedDate:yyyy-MM-dd} returned {entries.Count} date entries; expected exactly 1."));
            }

            if (!DateOnly.TryParseExact(entries[0].Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var confirmedDate))
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException($"Unparseable date '{entries[0].Date}' in calendar response."));
            }

            if (confirmedDate != requestedDate)
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException(
                        $"Requested {requestedDate:yyyy-MM-dd} but the response confirmed '{entries[0].Date}'."),
                    "We couldn't confirm Bring-A-Friend Duo availability for the requested date.");
            }

            var products = BuildProducts(entries[0].Packages?.P ?? [], requestedDate);
            if (products is null)
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException($"Matched date entry for {requestedDate:yyyy-MM-dd} had no usable packages."));
            }

            return SourceResult<DatedBringAFriendDuoPayload>.Success(
                SourceId,
                new SourceEvidence(
                    "Carowinds Dated Bring-A-Friend Duo",
                    OfficialPageUrl,
                    timeChecked,
                    RetrievalStatus.Verified,
                    $"Bring-A-Friend Duo confirmed for {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}.",
                    RetrievalStrategy.DirectApi,
                    requestedDate: requestedDate,
                    confirmedDate: requestedDate),
                new DatedBringAFriendDuoPayload(products));
        }

        var windowEnd = requestedDate > today.AddDays(HorizonPaddingDays) ? requestedDate : today.AddDays(HorizonPaddingDays);
        var horizonBody = AccessoKeywordCalendarRequest.BuildBody(Keyword, DisplayZeroCapacity, today, windowEnd);
        var horizonResult = await CallCalendarAsync(horizonBody, timeChecked, requestedDate, cancellationToken).ConfigureAwait(false);
        if (horizonResult.IsError)
        {
            return horizonResult.Error!;
        }

        var horizonService = horizonResult.Service!;

        // Phase 19B: a limited-time offer that has ended (live-confirmed for this keyword on
        // 2026-10-15) has no dates anywhere from today onward — a successful "not currently
        // published" answer, never a retrieval failure.
        if (horizonService.IsNoDatesFound)
        {
            return NotPublished(timeChecked, requestedDate);
        }

        if (!string.Equals(horizonService.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"SERVICE.status was '{horizonService.Status}' (error_msg='{horizonService.ErrorMessage}') for a horizon window that should have included today."),
                "We couldn't check Bring-A-Friend Duo availability right now.");
        }

        var horizonEntries = horizonService.Dates?.Entries ?? [];
        var parsedHorizonDates = new List<DateOnly>(horizonEntries.Count);
        foreach (var entry in horizonEntries)
        {
            if (!DateOnly.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var entryDate))
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException($"Unparseable date '{entry.Date}' in horizon calendar response."));
            }

            parsedHorizonDates.Add(entryDate);
        }

        if (parsedHorizonDates.Count == 0)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException("SERVICE.status was 'OK' but the horizon window's DATES.D contained no entries."));
        }

        var maxDate = parsedHorizonDates.Max();
        return requestedDate > maxDate
            ? NotPublished(timeChecked, requestedDate)
            : Unavailable(timeChecked, requestedDate, DefaultUnavailableDetail(requestedDate));
    }

    private async Task<CalendarCallResult> CallCalendarAsync(
        string body,
        DateTimeOffset timeChecked,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        RetrievalHttpResponse response;
        try
        {
            response = await httpClient
                .PostJsonAsync(AccessoKeywordCalendarRequest.Endpoint, body, AccessoKeywordCalendarRequest.Headers, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CalendarCallResult.Failed(Error(timeChecked, requestedDate, ex));
        }

        if (!response.IsSuccessStatusCode)
        {
            return CalendarCallResult.Failed(Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"POST {AccessoKeywordCalendarRequest.Endpoint} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(response.Body)}")));
        }

        AccessoKeywordCalendarApiResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AccessoKeywordCalendarApiResponse>(response.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return CalendarCallResult.Failed(Error(timeChecked, requestedDate, ex));
        }

        if (parsed?.Service is null)
        {
            return CalendarCallResult.Failed(Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"Unexpected response shape from {AccessoKeywordCalendarRequest.Endpoint}: no SERVICE object. Body: {Truncate(response.Body)}")));
        }

        return CalendarCallResult.Succeeded(parsed.Service);
    }

    /// <summary>
    /// One <see cref="DatedBringAFriendDuoProduct"/> per package. Every live response observed for
    /// this keyword returned exactly one package per date, so — unlike
    /// <see cref="DatedAdmissionSource"/> — there is no by-name grouping/comparison-price pairing to
    /// do. Never reads the date-level `lowest_price` aggregate field (spec §5.4).
    /// </summary>
    private static List<DatedBringAFriendDuoProduct>? BuildProducts(
        IReadOnlyList<AccessoKeywordCalendarPackage> packages,
        DateOnly applicableDate)
    {
        if (packages.Count == 0)
        {
            return null;
        }

        var products = new List<DatedBringAFriendDuoProduct>(packages.Count);
        foreach (var package in packages)
        {
            if (string.IsNullOrWhiteSpace(package.Id) ||
                string.IsNullOrWhiteSpace(package.Name) ||
                !decimal.TryParse(package.CustomerType?.RetailAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                return null;
            }

            products.Add(new DatedBringAFriendDuoProduct(package.Id, package.Name.Trim(), price, null, null, applicableDate));
        }

        return products;
    }

    private static string DefaultUnavailableDetail(DateOnly requestedDate) =>
        $"Bring-A-Friend Duo is not available for {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}.";

    private SourceResult<DatedBringAFriendDuoPayload> NotPublished(DateTimeOffset timeChecked, DateOnly requestedDate) =>
        SourceResult<DatedBringAFriendDuoPayload>.WithoutPayload(
            SourceId,
            new SourceEvidence(
                "Carowinds Dated Bring-A-Friend Duo",
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.NotPublished,
                $"Bring-A-Friend Duo for {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)} has not been published yet.",
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    private SourceResult<DatedBringAFriendDuoPayload> Unavailable(DateTimeOffset timeChecked, DateOnly requestedDate, string detail) =>
        SourceResult<DatedBringAFriendDuoPayload>.WithoutPayload(
            SourceId,
            new SourceEvidence(
                "Carowinds Dated Bring-A-Friend Duo",
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.Unavailable,
                detail,
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    private SourceResult<DatedBringAFriendDuoPayload> Error(
        DateTimeOffset timeChecked,
        DateOnly requestedDate,
        Exception exception,
        string userFacingDetail = "We couldn't check Bring-A-Friend Duo availability right now.") =>
        SourceResult<DatedBringAFriendDuoPayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                "Carowinds Dated Bring-A-Friend Duo",
                OfficialPageUrl,
                timeChecked,
                RetrievalStrategy.DirectApi,
                userFacingDetail,
                exception,
                requestedDate));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";

    private readonly struct CalendarCallResult
    {
        private CalendarCallResult(AccessoKeywordCalendarService? service, SourceResult<DatedBringAFriendDuoPayload>? error)
        {
            Service = service;
            Error = error;
        }

        public AccessoKeywordCalendarService? Service { get; }

        public SourceResult<DatedBringAFriendDuoPayload>? Error { get; }

        public bool IsError => Error is not null;

        public static CalendarCallResult Succeeded(AccessoKeywordCalendarService service) => new(service, null);

        public static CalendarCallResult Failed(SourceResult<DatedBringAFriendDuoPayload> error) => new(null, error);
    }
}
