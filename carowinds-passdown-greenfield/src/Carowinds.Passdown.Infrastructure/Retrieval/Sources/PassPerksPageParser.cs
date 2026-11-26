using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 32: reads the official Perks &amp; Play pages' structured CMS data (the Next.js flight payload
/// the pages render from). Structure only — interpretation (dates, visits, classification) is
/// <see cref="Domain.PassPerks.PassPerkText"/>.
/// <list type="bullet">
/// <item>Park page (<c>/carowinds/perks-and-play</c>): the month heading block titled "{Month} Perks"; the
/// first accordion after it (each item: <c>heading</c> = reward name, <c>children</c> = portable-text
/// paragraphs, the first bold paragraph being the reward's own date line); featured visit-based campaign
/// blocks before the heading (<c>imageTextBlock</c> whose text states a visit goal); and the loading
/// footnote. Year-round passholder benefits after the list are not Pass Perks and are not read.</item>
/// <item>Program FAQ (<c>/perks-and-play</c>): an accordion item's answer by its exact question.</item>
/// <item>Terms (<c>/pass-perks-terms</c>): server-rendered paragraphs, read as plain sentences.</item>
/// </list>
/// </summary>
internal static partial class PassPerksPageParser
{
    public sealed record RawPerk(string? Title, string? DateLine, IReadOnlyList<string> Paragraphs, bool IsMonthlyListing);

    public sealed record ParkPageResult(bool HadStructuredData, string? MonthHeading, IReadOnlyList<RawPerk> Items, string? LoadingNote);

    public static ParkPageResult ParseParkPage(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var payload = NextFlightPayload.Extract(html);
        if (payload is null)
        {
            return new ParkPageResult(false, null, [], null);
        }

        var loadingNote = LoadingNoteRegex().Match(payload) is { Success: true } note ? Unescape(note.Groups["text"].Value)?.TrimStart('*', ' ') : null;

        var heading = MonthHeadingRegex().Match(payload);
        if (!heading.Success)
        {
            return new ParkPageResult(true, null, [], loadingNote);
        }

        var items = new List<RawPerk>();

        // Featured campaigns sit before the month heading; only a block whose own text states a visit goal is a perk.
        foreach (var featured in NextFlightPayload.FindEnclosingObjects(payload[..heading.Index], "\"_type\":\"imageTextBlock\""))
        {
            if (!featured.TryGetProperty("content", out var content))
            {
                continue;
            }

            var title = NextFlightPayload.GetText(content, "title");
            var paragraphs = NextFlightPayload.GetBlockParagraphs(content, "richTextDescription");
            if (title is not null && FeaturedPerkRegex().IsMatch(title + " " + string.Join(" ", paragraphs)))
            {
                items.Add(new RawPerk(title, null, paragraphs, IsMonthlyListing: false));
            }
        }

        var accordionIndex = payload.IndexOf("\"_type\":\"accordionBlock\"", heading.Index, StringComparison.Ordinal);
        var nextHeadingIndex = payload.IndexOf("\"_type\":\"headingBlock\"", heading.Index + heading.Length, StringComparison.Ordinal);
        if (accordionIndex >= 0 && (nextHeadingIndex < 0 || accordionIndex < nextHeadingIndex) &&
            NextFlightPayload.FindEnclosingObjectAt(payload, accordionIndex) is { } accordion &&
            accordion.TryGetProperty("accordionItems", out var accordionItems) &&
            accordionItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in accordionItems.EnumerateArray())
            {
                items.Add(ReadAccordionItem(item));
            }
        }

        return new ParkPageResult(true, Unescape(heading.Groups["title"].Value), items, loadingNote);
    }

    /// <summary>The answer paragraphs of the FAQ item whose question is exactly <paramref name="question"/>.</summary>
    public static IReadOnlyList<string> FaqAnswer(string html, string question)
    {
        ArgumentNullException.ThrowIfNull(html);
        var payload = NextFlightPayload.Extract(html);
        if (payload is null)
        {
            return [];
        }

        var marker = "\"heading\":" + JsonSerializer.Serialize(question);
        foreach (var item in NextFlightPayload.FindEnclosingObjects(payload, marker))
        {
            var answer = ReadAccordionItem(item);
            var paragraphs = answer.DateLine is null ? answer.Paragraphs : [answer.DateLine, .. answer.Paragraphs];
            if (paragraphs.Count > 0)
            {
                return paragraphs;
            }
        }

        return [];
    }

    /// <summary>The terms page's paragraphs, list items and headings as plain text (tags removed, entities decoded), in order.</summary>
    public static IReadOnlyList<string> TermsParagraphs(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var main = MainRegex().Match(html) is { Success: true } m ? m.Value : html;
        return ParagraphRegex().Matches(main)
            .Select(p => WebUtility.HtmlDecode(TagRegex().Replace(p.Groups["inner"].Value, " ")))
            .Select(t => SpaceRegex().Replace(t, " ").Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static RawPerk ReadAccordionItem(JsonElement item)
    {
        var title = NextFlightPayload.GetText(item, "heading");
        string? dateLine = null;
        var paragraphs = new List<string>();
        if (item.TryGetProperty("children", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in blocks.EnumerateArray())
            {
                var (text, allBold, allLink) = ReadBlock(block);
                if (text is null || allLink)
                {
                    continue; // empty, or a bare link label such as "Book Now"
                }

                if (dateLine is null && paragraphs.Count == 0 && allBold)
                {
                    dateLine = text;
                    continue;
                }

                paragraphs.Add(text);
            }
        }

        return new RawPerk(title, dateLine, paragraphs, IsMonthlyListing: true);
    }

    private static (string? Text, bool AllBold, bool AllLink) ReadBlock(JsonElement block)
    {
        if (block.ValueKind != JsonValueKind.Object ||
            !block.TryGetProperty("children", out var spans) ||
            spans.ValueKind != JsonValueKind.Array)
        {
            return (null, false, false);
        }

        var linkKeys = new HashSet<string>(StringComparer.Ordinal);
        if (block.TryGetProperty("markDefs", out var defs) && defs.ValueKind == JsonValueKind.Array)
        {
            foreach (var def in defs.EnumerateArray())
            {
                if (NextFlightPayload.GetText(def, "_type") == "link" && NextFlightPayload.GetText(def, "_key") is { } key)
                {
                    linkKeys.Add(key);
                }
            }
        }

        var text = string.Empty;
        var allBold = true;
        var allLink = true;
        foreach (var span in spans.EnumerateArray())
        {
            if (span.ValueKind != JsonValueKind.Object || !span.TryGetProperty("text", out var t) || t.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var piece = t.GetString() ?? string.Empty;
            text += piece;
            if (piece.Trim().Length == 0)
            {
                continue;
            }

            var marks = span.TryGetProperty("marks", out var m) && m.ValueKind == JsonValueKind.Array
                ? m.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                : [];
            allBold &= marks.Contains("strong");
            allLink &= marks.Any(linkKeys.Contains);
        }

        var cleaned = NextFlightPayload.CleanText(text);
        return (cleaned, cleaned is not null && allBold, cleaned is not null && allLink);
    }

    private static string? Unescape(string jsonStringBody)
    {
        try
        {
            return NextFlightPayload.CleanText(JsonSerializer.Deserialize<string>("\"" + jsonStringBody + "\""));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"""title"":""(?<title>(?:January|February|March|April|May|June|July|August|September|October|November|December)(?:\s+\d{4})?\s+(?:Pass\s+)?Perks)""")]
    private static partial Regex MonthHeadingRegex();

    [GeneratedRegex(@"""text"":""(?<text>(?:[^""\\]|\\.)*?\bare loaded to\b(?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase)]
    private static partial Regex LoadingNoteRegex();

    [GeneratedRegex(@"\bvisit\s+(?:once|twice|thrice|\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten)\b|\b(?:\d{1,2}|two|three|four|five|six|seven|eight|nine|ten)\s+(?:qualifying\s+|separate\s+)?visits\b", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturedPerkRegex();

    [GeneratedRegex(@"<main\b[\s\S]*?</main>", RegexOptions.IgnoreCase)]
    private static partial Regex MainRegex();

    [GeneratedRegex(@"<(?<tag>p|li|h[1-6])\b[^>]*>(?<inner>[\s\S]*?)</\k<tag>>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}
