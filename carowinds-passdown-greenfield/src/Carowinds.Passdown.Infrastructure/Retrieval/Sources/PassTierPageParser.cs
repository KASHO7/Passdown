using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 25: extracts the tier cards of the official redesigned
/// <c>sixflags.com/carowinds/memberships-and-season-passes</c> page (see
/// docs/phase25-memberships-season-passes.md). The page is a Next.js App Router page whose Sanity CMS
/// content streams inline as React Server Components "flight" data
/// (<c>self.__next_f.push([1,"…"])</c> script chunks, server-rendered into the plain HTTP response).
/// Each tier is one <c>product_v2</c> document whose <c>paymentOptions</c> array carries every
/// purchase option of that tier as <b>structured, labeled fields</b> — option title ("Season Pass",
/// "Membership"), price, price frequency ("/ea*", "/mo*"), crossed-out reference price
/// (<c>pricePretense</c>), subtitle ("$20 initiation fee due today"), savings line ("Renewals Just
/// $89", "Auto-renews After 12mo"), locked text ("Not Available") and checkout keyword.
///
/// Reading these fields is preferred over the rendered buttons: the rendered card only names the
/// checkout keyword of the option selected by default, and the same amounts are repeated in a
/// comparison table — the structured fields relate every amount to its label unambiguously.
///
/// Pure transport/markup shape, like <see cref="ProductCardHtmlParser"/>: no category, identity or
/// price-meaning decisions (those are <see cref="PassOfferInterpreter"/>'s).
/// </summary>
internal static class PassTierPageParser
{
    private const string ProductTypeMarker = "\"_type\":\"product_v2\"";
    private const string LegacyCardMarker = "data-ab=\"productv2-card\"";

    /// <summary>How far back from a <c>product_v2</c> type marker an enclosing object start is searched for.</summary>
    private const int MaxObjectStartCandidates = 64;

    /// <summary>Generous cap on one tier document's size (observed: ~15–30 KB each).</summary>
    private const int MaxObjectLength = 400_000;

    private static readonly Regex FlightChunk = new(
        @"self\.__next_f\.push\(\[1,(""(?:[^""\\]|\\.)*"")\]\)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(10));

    /// <summary>
    /// Parses <paramref name="html"/>. Throws <see cref="FormatException"/> when a <c>product_v2</c>
    /// document is present but cannot be read (truncated/restructured), never returning a partial guess
    /// for it.
    /// </summary>
    public static ParsedPassPage Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var hasLegacyCardMarkup = html.Contains(LegacyCardMarker, StringComparison.Ordinal);
        var flight = ExtractFlightData(html);
        if (flight is null)
        {
            return new ParsedPassPage(HasStructuredData: false, hasLegacyCardMarkup, []);
        }

        var tiers = new List<ParsedPassTier>();
        var markerIndex = flight.IndexOf(ProductTypeMarker, StringComparison.Ordinal);
        while (markerIndex >= 0)
        {
            tiers.Add(ParseTierAt(flight, markerIndex));
            markerIndex = flight.IndexOf(ProductTypeMarker, markerIndex + ProductTypeMarker.Length, StringComparison.Ordinal);
        }

        return new ParsedPassPage(HasStructuredData: true, hasLegacyCardMarkup, tiers);
    }

    /// <summary>Concatenates every decoded flight data chunk, or null when the page has none at all.</summary>
    private static string? ExtractFlightData(string html)
    {
        StringBuilder? builder = null;
        foreach (Match match in FlightChunk.Matches(html))
        {
            string? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<string>(match.Groups[1].Value);
            }
            catch (JsonException ex)
            {
                throw new FormatException("An embedded page data chunk could not be decoded.", ex);
            }

            (builder ??= new StringBuilder()).Append(chunk);
        }

        return builder?.ToString();
    }

    private static ParsedPassTier ParseTierAt(string flight, int markerIndex)
    {
        var candidate = markerIndex;
        for (var attempt = 0; attempt < MaxObjectStartCandidates; attempt++)
        {
            candidate = flight.LastIndexOf('{', candidate - 1 < 0 ? 0 : candidate - 1);
            if (candidate < 0)
            {
                break;
            }

            if (TryParseObject(flight, candidate) is { } root &&
                root.TryGetProperty("_type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                type.GetString() == "product_v2")
            {
                return ReadTier(root);
            }

            if (candidate == 0)
            {
                break;
            }
        }

        throw new FormatException(
            "A Season Pass/Membership tier was found in the page data but could not be read — " +
            "the page structure may have changed or the response was truncated.");
    }

    private static JsonElement? TryParseObject(string flight, int start)
    {
        var length = Math.Min(MaxObjectLength, flight.Length - start);
        var bytes = Encoding.UTF8.GetBytes(flight.AsSpan(start, length).ToArray());
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ParsedPassTier ReadTier(JsonElement root)
    {
        var options = new List<ParsedPassOption>();
        if (root.TryGetProperty("paymentOptions", out var paymentOptions) && paymentOptions.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in paymentOptions.EnumerateArray())
            {
                if (option.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var unresolved = false;
                string? Field(string name) => ReadFlightString(option, name, ref unresolved);

                options.Add(new ParsedPassOption(
                    Title: Field("optionTitle"),
                    Price: Field("price"),
                    PriceFrequency: Field("priceFrequency"),
                    ReferencePrice: Field("pricePretense"),
                    Subtitle: Field("optionSubtitle"),
                    Savings: Field("priceSavings"),
                    Highlight: Field("highlightText"),
                    BeforePriceText: Field("beforePriceText"),
                    LockedText: Field("lockedText"),
                    AccessoKeyword: NullLiteralToNull(Field("accessoKeyword")),
                    HasUnresolvedField: unresolved));
            }
        }

        var ignored = false;
        return new ParsedPassTier(
            CmsId: ReadFlightString(root, "_id", ref ignored),
            Title: ReadFlightString(root, "guestFacingTitle", ref ignored),
            HasPaymentOptions: options.Count > 0,
            Options: options,
            FeesDisclosure: ReadPortableText(root, "taxesAndFeesDisclosure"));
    }

    /// <summary>
    /// Reads a string field, decoding the flight format's escaping: a literal value starting with "$"
    /// is sent as "$$…" ("$$99" → "$99"), while "$" followed by anything else is a reference to another
    /// chunk — never read as text (flagged through <paramref name="unresolved"/> instead).
    /// Whitespace-only values are null.
    /// </summary>
    private static string? ReadFlightString(JsonElement element, string name, ref bool unresolved)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.StartsWith("$$", StringComparison.Ordinal))
        {
            text = text[1..];
        }
        else if (text.StartsWith('$'))
        {
            unresolved = true;
            return null;
        }

        return text.Trim();
    }

    private static string? NullLiteralToNull(string? value) =>
        string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) ? null : value;

    /// <summary>Joins a Sanity portable-text field's span texts ("*Plus processing fee of up to $9.99 &amp; applicable taxes.").</summary>
    private static string? ReadPortableText(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var blocks) || blocks.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object ||
                !block.TryGetProperty("children", out var children) ||
                children.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var text = string.Concat(children.EnumerateArray()
                .Where(child => child.ValueKind == JsonValueKind.Object && child.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                .Select(child => child.GetProperty("text").GetString()));
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text.Trim());
            }
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }
}

/// <param name="HasStructuredData">The page carried flight data at all (a page without it is not the expected page format).</param>
/// <param name="HasLegacyCardMarkup">Rendered <c>productv2-card</c> markup is present (so an absence of tier data is a layout change, not an empty catalog).</param>
internal sealed record ParsedPassPage(bool HasStructuredData, bool HasLegacyCardMarkup, IReadOnlyList<ParsedPassTier> Tiers);

/// <summary>One tier document as published. <see cref="Title"/> is the guest-facing card title ("Gold Tier").</summary>
internal sealed record ParsedPassTier(
    string? CmsId,
    string? Title,
    bool HasPaymentOptions,
    IReadOnlyList<ParsedPassOption> Options,
    string? FeesDisclosure);

/// <summary>One "Choose your way" purchase option exactly as published — every field optional, nothing interpreted.</summary>
internal sealed record ParsedPassOption(
    string? Title,
    string? Price,
    string? PriceFrequency,
    string? ReferencePrice,
    string? Subtitle,
    string? Savings,
    string? Highlight,
    string? BeforePriceText,
    string? LockedText,
    string? AccessoKeyword,
    bool HasUnresolvedField);
