using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.HistoricalBackfill;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds park-hours adapter (Phase 3B; see docs/retrieval/park-hours-source.md for
/// the live investigation this is built from). Calls the same public, unauthenticated
/// CloudFront-hosted JSON endpoint the official park-hours page itself fetches client-side —
/// confirmed by a real browser network trace, not guessed — so this uses
/// <see cref="RetrievalStrategy.DirectApi"/> rather than Playwright (spec §6.4: prefer a
/// dependable direct endpoint once one is actually observed).
///
/// Phase 20C: also the current-year historical backfill's source (<see cref="IParkHoursHistorySource"/>)
/// — the same daily request and parsing, plus a structured "not retained" signal for past dates (see
/// docs/phase20c-historical-backfill.md). The feed's monthly form is deliberately not used for history:
/// observed live, it returns only today-forward dates (a past month returns just its last day).
///
/// Phase 22E.1: the main gate is the operating entry whose official <c>operatingTypeId</c> is 24 — the id
/// the official page's own CMS configuration assigns to "Park" and matches entries by. Seasonal days
/// label that same entry differently ("Winterfest", "New Years Eve"); matching the label "Park" alone
/// had reported those published days as not published. Also implements <see cref="IParkHoursScheduleSource"/>:
/// the feed's month form, one request per month, for forward published-schedule discovery
/// (docs/phase22e1-published-park-schedule-coverage.md).
/// </summary>
public sealed class ParkHoursSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<ParkHoursPayload>, IParkHoursHistorySource, IParkHoursScheduleSource
{
    private const int CarowindsParkId = 30;
    private const string ParkOperatingTypeName = "Park";

    /// <summary>Phase 22E.1: the official main-gate operating-type id (the page's CMS maps "Park" to 24).</summary>
    private const int ParkOperatingTypeId = 24;

    /// <summary>Phase 13D Part D: the same "Waterpark" operating-type entry documented (but previously left unread) in docs/retrieval/park-hours-source.md — present in the payload only on dates Carolina Harbor is separately open.</summary>
    private const string WaterparkOperatingTypeName = "Waterpark";

    /// <summary>Phase 22E.1: the official water-park operating-type id (the page's CMS maps "Waterpark" to 70).</summary>
    private const int WaterparkOperatingTypeId = 70;

    /// <summary>
    /// The human-facing official page (spec §5.1) — used as <see cref="SourceEvidence.SourceUrl"/>
    /// so evidence points somewhere a person can verify, rather than the CloudFront implementation
    /// detail. The exact endpoint actually queried is preserved in <see cref="SourceEvidence.TechnicalDetail"/>
    /// on failure instead.
    /// </summary>
    private static readonly Uri OfficialPageUrl = new("https://www.sixflags.com/carowinds/park-hours");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string SourceId => "park-hours.official";

    public SourceCategory Category => SourceCategory.ParkHours;

    public async Task<SourceResult<ParkHoursPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestedDate is not { } requestedDate)
        {
            throw new ArgumentException("Park hours retrieval requires a requested date.", nameof(request));
        }

        return (await RetrieveCoreAsync(requestedDate, cancellationToken).ConfigureAwait(false)).Result;
    }

    public async Task<HistoricalParkHoursResult> RetrieveHistoricalAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var (result, notFound) = await RetrieveCoreAsync(date, cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(clock.UtcNow).DateTime);
        return new HistoricalParkHoursResult(result, NotRetainedBySource: notFound && date < today);
    }

    /// <summary>The single retrieval; <c>NotFound</c> is true only for the feed's own JSON "Not Found" answer (HTTP 404 with its error body), never for a network failure.</summary>
    private async Task<(SourceResult<ParkHoursPayload> Result, bool NotFound)> RetrieveCoreAsync(DateOnly requestedDate, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(
            string.Create(CultureInfo.InvariantCulture, $"https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/{CarowindsParkId}?date={requestedDate:yyyyMMdd}"));
        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);

        RetrievalHttpResponse response;
        try
        {
            response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (Error(timeChecked, requestedDate, ex), false);
        }

        if (!response.IsSuccessStatusCode)
        {
            return (Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"GET {endpoint} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(response.Body)}")),
                IsFeedNotFound(response));
        }

        ParkHoursApiResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ParkHoursApiResponse>(response.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return (Error(timeChecked, requestedDate, ex), false);
        }

        if (parsed is null || parsed.Error is not null || parsed.Dates is not { Count: 1 })
        {
            var shapeDescription = parsed?.Error is { } apiError
                ? $"error='{apiError}'"
                : $"{parsed?.Dates?.Count ?? 0} date entries";
            return (Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"Unexpected response shape from {endpoint}: {shapeDescription}. Body: {Truncate(response.Body)}")), false);
        }

        var day = parsed.Dates[0];
        if (!TryParseResponseDate(day.Date, out var confirmedDate) || confirmedDate != requestedDate)
        {
            return (Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"Requested {requestedDate:yyyy-MM-dd} but the response confirmed '{day.Date}'."),
                "We couldn't confirm park hours for the requested date."), false);
        }

        return (EvaluateDay(day, requestedDate, timeChecked), false);
    }

    /// <summary>
    /// Phase 22E.1: one request for a whole month (<c>?date=yyyyMM</c>). Each listed entry is evaluated
    /// by exactly the same rules as a single-date request (<see cref="EvaluateDay"/>). Entries outside
    /// the requested month, unreadable dates and repeated dates are ignored — never substituted — and a
    /// date the response does not list is simply absent (never treated as closed). A transport failure,
    /// HTTP error, unreadable body or an empty <c>dates</c> list fails the whole month, so nothing is
    /// stored from it.
    /// </summary>
    public async Task<PublishedParkHoursMonth> RetrieveMonthAsync(int year, int month, CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(year, month, 1);
        var endpoint = new Uri(
            string.Create(CultureInfo.InvariantCulture, $"https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/{CarowindsParkId}?date={monthStart:yyyyMM}"));
        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        const string monthFailureDetail = "We couldn't check the published park schedule for this month right now.";

        PublishedParkHoursMonth Failed(Exception exception) =>
            new(year, month, [], Error(timeChecked, monthStart, exception, monthFailureDetail).Evidence);

        RetrievalHttpResponse response;
        try
        {
            response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Failed(new InvalidOperationException(
                $"GET {endpoint} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(response.Body)}"));
        }

        ParkHoursApiResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ParkHoursApiResponse>(response.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Failed(ex);
        }

        if (parsed is null || parsed.Error is not null || parsed.Dates is not { Count: > 0 })
        {
            var shapeDescription = parsed?.Error is { } apiError ? $"error='{apiError}'" : "no date entries";
            return Failed(new InvalidOperationException(
                $"Unexpected response shape from {endpoint}: {shapeDescription}. Body: {Truncate(response.Body)}"));
        }

        var results = new List<SourceResult<ParkHoursPayload>>();
        var seen = new HashSet<DateOnly>();
        foreach (var day in parsed.Dates)
        {
            if (!TryParseResponseDate(day.Date, out var date) || date.Year != year || date.Month != month || !seen.Add(date))
            {
                continue;
            }

            results.Add(EvaluateDay(day, date, timeChecked));
        }

        return new PublishedParkHoursMonth(year, month, results.OrderBy(r => r.Evidence.RequestedDate).ToList(), Failure: null);
    }

    /// <summary>Classifies one confirmed date entry: Closed, Verified (main-gate hours present), NotPublished (open flag but no main-gate hours), or Error (unparseable hours).</summary>
    private SourceResult<ParkHoursPayload> EvaluateDay(ParkHoursApiDate day, DateOnly requestedDate, DateTimeOffset timeChecked)
    {
        var confirmedDate = requestedDate;
        if (day.IsParkClosed)
        {
            return SourceResult<ParkHoursPayload>.WithoutPayload(
                SourceId,
                new SourceEvidence(
                    "Carowinds Park Hours",
                    OfficialPageUrl,
                    timeChecked,
                    RetrievalStatus.Closed,
                    $"Carowinds is officially closed on {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}.",
                    RetrievalStrategy.DirectApi,
                    requestedDate: requestedDate,
                    confirmedDate: confirmedDate));
        }

        var parkItem = FindOperating(day, ParkOperatingTypeId, ParkOperatingTypeName)?.Items?.FirstOrDefault();

        if (parkItem is null || string.IsNullOrWhiteSpace(parkItem.TimeFrom) || string.IsNullOrWhiteSpace(parkItem.TimeTo))
        {
            return SourceResult<ParkHoursPayload>.WithoutPayload(
                SourceId,
                new SourceEvidence(
                    "Carowinds Park Hours",
                    OfficialPageUrl,
                    timeChecked,
                    RetrievalStatus.NotPublished,
                    $"Hours for {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)} have not been published yet.",
                    RetrievalStrategy.DirectApi,
                    requestedDate: requestedDate,
                    confirmedDate: confirmedDate));
        }

        if (!TryParseClockTime(parkItem.TimeFrom, out var openingTime) || !TryParseClockTime(parkItem.TimeTo, out var closingTime))
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"Unparseable Park operating times '{parkItem.TimeFrom}'-'{parkItem.TimeTo}' for {requestedDate:yyyy-MM-dd}."));
        }

        var (waterparkOpening, waterparkClosing) = ReadWaterparkHours(day);

        return SourceResult<ParkHoursPayload>.Success(
            SourceId,
            new SourceEvidence(
                "Carowinds Park Hours",
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.Verified,
                $"Carowinds is open {FormatTime(openingTime)}–{FormatTime(closingTime)} on {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}.",
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate,
                confirmedDate: confirmedDate),
            new ParkHoursPayload(openingTime, closingTime, waterparkOpening, waterparkClosing));
    }

    /// <summary>
    /// Phase 22E.1: the entry with the official operating-type id, as the official page itself selects
    /// it; an entry without an id falls back to the conventional label, so older shapes still read.
    /// </summary>
    private static ParkHoursApiOperating? FindOperating(ParkHoursApiDate day, int operatingTypeId, string operatingTypeName) =>
        day.Operatings?.FirstOrDefault(o => o.OperatingTypeId == operatingTypeId)
        ?? day.Operatings?.FirstOrDefault(o => o.OperatingTypeId is null
            && string.Equals(o.OperatingTypeName, operatingTypeName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Phase 13D Part D: reads the optional second <c>"Waterpark"</c> operating-type entry the same
    /// response carries on dates Carolina Harbor is separately open — never guessed, never defaulted
    /// to a hardcoded season window. Deliberately lenient: an absent, blank, or unparseable waterpark
    /// entry yields <see langword="null"/>/<see langword="null"/> rather than failing the whole
    /// retrieval — the main gate's hours (already validated above) are the critical data this source
    /// exists for; the waterpark line is a same-payload bonus, not a second source of truth to guard
    /// as strictly.
    /// </summary>
    private static (TimeOnly? Opening, TimeOnly? Closing) ReadWaterparkHours(ParkHoursApiDate day)
    {
        var waterparkItem = FindOperating(day, WaterparkOperatingTypeId, WaterparkOperatingTypeName)?.Items?.FirstOrDefault();

        if (waterparkItem is null
            || !TryParseClockTime(waterparkItem.TimeFrom, out var opening)
            || !TryParseClockTime(waterparkItem.TimeTo, out var closing))
        {
            return (null, null);
        }

        return (opening, closing);
    }

    private SourceResult<ParkHoursPayload> Error(
        DateTimeOffset timeChecked,
        DateOnly requestedDate,
        Exception exception,
        string userFacingDetail = "We couldn't check park hours right now.") =>
        SourceResult<ParkHoursPayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                "Carowinds Park Hours",
                OfficialPageUrl,
                timeChecked,
                RetrievalStrategy.DirectApi,
                userFacingDetail,
                exception,
                requestedDate));

    private static bool TryParseResponseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseClockTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static string FormatTime(TimeOnly time) => time.ToString("h:mm tt", PresentationCulture.UnitedStates);

    /// <summary>The feed's explicit "no such date" answer: HTTP 404 whose body is its JSON error document (observed: <c>{"error":"Not Found",…}</c>).</summary>
    private static bool IsFeedNotFound(RetrievalHttpResponse response)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && string.Equals(error.GetString(), "Not Found", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
