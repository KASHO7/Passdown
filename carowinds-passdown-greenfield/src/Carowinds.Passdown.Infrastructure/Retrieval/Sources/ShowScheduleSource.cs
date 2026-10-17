using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Domain.Shows;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 21C official show-schedule adapter (docs/phase21c-official-show-schedules.md). Reads the
/// <c>shows</c> array of the same public FIMS operating-hours JSON the official park-hours page and
/// <see cref="ParkHoursSource"/> use — the daily form, one request per requested date, plain HTTP
/// (<see cref="RetrievalStrategy.DirectApi"/>). In the app both adapters share one coalescing HTTP
/// client, so a date's hours and shows checked together cost one request.
/// <para>Outcomes, all observed live on 2026-10-16:</para>
/// <list type="bullet">
/// <item><b>Verified</b> — at least one listed show has published times; only those shows are kept. An
/// open day with published park hours and an empty <c>shows</c> array is a confirmed empty schedule
/// (Verified, no shows).</item>
/// <item><b>Closed</b> — <c>isParkClosed</c>.</item>
/// <item><b>NotPublished</b> — shows listed but none has times (e.g. far-future dates), or no park
/// hours and no shows yet.</item>
/// <item><b>Unavailable</b> — a past date: never requested (no established historical source).</item>
/// <item><b>Error</b> — network/HTTP/JSON/shape failures, a date mismatch, or any unparseable time.
/// Errors never replace last-known-good data (<c>DatedSourceRegistration</c>).</item>
/// </list>
/// The feed publishes no cancellation, duration, or "changed" flag, so this adapter never claims one.
/// </summary>
public sealed class ShowScheduleSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<ShowSchedulePayload>
{
    private const int CarowindsParkId = 30;
    private const string SourceName = "Carowinds Show Schedule";
    private const string ParkOperatingTypeName = "Park";

    /// <summary>The human-facing official page; the queried endpoint is kept in technical detail on failure.</summary>
    private static readonly Uri OfficialPageUrl = new("https://www.sixflags.com/carowinds/shows");

    private static readonly string[] TimeFormats = ["hh:mm tt", "h:mm tt", "HH:mm", "H:mm"];

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string SourceId => "show-schedule.official";

    public SourceCategory Category => SourceCategory.ShowSchedule;

    public async Task<SourceResult<ShowSchedulePayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestedDate is not { } requestedDate)
        {
            throw new ArgumentException("Show schedule retrieval requires a requested date.", nameof(request));
        }

        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var today = DateOnly.FromDateTime(timeChecked.DateTime);
        if (requestedDate < today)
        {
            return WithoutPayload(timeChecked, requestedDate, RetrievalStatus.Unavailable, "Past show schedules are not retrieved.", confirmedDate: null);
        }

        var endpoint = new Uri(
            string.Create(CultureInfo.InvariantCulture, $"https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/{CarowindsParkId}?date={requestedDate:yyyyMMdd}"));

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
            return Error(timeChecked, requestedDate, new InvalidOperationException(
                $"GET {endpoint} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(response.Body)}"));
        }

        ShowScheduleApiResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ShowScheduleApiResponse>(response.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Error(timeChecked, requestedDate, ex);
        }

        if (parsed is null || parsed.Error is not null || parsed.Dates is not { Count: 1 })
        {
            var shape = parsed?.Error is { } apiError ? $"error='{apiError}'" : $"{parsed?.Dates?.Count ?? 0} date entries";
            return Error(timeChecked, requestedDate, new InvalidOperationException(
                $"Unexpected response shape from {endpoint}: {shape}. Body: {Truncate(response.Body)}"));
        }

        var day = parsed.Dates[0];
        if (!DateOnly.TryParseExact(day.Date, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var confirmedDate)
            || confirmedDate != requestedDate)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException($"Requested {requestedDate:yyyy-MM-dd} but the response confirmed '{day.Date}'."),
                "We couldn't confirm the show schedule for the requested date.");
        }

        var dateText = requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates);
        if (day.IsParkClosed)
        {
            return WithoutPayload(timeChecked, requestedDate, RetrievalStatus.Closed, $"Carowinds is closed on {dateText}; no shows are scheduled.", confirmedDate);
        }

        if (!TryReadShows(day.Shows ?? [], out var shows, out var listedWithoutTimes, out var parseFailure))
        {
            return Error(timeChecked, requestedDate, new InvalidOperationException(
                $"Unparseable show schedule for {requestedDate:yyyy-MM-dd}: {parseFailure}"));
        }

        if (shows.Count > 0)
        {
            return SourceResult<ShowSchedulePayload>.Success(
                SourceId,
                Evidence(timeChecked, requestedDate, RetrievalStatus.Verified, $"{shows.Count} {(shows.Count == 1 ? "show" : "shows")} scheduled on {dateText}.", confirmedDate),
                new ShowSchedulePayload(shows, listedWithoutTimes));
        }

        // Nothing performs: a confirmed empty schedule only when the day itself is published (park hours
        // present) and the feed lists no show at all; shows listed without times are "not published".
        if (listedWithoutTimes == 0 && HasPublishedParkHours(day))
        {
            return SourceResult<ShowSchedulePayload>.Success(
                SourceId,
                Evidence(timeChecked, requestedDate, RetrievalStatus.Verified, $"No shows are scheduled on {dateText}.", confirmedDate),
                new ShowSchedulePayload([], 0));
        }

        return WithoutPayload(timeChecked, requestedDate, RetrievalStatus.NotPublished, $"Show times for {dateText} have not been published.", confirmedDate);
    }

    /// <summary>
    /// Normalizes the feed's shows: entries with no times are counted, not kept; duplicate entries for
    /// one identity are merged (times unioned, venues combined); times are de-duplicated and put in
    /// operating-day order. Any time token that does not parse fails the whole date — a partly read
    /// schedule is never presented as the official one.
    /// </summary>
    private static bool TryReadShows(
        IReadOnlyList<ShowScheduleApiShow> rawShows,
        out IReadOnlyList<ScheduledShow> shows,
        out int listedWithoutTimes,
        out string? failure)
    {
        var merged = new List<(string Id, string Name, List<string> Venues, List<TimeOnly> Times)>();
        listedWithoutTimes = 0;
        failure = null;
        shows = [];

        foreach (var raw in rawShows)
        {
            var name = ShowIdentity.CleanDisplayText(raw.ShowName);
            if (name is null)
            {
                failure = "a show entry has no name";
                return false;
            }

            var venues = new List<string>();
            var times = new List<TimeOnly>();
            foreach (var item in raw.Items ?? [])
            {
                foreach (var token in (item.Times ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!TimeOnly.TryParseExact(token, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    {
                        failure = $"time '{token}' for '{name}'";
                        return false;
                    }

                    times.Add(time);
                }

                if (ShowIdentity.CleanDisplayText(item.AssignmentLocation) is { } venue && !venues.Contains(venue, StringComparer.OrdinalIgnoreCase))
                {
                    venues.Add(venue);
                }
            }

            if (times.Count == 0)
            {
                listedWithoutTimes++;
                continue;
            }

            var id = ShowIdentity.Resolve(raw.ExtLocationId, raw.ShowId, name, venues.FirstOrDefault());
            var existing = merged.FindIndex(entry => entry.Id == id);
            if (existing >= 0)
            {
                merged[existing].Times.AddRange(times);
                merged[existing].Venues.AddRange(venues.Where(v => !merged[existing].Venues.Contains(v, StringComparer.OrdinalIgnoreCase)));
            }
            else
            {
                merged.Add((id, name, venues, times));
            }
        }

        shows = merged
            .Select(entry => new ScheduledShow(
                entry.Id,
                entry.Name,
                entry.Venues.Count == 0 ? null : string.Join(" / ", entry.Venues),
                ShowTimes.Normalize(entry.Times)))
            .OrderBy(show => ShowTimes.OperatingDayOffset(show.Times[0]))
            .ThenBy(show => show.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }

    private static bool HasPublishedParkHours(ShowScheduleApiDate day) =>
        day.Operatings?.FirstOrDefault(o => string.Equals(o.OperatingTypeName, ParkOperatingTypeName, StringComparison.OrdinalIgnoreCase))
            ?.Items?.FirstOrDefault() is { } park
        && !string.IsNullOrWhiteSpace(park.TimeFrom)
        && !string.IsNullOrWhiteSpace(park.TimeTo);

    private SourceEvidence Evidence(DateTimeOffset timeChecked, DateOnly requestedDate, RetrievalStatus status, string detail, DateOnly? confirmedDate) =>
        new(SourceName, OfficialPageUrl, timeChecked, status, detail, RetrievalStrategy.DirectApi, requestedDate: requestedDate, confirmedDate: confirmedDate);

    private SourceResult<ShowSchedulePayload> WithoutPayload(DateTimeOffset timeChecked, DateOnly requestedDate, RetrievalStatus status, string detail, DateOnly? confirmedDate) =>
        SourceResult<ShowSchedulePayload>.WithoutPayload(SourceId, Evidence(timeChecked, requestedDate, status, detail, confirmedDate));

    private SourceResult<ShowSchedulePayload> Error(
        DateTimeOffset timeChecked,
        DateOnly requestedDate,
        Exception exception,
        string userFacingDetail = "We couldn't check the show schedule right now.") =>
        SourceResult<ShowSchedulePayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(SourceName, OfficialPageUrl, timeChecked, RetrievalStrategy.DirectApi, userFacingDetail, exception, requestedDate));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
