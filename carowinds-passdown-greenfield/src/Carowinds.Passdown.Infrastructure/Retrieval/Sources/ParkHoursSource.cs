using System.Globalization;
using System.Text.Json;
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
/// </summary>
public sealed class ParkHoursSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<ParkHoursPayload>
{
    private const int CarowindsParkId = 30;
    private const string ParkOperatingTypeName = "Park";

    /// <summary>Phase 13D Part D: the same "Waterpark" operating-type entry documented (but previously left unread) in docs/retrieval/park-hours-source.md — present in the payload only on dates Carolina Harbor is separately open.</summary>
    private const string WaterparkOperatingTypeName = "Waterpark";

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

        var endpoint = new Uri(
            $"https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/{CarowindsParkId}?date={requestedDate:yyyyMMdd}");
        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);

        RetrievalHttpResponse response;
        try
        {
            response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(timeChecked, requestedDate, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"GET {endpoint} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(response.Body)}"));
        }

        ParkHoursApiResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ParkHoursApiResponse>(response.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Error(timeChecked, requestedDate, ex);
        }

        if (parsed is null || parsed.Error is not null || parsed.Dates is not { Count: 1 })
        {
            var shapeDescription = parsed?.Error is { } apiError
                ? $"error='{apiError}'"
                : $"{parsed?.Dates?.Count ?? 0} date entries";
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"Unexpected response shape from {endpoint}: {shapeDescription}. Body: {Truncate(response.Body)}"));
        }

        var day = parsed.Dates[0];
        if (!TryParseResponseDate(day.Date, out var confirmedDate) || confirmedDate != requestedDate)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"Requested {requestedDate:yyyy-MM-dd} but the response confirmed '{day.Date}'."),
                "We couldn't confirm park hours for the requested date.");
        }

        if (day.IsParkClosed)
        {
            return SourceResult<ParkHoursPayload>.WithoutPayload(
                SourceId,
                new SourceEvidence(
                    "Carowinds Park Hours",
                    OfficialPageUrl,
                    timeChecked,
                    RetrievalStatus.Closed,
                    $"Carowinds is officially closed on {requestedDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}.",
                    RetrievalStrategy.DirectApi,
                    requestedDate: requestedDate,
                    confirmedDate: confirmedDate));
        }

        var parkItem = day.Operatings?
            .FirstOrDefault(o => string.Equals(o.OperatingTypeName, ParkOperatingTypeName, StringComparison.OrdinalIgnoreCase))
            ?.Items?.FirstOrDefault();

        if (parkItem is null || string.IsNullOrWhiteSpace(parkItem.TimeFrom) || string.IsNullOrWhiteSpace(parkItem.TimeTo))
        {
            return SourceResult<ParkHoursPayload>.WithoutPayload(
                SourceId,
                new SourceEvidence(
                    "Carowinds Park Hours",
                    OfficialPageUrl,
                    timeChecked,
                    RetrievalStatus.NotPublished,
                    $"Hours for {requestedDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)} have not been published yet.",
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
                $"Carowinds is open {FormatTime(openingTime)}–{FormatTime(closingTime)} on {requestedDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}.",
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate,
                confirmedDate: confirmedDate),
            new ParkHoursPayload(openingTime, closingTime, waterparkOpening, waterparkClosing));
    }

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
        var waterparkItem = day.Operatings?
            .FirstOrDefault(o => string.Equals(o.OperatingTypeName, WaterparkOperatingTypeName, StringComparison.OrdinalIgnoreCase))
            ?.Items?.FirstOrDefault();

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

    private static string FormatTime(TimeOnly time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
