using Carowinds.Passdown.Domain.Promotions;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 31: maps official offer cards to (a) the existing catalog card shape for Promotions catalog
/// reconciliation — preserving the established identities (<c>data-ab-product</c> id, else accesso
/// keyword) so existing rows, overrides and history keep matching — and (b) the richer
/// <see cref="OfficialPromotion"/> deals model.
/// </summary>
internal static class OfficialOfferMapper
{
    /// <summary>
    /// Catalog rows for one card. A card with a headline price is one row (as before). A card that
    /// sells through payment options instead (e.g. "Black Friday Deal": Silver Pass $69 / Good Any
    /// Day Ticket $69) becomes one row per option, named "{card}: {option}" from the official titles
    /// and identified by the option's own accesso keyword. A card with neither has no catalog row
    /// (it still appears as a deal).
    /// </summary>
    public static IEnumerable<ParsedProductCard> ToCatalogCards(OfferCard card)
    {
        if (card.Title is null)
        {
            yield break;
        }

        if (card.CurrentPriceText is not null)
        {
            yield return new ParsedProductCard(
                SanityProductId: card.OfficialId,
                AccessoKeyword: card.Keyword,
                DisplayName: card.Title,
                CurrentDisplayPrice: card.CurrentPriceText,
                CurrentPriceAmount: OfficialOfferText.ParseCurrencyAmount(card.CurrentPriceText),
                ComparisonDisplayPrice: card.StrikethroughPriceText,
                ComparisonPriceAmount: OfficialOfferText.ParseCurrencyAmount(card.StrikethroughPriceText),
                PriceQualifier: card.BeforePriceText);
            yield break;
        }

        foreach (var option in card.Options.Where(o => o.PriceText is not null))
        {
            yield return new ParsedProductCard(
                SanityProductId: null,
                AccessoKeyword: option.Keyword ?? $"{card.OfficialId}:{option.Key}",
                DisplayName: $"{card.Title}: {option.Title}",
                CurrentDisplayPrice: option.PriceText!,
                CurrentPriceAmount: OfficialOfferText.ParseCurrencyAmount(option.PriceText),
                ComparisonDisplayPrice: null,
                ComparisonPriceAmount: null,
                PriceQualifier: null);
        }
    }

    public static string PromotionIdFor(OfferCard card) =>
        "offer:" + (card.OfficialId ?? card.Keyword ?? (card.Title ?? "untitled").Trim().ToLowerInvariant());

    public static OfficialPromotion ToPromotion(
        OfferCard card,
        Uri pageUrl,
        DateTimeOffset checkedAt,
        IReadOnlyList<CampaignStatement> campaignStatements)
    {
        ArgumentNullException.ThrowIfNull(card);
        var observedOn = DateOnly.FromDateTime(checkedAt.DateTime);

        var feeDisclosures = card.Disclosures.Where(IsFeeDisclosure).ToList();
        var terms = new List<string>();
        if (card.DescriptionText is not null)
        {
            terms.Add(card.DescriptionText);
        }

        terms.AddRange(card.Disclosures.Where(d => !IsFeeDisclosure(d)));

        // Offer-specific wording only: the card's own description, terms and benefit labels.
        var offerText = string.Join(" \n", terms.Concat(card.BenefitLabels).Concat(feeDisclosures));
        var deadline = OfficialOfferText.FindPurchaseDeadline(offerText, observedOn, DeadlineSource.OfferTerms);
        var purchaseStart = OfficialOfferText.FindPurchaseStart(offerText, observedOn);
        var redemption = OfficialOfferText.FindRedemptionWindow(offerText, observedOn);

        var campaign = campaignStatements.FirstOrDefault(s => Names(card, s.CampaignPhrase));
        var discrepancies = new List<string>();
        if (campaign is not null && deadline is not null && campaign.End.Date != deadline.Date)
        {
            discrepancies.Add(
                $"The page banner says \"{campaign.End.SourceText}\", but this offer's own terms say \"{deadline.SourceText}\". The offer's own terms are shown as its deadline.");
        }

        var options = card.Options.Select(o => new PromotionPriceOption(
            o.Title,
            o.Subtitle,
            o.PriceText,
            OfficialOfferText.ParseCurrencyAmount(o.PriceText),
            o.PriceUnitText,
            o.SavingsText,
            o.Keyword)).ToList();

        return new OfficialPromotion
        {
            PromotionId = PromotionIdFor(card),
            Title = card.Title!,
            Category = Categorize(card),
            Highlight = card.Highlight,
            Subtitle = card.Subtitle,
            AdvertisedPriceText = card.CurrentPriceText,
            AdvertisedPriceAmount = OfficialOfferText.ParseCurrencyAmount(card.CurrentPriceText),
            PriceQualifier = card.BeforePriceText,
            PriceUnitText = card.CurrentPriceUnitText,
            RegularPriceAmount = OfficialOfferText.ParseCurrencyAmount(card.StrikethroughPriceText),
            SavingsText = card.SavingsText,
            PriceOptions = options,
            PurchaseDeadline = deadline,
            PurchaseStart = purchaseStart,
            CampaignEnd = campaign?.End,
            RedemptionWindow = redemption,
            ValidityNotes = card.BenefitLabels,
            Terms = terms,
            FeeDisclosures = feeDisclosures,
            Discrepancies = discrepancies,
            PrimarySourceUrl = pageUrl,
            SourceUrls = [pageUrl],
            Keyword = card.Keyword,
            FirstSeenAt = checkedAt,
            LastVerifiedAt = checkedAt,
        };
    }

    /// <summary>Merges the same offer seen on several official pages: union of source links; first page's details kept; a later page's extra deadline evidence fills gaps.</summary>
    public static OfficialPromotion MergeDuplicate(OfficialPromotion first, OfficialPromotion other)
    {
        var urls = first.SourceUrls.Concat(other.SourceUrls).DistinctBy(u => u.AbsoluteUri).ToList();
        var discrepancies = first.Discrepancies.Concat(other.Discrepancies).Distinct(StringComparer.Ordinal).ToList();
        return first with
        {
            SourceUrls = urls,
            CampaignEnd = first.CampaignEnd ?? other.CampaignEnd,
            PurchaseDeadline = first.PurchaseDeadline ?? other.PurchaseDeadline,
            RedemptionWindow = first.RedemptionWindow ?? other.RedemptionWindow,
            Discrepancies = discrepancies,
        };
    }

    private static bool Names(OfferCard card, string phrase) =>
        (card.Title?.Contains(phrase, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (card.Highlight?.Contains(phrase, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsFeeDisclosure(string text) =>
        text.Contains("processing fee", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("initiation fee", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("applicable taxes", StringComparison.OrdinalIgnoreCase);

    private static PromotionCategory Categorize(OfferCard card)
    {
        var text = string.Join(" ", new[] { card.Title, card.Subtitle, card.Keyword }
            .Concat(card.Options.Select(o => o.Title)).OfType<string>());

        bool Has(string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

        if (Has("Bundle"))
        {
            return PromotionCategory.Bundle;
        }

        if (Has("Membership"))
        {
            return PromotionCategory.Membership;
        }

        if (Has("Resort") || Has("Camp Wilderness") || Has("Hotel") || Has("Night"))
        {
            return PromotionCategory.Lodging;
        }

        if (Has("Pass") && (Has("Silver") || Has("Gold") || Has("Prestige") || Has("Season")))
        {
            return PromotionCategory.SeasonPass;
        }

        if (Has("Ticket"))
        {
            return PromotionCategory.DailyTicket;
        }

        return Has("Admission") ? PromotionCategory.Admission : PromotionCategory.Other;
    }

    /// <summary>Phase 31: a deal built from a legacy HTML card (pages without structured data) — title and prices only.</summary>
    public static OfficialPromotion FromHtmlCard(ParsedProductCard card, Uri pageUrl, DateTimeOffset checkedAt) => new()
    {
        PromotionId = "offer:" + (card.SanityProductId ?? card.AccessoKeyword ?? card.DisplayName.Trim().ToLowerInvariant()),
        Title = card.DisplayName,
        AdvertisedPriceText = card.CurrentDisplayPrice,
        AdvertisedPriceAmount = card.CurrentPriceAmount,
        PriceQualifier = card.PriceQualifier,
        RegularPriceAmount = card.ComparisonPriceAmount,
        PrimarySourceUrl = pageUrl,
        SourceUrls = [pageUrl],
        Keyword = card.AccessoKeyword,
        FirstSeenAt = checkedAt,
        LastVerifiedAt = checkedAt,
    };
}
