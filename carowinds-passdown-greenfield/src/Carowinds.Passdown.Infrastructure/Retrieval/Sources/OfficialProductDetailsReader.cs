using System.Text.Json;
using Carowinds.Passdown.Application.ProductDetails;
using Carowinds.Passdown.Domain.Promotions;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 31B: reads the official important details and deadlines of each product card from the
/// structured CMS data a sixflags.com Carowinds page embeds (<c>product_v2</c>: description, featured
/// benefits, taxes-and-fees disclosure, fine-print modal) plus the page's own campaign end banners.
/// Shared by the Season Passes, Military &amp; First Responder and Promotions sources so every channel's
/// terms are read by one rule (<see cref="OfficialProductTermsReader"/>). Never throws for unreadable
/// page data — details are optional; the catalog result is never affected by them.
/// </summary>
internal static class OfficialProductDetailsReader
{
    /// <summary>The details on one page, or null when the page carries no readable structured cards.</summary>
    public static OfficialProductDetailsObservation? TryRead(string html, Uri pageUrl, DateTimeOffset checkedAt, OfficialProductChannel channel)
    {
        ArgumentNullException.ThrowIfNull(html);

        var payload = NextFlightPayload.Extract(html);
        if (payload is null || !payload.Contains(OfficialOfferPageParser.ProductMarker, StringComparison.Ordinal))
        {
            return null;
        }

        IReadOnlyList<OfferCard> cards;
        try
        {
            cards = OfficialOfferPageParser.ParseCards(payload);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }

        var observedOn = DateOnly.FromDateTime(checkedAt.DateTime);
        var campaigns = Campaigns(html, observedOn);
        var products = new List<OfficialProductDetails>();
        var unreadable = new List<string>();
        foreach (var card in cards)
        {
            if (card.Title is null)
            {
                if ((card.OfficialId ?? card.Keyword) is not null)
                {
                    unreadable.Add(DetailsIdFor(channel, card));
                }

                continue;
            }

            products.Add(FromCard(card, pageUrl, checkedAt, campaigns, channel));
        }

        return new OfficialProductDetailsObservation(channel, products.DistinctBy(p => p.DetailsId, StringComparer.Ordinal).ToList())
        {
            UnreadableDetailIds = unreadable,
        };
    }

    public static IReadOnlyList<CampaignDeadline> Campaigns(string html, DateOnly observedOn) =>
        OfficialOfferPageParser.ParseCampaignStatements(html, observedOn)
            .Select(statement => new CampaignDeadline(statement.CampaignPhrase, statement.End))
            .DistinctBy(c => (c.CampaignPhrase.ToLowerInvariant(), c.End.Date))
            .ToList();

    public static string DetailsIdFor(OfficialProductChannel channel, OfferCard card) =>
        $"details:{channel}:{(card.OfficialId ?? card.Keyword ?? (card.Title ?? "untitled").Trim().ToLowerInvariant())}";

    public static OfficialProductDetails FromCard(
        OfferCard card, Uri pageUrl, DateTimeOffset checkedAt, IReadOnlyList<CampaignDeadline> campaigns, OfficialProductChannel channel)
    {
        ArgumentNullException.ThrowIfNull(card);
        var observedOn = DateOnly.FromDateTime(checkedAt.DateTime);

        var statements = OfficialProductTermsReader.Read(
            new ProductTermsSource(
                card.DescriptionText,
                card.Subtitle,
                card.BenefitLabels,
                card.Disclosures,
                card.FinePrintBlocks,
                campaigns),
            observedOn);

        return new OfficialProductDetails
        {
            DetailsId = DetailsIdFor(channel, card),
            Title = card.Title!.Trim(),
            Channel = channel,
            OfficialId = card.OfficialId,
            Keyword = card.Keyword,
            RelatedKeywords = card.Options
                .Select(o => o.Keyword)
                .OfType<string>()
                .Where(k => k.Length > 0 && !string.Equals(k, "null", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TierName = channel == OfficialProductChannel.SeasonPasses ? TierOf(card.Title) : null,
            Statements = statements,
            Discrepancies = Discrepancies(statements, campaigns),
            PrimarySourceUrl = pageUrl,
            SourceUrls = [pageUrl],
            FirstSeenAt = checkedAt,
            LastVerifiedAt = checkedAt,
        };
    }

    /// <summary>Same card seen on several official pages: union of source links; the first page's statements are kept.</summary>
    public static OfficialProductDetails MergeDuplicate(OfficialProductDetails first, OfficialProductDetails other) => first with
    {
        SourceUrls = first.SourceUrls.Concat(other.SourceUrls).DistinctBy(u => u.AbsoluteUri).ToList(),
        Discrepancies = first.Discrepancies.Concat(other.Discrepancies).Distinct(StringComparer.Ordinal).ToList(),
    };

    /// <summary>"Gold Tier" → "Gold"; a card title without the word "Tier" is not a tier card.</summary>
    private static string? TierOf(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var trimmed = title.Trim();
        const string suffix = " Tier";
        return trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && trimmed.Length > suffix.Length
            ? trimmed[..^suffix.Length].Trim()
            : null;
    }

    /// <summary>
    /// Official dates that disagree are reported, never merged: a statement's own purchase deadline that
    /// differs from the page banner's end date for the campaign it names, or two different stated purchase
    /// deadlines for the same kind of benefit.
    /// </summary>
    private static IReadOnlyList<string> Discrepancies(IReadOnlyList<ProductDetailStatement> statements, IReadOnlyList<CampaignDeadline> campaigns)
    {
        var result = new List<string>();
        foreach (var statement in statements.Where(s => s.PurchaseDeadline is { Source: DeadlineSource.OfferTerms }))
        {
            foreach (var campaign in campaigns.Where(c => statement.Text.Contains(c.CampaignPhrase, StringComparison.OrdinalIgnoreCase)))
            {
                if (campaign.End.Date != statement.PurchaseDeadline!.Date)
                {
                    result.Add($"The page banner says \"{campaign.End.SourceText}\", but this card's own terms say \"{statement.PurchaseDeadline.SourceText}\". Each is shown with its own wording.");
                }
            }
        }

        foreach (var group in statements.Where(s => s.PurchaseDeadline is not null).GroupBy(s => s.Kind))
        {
            var distinct = group.Select(s => s.PurchaseDeadline!).DistinctBy(d => d.Date).ToList();
            if (distinct.Count > 1)
            {
                result.Add("Official sources state different purchase deadlines: " +
                    string.Join(" / ", distinct.Select(d => $"\"{d.SourceText}\"")) + ". Each is shown with its own wording.");
            }
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }
}
