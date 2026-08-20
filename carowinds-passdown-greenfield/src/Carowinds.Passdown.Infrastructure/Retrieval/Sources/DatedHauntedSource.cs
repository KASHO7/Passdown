using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds exact-date haunted-attraction (SCarowinds) adapter (Phase 3E; see
/// docs/retrieval/dated-haunted-source.md for the live investigation this is built from). Calls the
/// same accesso Passport `getkeywordcalendar` endpoint <see cref="DatedAdmissionSource"/> (Phase
/// 3C) and <see cref="DatedFastLaneSource"/> (Phase 3D) use, independently re-confirmed live for
/// the `HAPWizard` keyword — not assumed from either prior phase. Uses
/// <see cref="RetrievalStrategy.DirectApi"/> for the same reason both do: a bare POST with no
/// browser, no cookies, and no cart/session bootstrap succeeds.
///
/// Structurally mirrors <see cref="DatedFastLaneSource"/>'s primary-single-day-then-horizon-probe
/// design rather than <see cref="DatedAdmissionSource"/>'s single wide-window search, because live
/// evidence for `HAPWizard` independently reproduces Fast Lane's shape: one package per name per
/// date (never admission's duplicate-name pair), and the same genuine need for a directly reachable
/// date-mismatch <c>Error</c> path. This adapter also has to correctly report a real "season hasn't
/// started yet" state — the horizon-probe step doubles as that classification with no extra
/// special-casing (see docs).
/// </summary>
public sealed class DatedHauntedSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<DatedHauntedPayload>
{
    private const string Keyword = "HAPWizard";

    /// <summary>Directly observed for this keyword (docs/retrieval/dated-haunted-source.md) — same value as `FastLaneOnly`, independently confirmed here rather than copied.</summary>
    private const string DisplayZeroCapacity = "0";

    /// <summary>Same padding rationale as the other two adapters, independently re-justified for this keyword by a live `2026-08-20`→`2027-01-17` horizon probe (docs/retrieval/dated-haunted-source.md).</summary>
    private const int HorizonPaddingDays = 120;

    private static readonly Uri OfficialPageUrl = new("https://www.sixflags.com/carowinds/events/scarowinds");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string SourceId => "dated-haunted.official";

    public SourceCategory Category => SourceCategory.HauntedAttractions;

    public async Task<SourceResult<DatedHauntedPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestedDate is not { } requestedDate)
        {
            throw new ArgumentException("Dated haunted-attraction retrieval requires a requested date.", nameof(request));
        }

        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var today = DateOnly.FromDateTime(timeChecked.DateTime);

        // Independently re-confirmed live for HAPWizard (docs/retrieval/dated-haunted-source.md): a
        // date before "today" returns the same FAILED "No dates found" shape as every other
        // unavailable case, so this is short-circuited the same way the other two adapters are.
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
                    "We couldn't confirm haunted-attraction availability for the requested date.");
            }

            var products = BuildProducts(entries[0].Packages?.P ?? [], requestedDate);
            if (products is null)
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException($"Matched date entry for {requestedDate:yyyy-MM-dd} had no usable packages."));
            }

            return SourceResult<DatedHauntedPayload>.Success(
                SourceId,
                new SourceEvidence(
                    "Carowinds Dated Haunted Attractions",
                    OfficialPageUrl,
                    timeChecked,
                    RetrievalStatus.Verified,
                    $"Haunted attractions confirmed for {requestedDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}.",
                    RetrievalStrategy.DirectApi,
                    requestedDate: requestedDate,
                    confirmedDate: requestedDate),
                new DatedHauntedPayload(products));
        }

        // Primary request found nothing for this exact date. That alone doesn't say whether the
        // date is beyond the published horizon, a dark night inside an active season, or before the
        // season has started at all — a second, wider request is needed to tell (docs).
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
            // The horizon window always spans from "today" through at least HorizonPaddingDays. If
            // SCarowinds is fully off-season for the *entire* window (e.g. checked well before any
            // season announcement), this branch is reachable in practice — unlike the other two
            // adapters, where every live check during investigation always returned something. Since
            // that specific scenario was not itself directly observed live (investigation happened
            // to occur while the *current* season was already both announced and orderable), it is
            // still treated as an unexpected shape rather than guessed at as a specific business
            // status (CLAUDE.md "never invent data") — see docs' limitations section.
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"SERVICE.status was '{horizonService.Status}' (error_msg='{horizonService.ErrorMessage}') for the horizon window."),
                "We couldn't check haunted-attraction availability right now.");
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
    /// One <see cref="DatedHauntedProduct"/> per package, read directly with no by-name grouping —
    /// like <see cref="DatedFastLaneSource"/> and unlike <see cref="DatedAdmissionSource"/>, no live
    /// `HAPWizard` response has ever shown two packages sharing one name on one date, so there is
    /// nothing to group (docs/retrieval/dated-haunted-source.md). No product name is hardcoded here
    /// — whatever names the response actually contains flow through (spec's "do not hard-code a
    /// product list"). <see cref="DatedHauntedProduct.ComparisonPrice"/> and
    /// <see cref="DatedHauntedProduct.PriceQualifier"/> are always passed as <c>null</c>: no field in
    /// this response ever supplies either one. Never reads the date-level `lowest_price` aggregate
    /// field (always `"0.00"` in every live response observed — spec §5.4's "Starting at" rule).
    /// </summary>
    private static List<DatedHauntedProduct>? BuildProducts(
        IReadOnlyList<AccessoKeywordCalendarPackage> packages,
        DateOnly applicableDate)
    {
        if (packages.Count == 0)
        {
            return null;
        }

        var products = new List<DatedHauntedProduct>(packages.Count);
        foreach (var package in packages)
        {
            if (string.IsNullOrWhiteSpace(package.Id) ||
                string.IsNullOrWhiteSpace(package.Name) ||
                !decimal.TryParse(package.CustomerType?.RetailAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                return null;
            }

            products.Add(new DatedHauntedProduct(package.Id, package.Name, price, null, null, applicableDate));
        }

        return products;
    }

    private static string DefaultUnavailableDetail(DateOnly requestedDate) =>
        $"Haunted attractions are not available for {requestedDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}.";

    private SourceResult<DatedHauntedPayload> NotPublished(DateTimeOffset timeChecked, DateOnly requestedDate) =>
        SourceResult<DatedHauntedPayload>.WithoutPayload(
            SourceId,
            new SourceEvidence(
                "Carowinds Dated Haunted Attractions",
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.NotPublished,
                $"Haunted attractions for {requestedDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)} have not been published yet.",
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    private SourceResult<DatedHauntedPayload> Unavailable(DateTimeOffset timeChecked, DateOnly requestedDate, string detail) =>
        SourceResult<DatedHauntedPayload>.WithoutPayload(
            SourceId,
            new SourceEvidence(
                "Carowinds Dated Haunted Attractions",
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.Unavailable,
                detail,
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    private SourceResult<DatedHauntedPayload> Error(
        DateTimeOffset timeChecked,
        DateOnly requestedDate,
        Exception exception,
        string userFacingDetail = "We couldn't check haunted-attraction availability right now.") =>
        SourceResult<DatedHauntedPayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                "Carowinds Dated Haunted Attractions",
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
        private CalendarCallResult(AccessoKeywordCalendarService? service, SourceResult<DatedHauntedPayload>? error)
        {
            Service = service;
            Error = error;
        }

        public AccessoKeywordCalendarService? Service { get; }

        public SourceResult<DatedHauntedPayload>? Error { get; }

        public bool IsError => Error is not null;

        public static CalendarCallResult Succeeded(AccessoKeywordCalendarService service) => new(service, null);

        public static CalendarCallResult Failed(SourceResult<DatedHauntedPayload> error) => new(null, error);
    }
}
