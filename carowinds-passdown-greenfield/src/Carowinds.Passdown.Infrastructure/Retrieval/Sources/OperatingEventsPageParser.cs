using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Carowinds.Passdown.Domain.Seasons;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 31: extracts official event occurrences from the Carowinds events listing and event pages.
/// The authoritative dates are each event document's own <c>showTimesSchedule[].startDate/endDate</c>
/// (ISO dates); the official date wording comes from the listing's <c>overrideText</c> or, on an event
/// page, from its visible "Select Days/Dates/Nights, …" line. A "Select …" wording marks the
/// occurrence as select-dates only. Nothing is inferred from a month or a name: an entry without a
/// parseable start and end date is skipped.
/// </summary>
internal static partial class OperatingEventsPageParser
{
    public const string ScheduleMarker = "\"showTimesSchedule\":";

    public sealed record ParseResult(IReadOnlyList<OfficialOperatingEvent> Events, bool HadScheduleData, IReadOnlyList<string> EventPageLinks);

    public static ParseResult Parse(string html, Uri pageUrl, DateTimeOffset checkedAt, bool isEventPage)
    {
        ArgumentNullException.ThrowIfNull(html);

        var payload = NextFlightPayload.Extract(html);
        if (payload is null)
        {
            return new ParseResult([], HadScheduleData: false, []);
        }

        var objects = NextFlightPayload.FindEnclosingObjects(payload, ScheduleMarker);
        var visibleSelectLabel = isEventPage ? FindVisibleSelectDateLabel(html) : null;

        var events = new List<OfficialOperatingEvent>();
        var links = new List<string>();
        foreach (var obj in objects)
        {
            var title = NextFlightPayload.GetText(obj, "title");
            if (title is null || !TryReadRange(obj, out var start, out var end))
            {
                continue;
            }

            var kind = OperatingEventKinds.FromOfficialTitle(title);
            var label = NextFlightPayload.GetText(obj, "overrideText") ?? visibleSelectLabel;
            var isSelect = label is not null && SelectWordingRegex().IsMatch(label);
            var officialId = NextFlightPayload.GetText(obj, "_id") ?? NextFlightPayload.GetText(obj, "fimsId");

            // Seasonal occurrences are identified by kind + start date so the listing entry and the
            // event page (which carry different CMS ids) merge into one occurrence.
            var eventId = OperatingEventKinds.IsSeasonal(kind)
                ? string.Create(CultureInfo.InvariantCulture, $"{kind}:{start:yyyy-MM-dd}")
                : officialId ?? string.Create(CultureInfo.InvariantCulture, $"event:{title.ToLowerInvariant()}:{start:yyyy-MM-dd}");

            var link = NextFlightPayload.GetText(obj, "link");
            var sourceUrl = pageUrl;
            if (!isEventPage && link is not null && OperatingEventKinds.IsSeasonal(kind) && SlugRegex().IsMatch(link))
            {
                links.Add(link);
                sourceUrl = new Uri(EventsBaseUrl, link);
            }

            events.Add(new OfficialOperatingEvent(eventId, title, kind, start, end, isSelect, label?.TrimStart(':', ' '), sourceUrl, checkedAt, checkedAt));
        }

        return new ParseResult(events, HadScheduleData: objects.Count > 0, links);
    }

    public static readonly Uri EventsBaseUrl = new("https://www.sixflags.com/carowinds/events/");

    private static bool TryReadRange(JsonElement obj, out DateOnly start, out DateOnly end)
    {
        start = default;
        end = default;
        if (!obj.TryGetProperty("showTimesSchedule", out var schedules) || schedules.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        DateOnly? min = null;
        DateOnly? max = null;
        foreach (var schedule in schedules.EnumerateArray())
        {
            if (TryIsoDate(NextFlightPayload.GetText(schedule, "startDate"), out var s) &&
                TryIsoDate(NextFlightPayload.GetText(schedule, "endDate"), out var e) &&
                e >= s)
            {
                min = min is null || s < min ? s : min;
                max = max is null || e > max ? e : max;
            }
        }

        if (min is null || max is null)
        {
            return false;
        }

        start = min.Value;
        end = max.Value;
        return true;
    }

    private static bool TryIsoDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>The first visible text node on an event page that states select dates ("Select Days, November 7-22, 2026").</summary>
    private static string? FindVisibleSelectDateLabel(string html)
    {
        foreach (Match match in TextNodeRegex().Matches(html))
        {
            var text = WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
            if (text.Length is > 0 and < 120 && SelectLabelRegex().IsMatch(text))
            {
                return text;
            }
        }

        return null;
    }

    [GeneratedRegex(@"\bSelect\s+(?:Days|Dates|Nights|Public Operating Days)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SelectWordingRegex();

    [GeneratedRegex(@"^:?\s*Select\s+(?:Days|Dates|Nights)\b.*\d{4}", RegexOptions.IgnoreCase)]
    private static partial Regex SelectLabelRegex();

    [GeneratedRegex(@">(?<text>[^<>]{3,160})<")]
    private static partial Regex TextNodeRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]*$")]
    private static partial Regex SlugRegex();
}
