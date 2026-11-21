using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 31: reads the structured CMS data the official sixflags.com pages embed for their own
/// rendering (Next.js "flight" payload, pushed as <c>self.__next_f.push([1,"…"])</c> string chunks).
/// That data carries the same fields the visible cards are rendered from (offer titles, prices,
/// payment options, terms, event start/end dates), so reading it is the "official structured data"
/// preference (CLAUDE.md retrieval rules) over scraping rendered markup.
/// <para>
/// Not a general RSC decoder: it decodes the chunks, then, for a marker such as
/// <c>"_type":"product_v2"</c>, returns the JSON object that encloses each occurrence by a
/// string-aware brace scan limited to the marker's own payload row (JSON rows never contain a raw
/// newline). Values that are RSC references (<c>"$7a"</c>) are unavailable and read as null; an
/// escaped literal dollar (<c>"$$69"</c>) reads as <c>"$69"</c>.
/// </para>
/// </summary>
internal static partial class NextFlightPayload
{
    /// <summary>The decoded payload, or null when the page carries no flight chunks.</summary>
    public static string? Extract(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var builder = new StringBuilder();
        foreach (Match match in PushRegex().Matches(html))
        {
            try
            {
                builder.Append(JsonSerializer.Deserialize<string>("\"" + match.Groups["chunk"].Value + "\""));
            }
            catch (JsonException)
            {
                // A damaged chunk is skipped; objects inside it are simply not found.
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Every distinct JSON object enclosing an occurrence of <paramref name="marker"/>, parsed and cloned.</summary>
    public static IReadOnlyList<JsonElement> FindEnclosingObjects(string payload, string marker)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var results = new List<JsonElement>();
        var seenStarts = new HashSet<int>();
        var index = payload.IndexOf(marker, StringComparison.Ordinal);
        while (index >= 0)
        {
            var start = EnclosingObjectStart(payload, index);
            if (start >= 0 && seenStarts.Add(start))
            {
                var end = MatchingBrace(payload, start);
                if (end > start)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(payload.AsMemory(start, end - start + 1));
                        results.Add(document.RootElement.Clone());
                    }
                    catch (JsonException)
                    {
                        // Not a parseable object (truncated chunk) — skipped, reported by the caller as unreadable if it matters.
                    }
                }
            }

            index = payload.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
        }

        return results;
    }

    private static int EnclosingObjectStart(string payload, int markerIndex)
    {
        var rowStart = payload.LastIndexOf('\n', markerIndex) + 1;
        var stack = new Stack<int>();
        var inString = false;
        for (var i = rowStart; i < markerIndex; i++)
        {
            var c = payload[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    stack.Push(i);
                    break;
                case '}' when stack.Count > 0:
                    stack.Pop();
                    break;
            }
        }

        return inString || stack.Count == 0 ? -1 : stack.Peek();
    }

    private static int MatchingBrace(string payload, int start)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < payload.Length; i++)
        {
            var c = payload[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
            else if (c == '\n')
            {
                return -1; // left the row: truncated object
            }
        }

        return -1;
    }

    /// <summary>A string property's official text, with RSC references treated as unavailable and "$$" unescaped; null when blank.</summary>
    public static string? GetText(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return CleanText(value.GetString());
    }

    public static string? CleanText(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        if (raw.StartsWith('$'))
        {
            if (!raw.StartsWith("$$", StringComparison.Ordinal))
            {
                return null; // an RSC reference to another chunk, not text
            }

            raw = raw[1..];
        }

        var text = raw.Replace("$$", "$", StringComparison.Ordinal).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Plain text of a Sanity portable-text block array (paragraph text joined by spaces).</summary>
    public static string? GetBlockText(JsonElement element, string property)
    {
        var paragraphs = GetBlockParagraphs(element, property);
        return paragraphs.Count == 0 ? null : string.Join(" ", paragraphs);
    }

    /// <summary>Phase 31B: each paragraph of a Sanity portable-text block array, in order (blank ones skipped).</summary>
    public static IReadOnlyList<string> GetBlockParagraphs(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var blocks) ||
            blocks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var paragraphs = new List<string>();
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object ||
                !block.TryGetProperty("children", out var children) ||
                children.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var text = string.Concat(children.EnumerateArray()
                .Select(child => child.ValueKind == JsonValueKind.Object && child.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()
                    : null));
            text = CleanText(text);
            if (text is not null)
            {
                paragraphs.Add(text);
            }
        }

        return paragraphs;
    }

    [GeneratedRegex(@"self\.__next_f\.push\(\[1,""(?<chunk>(?:[^""\\]|\\.)*)""\]\)")]
    private static partial Regex PushRegex();
}
