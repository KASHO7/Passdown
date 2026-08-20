using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds exact-date Fast Lane adapter (Phase 3D; see
/// docs/retrieval/dated-fast-lane-source.md for the live investigation this is built from). Calls
/// the same accesso Passport `getkeywordcalendar` endpoint <see cref="DatedAdmissionSource"/> uses
/// (Phase 3C), independently re-confirmed live for the `FastLaneOnly` keyword — not assumed from
/// Phase 3C. Uses <see cref="RetrievalStrategy.DirectApi"/> for the same reason Phase 3C does: a
/// bare POST with no browser, no cookies, and no cart/session bootstrap succeeds.
///
/// Unlike <see cref="DatedAdmissionSource"/>'s single wide-window search, this adapter makes a
/// primary single-day request for the requested date and only falls back to a wider horizon probe
/// when that primary request comes back empty. This was chosen specifically because it gives a
/// genuine requested/confirmed date mismatch a directly reachable <c>Error</c> outcome (this
/// phase's status-behavior requirement), while still costing only one call in the common case
/// where the requested date is currently published — see docs for the full rationale and the
/// evidence that both endpoints' response envelopes are otherwise identical.
/// </summary>
public sealed class DatedFastLaneSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<DatedFastLanePayload>
{
    private const string Keyword = "FastLaneOnly";

    /// <summary>Directly observed for this keyword (docs/retrieval/dated-fast-lane-source.md) — different from dated admission's `"1"`, not copied from it.</summary>
    private const string DisplayZeroCapacity = "0";

    /// <summary>Same padding rationale as <see cref="DatedAdmissionSource"/>, independently re-justified for this keyword by a live `2026-08-20`→`2027-01-17` horizon probe (docs/retrieval/dated-fast-lane-source.md).</summary>
    private const int HorizonPaddingDays = 120;

    private static readonly Uri OfficialPageUrl = new("https://www.sixflags.com/carowinds/fast-lane");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string SourceId => "dated-fast-lane.official";

    public SourceCategory Category => SourceCategory.DatedFastLane;

    public async Task<SourceResult<DatedFastLanePayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestedDate is not { } requestedDate)
        {
            throw new ArgumentException("Dated Fast Lane retrieval requires a requested date.", nameof(request));
        }

        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var today = DateOnly.FromDateTime(timeChecked.DateTime);

        // Independently re-confirmed live for FastLaneOnly (docs/retrieval/dated-fast-lane-source.md):
        // a date before "today" returns the same FAILED "No dates found" shape as every other
        // unavailable case, so this is short-circuited the same way Phase 3C's admission adapter is.
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
                // Never observed live — defensive rejection per spec §5.4's "never substitute": a
                // successful response that doesn't actually confirm the date we asked for must not
                // be treated as data for that date.
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException(
                        $"Requested {requestedDate:yyyy-MM-dd} but the response confirmed '{entries[0].Date}'."),
                    "We couldn't confirm Fast Lane availability for the requested date.");
            }

            var products = BuildProducts(entries[0].Packages?.P ?? [], requestedDate);
            if (products is null)
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException($"Matched date entry for {requestedDate:yyyy-MM-dd} had no usable packages."));
            }

            return SourceResult<DatedFastLanePayload>.Success(
                SourceId,
                new SourceEvidence(
                    "Carowinds Dated Fast Lane",
                    OfficialPageUrl,
                    timeChecked,
                    RetrievalStatus.Verified,
                    $"Fast Lane confirmed for {requestedDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}.",
                    RetrievalStrategy.DirectApi,
                    requestedDate: requestedDate,
                    confirmedDate: requestedDate),
                new DatedFastLanePayload(products));
        }

        // Primary request found nothing for this exact date. That alone doesn't say whether the
        // date is beyond the published horizon or just a closed/gap date within it — a second,
        // wider request is needed to tell (docs/retrieval/dated-fast-lane-source.md).
        var windowEnd = requestedDate > today.AddDays(HorizonPaddingDays) ? requestedDate : today.AddDays(HorizonPaddingDays);
        var horizonBody = AccessoKeywordCalendarRequest.BuildBody(Keyword, DisplayZeroCapacity, today, windowEnd);
        var horizonResult = await CallCalendarAsync(horizonBody, timeChecked, requestedDate, cancellationToken).ConfigureAwait(false);
        if (horizonResult.IsError)
        {
            return horizonResult.Error!;
        }

        var horizonService = horizonResult.Service!;
        if (!string.Equals(horizonService.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            // The horizon window always spans from "today" through at least HorizonPaddingDays, and
            // every live check performed during investigation returned at least one date within
            // that span. A totally empty result here has never been observed, so it is treated as an
            // unexpected shape rather than guessed at as a specific business status.
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"SERVICE.status was '{horizonService.Status}' (error_msg='{horizonService.ErrorMessage}') for a horizon window that should have included today."),
                "We couldn't check Fast Lane availability right now.");
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

    /// <summary>
    /// Shared transport/parse handling for both the primary and horizon calls: transport failure,
    /// non-success status code, malformed JSON, and a missing <c>SERVICE</c> object all become the
    /// same <see cref="RetrievalStatus.Error"/> shape regardless of which call produced them.
    /// </summary>
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
    /// One <see cref="DatedFastLaneProduct"/> per package, read directly with no by-name grouping —
    /// unlike <see cref="DatedAdmissionSource"/>, no live `FastLaneOnly` response has ever shown two
    /// packages sharing one name on one date, so there is nothing to group (docs/retrieval/dated-fast-lane-source.md).
    /// <see cref="DatedFastLaneProduct.ComparisonPrice"/> and <see cref="DatedFastLaneProduct.PriceQualifier"/>
    /// are always passed as <c>null</c> for the same reason: no field in this response ever supplies
    /// either one. Never reads the date-level `lowest_price` aggregate field (always `"0.00"` in
    /// every live response observed — spec §5.4's "Starting at" rule).
    /// </summary>
    private static List<DatedFastLaneProduct>? BuildProducts(
        IReadOnlyList<AccessoKeywordCalendarPackage> packages,
        DateOnly applicableDate)
    {
        if (packages.Count == 0)
        {
            return null;
        }

        var products = new List<DatedFastLaneProduct>(packages.Count);
        foreach (var package in packages)
        {
            if (string.IsNullOrWhiteSpace(package.Id) ||
                string.IsNullOrWhiteSpace(package.Name) ||
                !decimal.TryParse(package.CustomerType?.RetailAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                return null;
            }

            products.Add(new DatedFastLaneProduct(package.Id, package.Name, price, null, null, applicableDate));
        }

        return products;
    }

    private static string DefaultUnavailableDetail(DateOnly requestedDate) =>
        $"Fast Lane is not available for {requestedDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}.";

    private SourceResult<DatedFastLanePayload> NotPublished(DateTimeOffset timeChecked, DateOnly requestedDate) =>
        SourceResult<DatedFastLanePayload>.WithoutPayload(
            SourceId,
            new SourceEvidence(
                "Carowinds Dated Fast Lane",
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.NotPublished,
                $"Fast Lane for {requestedDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)} has not been published yet.",
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    private SourceResult<DatedFastLanePayload> Unavailable(DateTimeOffset timeChecked, DateOnly requestedDate, string detail) =>
        SourceResult<DatedFastLanePayload>.WithoutPayload(
            SourceId,
            new SourceEvidence(
                "Carowinds Dated Fast Lane",
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.Unavailable,
                detail,
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    private SourceResult<DatedFastLanePayload> Error(
        DateTimeOffset timeChecked,
        DateOnly requestedDate,
        Exception exception,
        string userFacingDetail = "We couldn't check Fast Lane availability right now.") =>
        SourceResult<DatedFastLanePayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                "Carowinds Dated Fast Lane",
                OfficialPageUrl,
                timeChecked,
                RetrievalStrategy.DirectApi,
                userFacingDetail,
                exception,
                requestedDate));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";

    /// <summary>Result of one raw <c>getkeywordcalendar</c> call: either a parsed <see cref="AccessoKeywordCalendarService"/> to interpret, or an already-built <see cref="RetrievalStatus.Error"/> result to return as-is.</summary>
    private readonly struct CalendarCallResult
    {
        private CalendarCallResult(AccessoKeywordCalendarService? service, SourceResult<DatedFastLanePayload>? error)
        {
            Service = service;
            Error = error;
        }

        public AccessoKeywordCalendarService? Service { get; }

        public SourceResult<DatedFastLanePayload>? Error { get; }

        public bool IsError => Error is not null;

        public static CalendarCallResult Succeeded(AccessoKeywordCalendarService service) => new(service, null);

        public static CalendarCallResult Failed(SourceResult<DatedFastLanePayload> error) => new(null, error);
    }
}
