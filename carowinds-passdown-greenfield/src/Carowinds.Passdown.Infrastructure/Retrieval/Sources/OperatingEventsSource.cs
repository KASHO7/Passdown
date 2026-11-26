using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Domain.Seasons;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 31: official Carowinds event schedule adapter. One plain GET of the events listing
/// (<c>/carowinds/events</c>) plus each seasonal event page — the three standing seasonal pages
/// (SCarowinds, Oktoberfest, WinterFest) and any other seasonal event the listing links to — capped
/// at <see cref="MaxEventPages"/> so a listing change can never turn into a crawl. Event pages matter
/// because an event that has ended (SCarowinds after November) drops off the listing while its page
/// keeps its official dates, and because a page can state "Select Dates" when the listing does not.
/// <para>
/// The listing is required: if it cannot be fetched, or no longer carries any schedule data at all
/// (a structural change), the result is <see cref="RetrievalStatus.Error"/> and the stored schedule
/// is untouched. A single event page failing is only a warning.
/// </para>
/// </summary>
public sealed class OperatingEventsSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<OperatingEventsPayload>
{
    private const string SourceName = "Carowinds Events";
    public const int MaxEventPages = 6;

    public static readonly Uri ListingUrl = new("https://www.sixflags.com/carowinds/events");

    private static readonly string[] StandingSeasonalPages = ["scarowinds", "oktoberfest", "winterfest"];

    public string SourceId => "operating-events.official";

    public SourceCategory Category => SourceCategory.OperatingEvents;

    public async Task<SourceResult<OperatingEventsPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);

        RetrievalHttpResponse listing;
        try
        {
            listing = await httpClient.GetAsync(ListingUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(timeChecked, ListingUrl, ex);
        }

        if (!listing.IsSuccessStatusCode)
        {
            return Error(timeChecked, ListingUrl, new InvalidOperationException($"GET {ListingUrl} returned {(int)listing.StatusCode} {listing.StatusCode}."));
        }

        var listingResult = OperatingEventsPageParser.Parse(listing.Body, ListingUrl, timeChecked, isEventPage: false);
        if (!listingResult.HadScheduleData)
        {
            return Error(timeChecked, ListingUrl, new FormatException(
                "The official events listing no longer contains recognizable event schedule data — the page structure may have changed."));
        }

        var events = new List<OfficialOperatingEvent>(listingResult.Events);
        var warnings = new List<string>();

        var slugs = StandingSeasonalPages
            .Concat(listingResult.EventPageLinks)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxEventPages);
        foreach (var slug in slugs)
        {
            var pageUrl = new Uri(OperatingEventsPageParser.EventsBaseUrl, slug);
            try
            {
                var response = await httpClient.GetAsync(pageUrl, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    warnings.Add($"{pageUrl} returned {(int)response.StatusCode}.");
                    continue;
                }

                var pageResult = OperatingEventsPageParser.Parse(response.Body, pageUrl, timeChecked, isEventPage: true);
                if (pageResult.Events.Count == 0)
                {
                    warnings.Add($"{pageUrl} had no readable event dates.");
                }

                events.AddRange(pageResult.Events);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"{pageUrl} could not be checked: {ex.Message}");
            }
        }

        var combined = Combine(events);
        var seasonalCount = combined.Count(e => OperatingEventKinds.IsSeasonal(e.Kind));
        return SourceResult<OperatingEventsPayload>.Success(
            SourceId,
            new SourceEvidence(
                SourceName,
                ListingUrl,
                timeChecked,
                RetrievalStatus.Verified,
                seasonalCount == 1 ? "1 seasonal event schedule confirmed." : $"{seasonalCount} seasonal event schedules confirmed.",
                RetrievalStrategy.Html,
                requestedDate: null,
                technicalDetail: warnings.Count == 0 ? null : string.Join(" ", warnings)),
            new OperatingEventsPayload(combined) { Warnings = warnings });
    }

    /// <summary>
    /// Merges the listing entry and event-page entry of the same occurrence: the widest official
    /// range, "select dates" if either source says so, the "Select …" wording when one exists, and the
    /// event page as source link.
    /// </summary>
    internal static IReadOnlyList<OfficialOperatingEvent> Combine(IEnumerable<OfficialOperatingEvent> events) =>
        events
            .GroupBy(e => e.EventId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var label = group.Select(e => e.DateLabel).FirstOrDefault(l => l is not null && l.Contains("Select", StringComparison.OrdinalIgnoreCase))
                    ?? group.Select(e => e.DateLabel).FirstOrDefault(l => l is not null);
                var pageSource = group.Select(e => e.SourceUrl).FirstOrDefault(u => u != ListingUrl) ?? first.SourceUrl;
                return first with
                {
                    StartDate = group.Min(e => e.StartDate),
                    EndDate = group.Max(e => e.EndDate),
                    IsSelectDates = group.Any(e => e.IsSelectDates),
                    DateLabel = label,
                    SourceUrl = pageSource,
                };
            })
            .OrderBy(e => e.StartDate)
            .ToList();

    private SourceResult<OperatingEventsPayload> Error(DateTimeOffset timeChecked, Uri pageUrl, Exception exception) =>
        SourceResult<OperatingEventsPayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                SourceName,
                pageUrl,
                timeChecked,
                RetrievalStrategy.Html,
                "We couldn't check the official event schedule right now.",
                exception));
}
