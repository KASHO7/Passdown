using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Carowinds.Passdown.Domain.Promotions;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>One official purchase option inside a structured offer card (<c>paymentOptions[]</c>).</summary>
internal sealed record OfferCardOption(string Key, string Title, string? Subtitle, string? PriceText, string? PriceUnitText, string? SavingsText, string? Keyword);

/// <summary>
/// Phase 31: one official offer card as published in the page's structured CMS data
/// (<c>_type: "product_v2"</c>) — the same document the visible <c>productv2-card</c> renders from.
/// <see cref="Title"/> is null when the card's title could not be read (reported as unreadable,
/// never fabricated).
/// </summary>
internal sealed record OfferCard(
    string? OfficialId,
    string? Title,
    string? Keyword,
    string? BeforePriceText,
    string? CurrentPriceText,
    string? CurrentPriceUnitText,
    string? StrikethroughPriceText,
    string? SavingsText,
    string? Subtitle,
    string? Highlight,
    string? DescriptionText,
    IReadOnlyList<string> BenefitLabels,
    IReadOnlyList<string> Disclosures,
    IReadOnlyList<OfferCardOption> Options);

/// <summary>A page-level campaign statement with a date, e.g. "Hurry! Black Friday Deal Ends 11/27".</summary>
internal sealed record CampaignStatement(string CampaignPhrase, OfficialDateStatement End);

/// <summary>
/// Phase 31: reads official offer cards, campaign end banners, and campaign tab links from a
/// sixflags.com Carowinds offers page. Pure parsing — no identity, category, or status decisions.
/// </summary>
internal static partial class OfficialOfferPageParser
{
    public const string ProductMarker = "\"_type\":\"product_v2\"";

    public static IReadOnlyList<OfferCard> ParseCards(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var cards = new List<OfferCard>();
        foreach (var obj in NextFlightPayload.FindEnclosingObjects(payload, ProductMarker))
        {
            cards.Add(new OfferCard(
                OfficialId: NextFlightPayload.GetText(obj, "_id"),
                Title: NextFlightPayload.GetText(obj, "guestFacingTitle") ?? NextFlightPayload.GetText(obj, "title"),
                Keyword: obj.TryGetProperty("buyNowLink", out var buy) ? NextFlightPayload.GetText(buy, "keyword") : null,
                BeforePriceText: NextFlightPayload.GetText(obj, "beforePriceText"),
                CurrentPriceText: NextFlightPayload.GetText(obj, "currentPrice"),
                CurrentPriceUnitText: NextFlightPayload.GetText(obj, "currentPriceModifier"),
                StrikethroughPriceText: NextFlightPayload.GetText(obj, "strikethroughPrice"),
                SavingsText: NextFlightPayload.GetText(obj, "savings"),
                Subtitle: NextFlightPayload.GetText(obj, "subtitle"),
                Highlight: NextFlightPayload.GetText(obj, "highlight"),
                DescriptionText: NextFlightPayload.GetBlockText(obj, "description"),
                BenefitLabels: ReadLabels(obj, "featuredBenefits", "guestFacingLabel"),
                Disclosures: NextFlightPayload.GetBlockText(obj, "taxesAndFeesDisclosure") is { } disclosure ? [disclosure] : [],
                Options: ReadOptions(obj)));
        }

        return cards;
    }

    private static IReadOnlyList<string> ReadLabels(JsonElement obj, string arrayProperty, string labelProperty)
    {
        if (!obj.TryGetProperty(arrayProperty, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items.EnumerateArray()
            .Select(item => NextFlightPayload.GetText(item, labelProperty))
            .OfType<string>()
            .ToList();
    }

    private static IReadOnlyList<OfferCardOption> ReadOptions(JsonElement obj)
    {
        if (!obj.TryGetProperty("paymentOptions", out var options) || options.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<OfferCardOption>();
        var index = 0;
        foreach (var option in options.EnumerateArray())
        {
            index++;
            var title = NextFlightPayload.GetText(option, "optionTitle");
            if (title is null)
            {
                continue;
            }

            result.Add(new OfferCardOption(
                Key: NextFlightPayload.GetText(option, "_key") ?? index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Title: title,
                Subtitle: NextFlightPayload.GetText(option, "optionSubtitle"),
                PriceText: NextFlightPayload.GetText(option, "price"),
                PriceUnitText: NextFlightPayload.GetText(option, "priceFrequency"),
                SavingsText: NextFlightPayload.GetText(option, "priceSavings"),
                Keyword: NextFlightPayload.GetText(option, "accessoKeyword")));
        }

        return result;
    }

    /// <summary>
    /// Campaign tab links (<c>productGroupTabs[].link</c>, e.g. "black-friday-deal"), so a new
    /// campaign page the official offers page links to is discovered without hardcoding its URL.
    /// </summary>
    public static IReadOnlyList<string> ParseTabLinks(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var links = new List<string>();
        foreach (Match match in TabsRegex().Matches(payload))
        {
            try
            {
                using var document = JsonDocument.Parse(match.Groups["array"].Value);
                foreach (var tab in document.RootElement.EnumerateArray())
                {
                    if (NextFlightPayload.GetText(tab, "link") is { } link && SlugRegex().IsMatch(link))
                    {
                        links.Add(link);
                    }
                }
            }
            catch (JsonException)
            {
                // Not a self-contained array; ignore.
            }
        }

        return links.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Page-level "… Ends M/D" statements from the visible page text (hero, promo bars). The campaign
    /// phrase is the wording before "Ends" (after any "Hurry!"; before any ":" headline split), used
    /// only to match offers whose own title or highlight names the same campaign.
    /// </summary>
    public static IReadOnlyList<CampaignStatement> ParseCampaignStatements(string html, DateOnly observedOn)
    {
        ArgumentNullException.ThrowIfNull(html);

        var statements = new List<CampaignStatement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match node in TextNodeRegex().Matches(html))
        {
            var text = WebUtility.HtmlDecode(node.Groups["text"].Value).Trim();
            var match = CampaignEndRegex().Match(text);
            if (!match.Success || !seen.Add(text))
            {
                continue;
            }

            var phrase = match.Groups["phrase"].Value.Trim();
            var colon = phrase.IndexOf(':');
            if (colon > 0)
            {
                phrase = phrase[..colon];
            }

            phrase = HurryRegex().Replace(phrase, string.Empty).Trim().TrimEnd('.', '!', ',').Trim();
            if (phrase.Length < 4)
            {
                continue;
            }

            var end = OfficialOfferText.FindPurchaseDeadline(text, observedOn, DeadlineSource.CampaignBanner);
            if (end is not null)
            {
                statements.Add(new CampaignStatement(phrase, end with { SourceText = text }));
            }
        }

        return statements;
    }

    [GeneratedRegex(@"""productGroupTabs"":(?<array>\[[^\[\]]*\])")]
    private static partial Regex TabsRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-/]*$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@">(?<text>[^<>]{6,300})<")]
    private static partial Regex TextNodeRegex();

    [GeneratedRegex(@"^(?<phrase>.{4,120}?)\s*[.!]?\s*\bEnds\s+\d{1,2}/\d{1,2}", RegexOptions.IgnoreCase)]
    private static partial Regex CampaignEndRegex();

    [GeneratedRegex(@"^\s*Hurry!?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex HurryRegex();
}
