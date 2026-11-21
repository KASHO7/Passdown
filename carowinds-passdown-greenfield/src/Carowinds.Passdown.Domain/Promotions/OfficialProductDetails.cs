using System.Globalization;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Domain.Promotions;

/// <summary>Phase 31B: the official purchase channel a product's details were read from.</summary>
public enum OfficialProductChannel
{
    /// <summary>The official Memberships &amp; Season Passes page.</summary>
    SeasonPasses,

    /// <summary>The official Military &amp; First Responders page (eligibility verified through ID.me at checkout).</summary>
    MilitaryFirstResponder,

    /// <summary>The official Special Offers page and the campaign pages it links to.</summary>
    Promotions,
}

/// <summary>Phase 31B: what one official statement about a product is about.</summary>
public enum ProductDetailKind
{
    /// <summary>Who may buy or use it (military, first responders, passholders, guests 65+, ID required…).</summary>
    Eligibility,

    /// <summary>A purchase condition that is not a deadline ("Offer available online only", "Book by …").</summary>
    Purchase,

    /// <summary>When the product can be used/visited ("Visits Thru Labor Day 2027", a stated date range).</summary>
    VisitValidity,

    /// <summary>A Bring-A-Friend benefit (free ticket or discount) — never assumed for a product whose own text does not state it.</summary>
    BringAFriend,

    /// <summary>A conditional bonus tied to buying during a campaign or with another product.</summary>
    Incentive,

    /// <summary>Restrictions, exclusions and blackout dates.</summary>
    Restriction,

    /// <summary>Processing fees, taxes and additional charges.</summary>
    Fee,

    /// <summary>General fine print attached to the card (benefits subject to change, pricing online only…).</summary>
    General,
}

/// <summary>
/// Phase 31B: one official statement about a product, in the official wording. Purchase and
/// redemption deadlines are kept separate: <see cref="PurchaseDeadline"/> is the last day to
/// <em>buy</em> (to qualify), <see cref="RedemptionDeadline"/> the last day to <em>use</em> what was
/// earned. Either is null when the official text does not state it — nothing is inferred.
/// </summary>
public sealed record ProductDetailStatement
{
    public required ProductDetailKind Kind { get; init; }

    /// <summary>The official sentence (or benefit label) exactly as published, trimmed.</summary>
    public required string Text { get; init; }

    public OfficialDateStatement? PurchaseDeadline { get; init; }

    public OfficialDateStatement? RedemptionDeadline { get; init; }

    public OfficialDateRange? ValidityWindow { get; init; }

    /// <summary>The footnote marker this statement was referenced by on the card (e.g. "**"), when it came from the card's fine print.</summary>
    public string? FootnoteMarker { get; init; }
}

/// <summary>Phase 31B: a previously published set of statements for a product, kept when the official terms change.</summary>
public sealed record SupersededProductTerms(
    IReadOnlyList<ProductDetailStatement> Statements,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastVerifiedAt);

/// <summary>
/// Phase 31B: the official important details and deadlines of one product card, as last verified.
/// Identified by <see cref="DetailsId"/> (channel + official id, else Buy Now keyword, else title).
/// Matching to a Products row uses <see cref="Keyword"/>/<see cref="RelatedKeywords"/>/
/// <see cref="OfficialId"/> or, on the Season Passes page, the pass tier (<see cref="TierName"/>).
/// </summary>
public sealed record OfficialProductDetails
{
    public required string DetailsId { get; init; }

    public required string Title { get; init; }

    public OfficialProductChannel Channel { get; init; }

    public string? OfficialId { get; init; }

    public string? Keyword { get; init; }

    /// <summary>Purchase-option keywords published on the same card (e.g. the Season Pass and Bundle options).</summary>
    public IReadOnlyList<string> RelatedKeywords { get; init; } = [];

    /// <summary>The pass tier a Season Passes card is for ("Gold" for "Gold Tier"), else null.</summary>
    public string? TierName { get; init; }

    public IReadOnlyList<ProductDetailStatement> Statements { get; init; } = [];

    /// <summary>Official sources that disagree (e.g. a banner date vs. the card's own terms) — shown, never silently merged.</summary>
    public IReadOnlyList<string> Discrepancies { get; init; } = [];

    public required Uri PrimarySourceUrl { get; init; }

    public IReadOnlyList<Uri> SourceUrls { get; init; } = [];

    public DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>The last successful official check that listed this card.</summary>
    public DateTimeOffset LastVerifiedAt { get; init; }

    /// <summary>The latest successful check listed the card but could not read it — its details are the last verified ones.</summary>
    public bool LastCheckUnreadable { get; init; }

    /// <summary>Earlier official terms for this card, newest first (bounded).</summary>
    public IReadOnlyList<SupersededProductTerms> PreviousTerms { get; init; } = [];
}

/// <summary>Phase 31B: a campaign end date stated page-wide ("Hurry! Black Friday Deal Ends 11/27").</summary>
public sealed record CampaignDeadline(string CampaignPhrase, OfficialDateStatement End);

/// <summary>Phase 31B: the official texts of one product card that may carry important details.</summary>
/// <param name="Description">The card's own description (card-specific statements).</param>
/// <param name="BenefitLabels">Featured benefit labels, with any footnote markers ("Free General Parking*").</param>
/// <param name="FeeDisclosures">The card's taxes-and-fees disclosure blocks.</param>
/// <param name="FinePrintBlocks">The card's fine-print modal blocks; a block starting with a footnote marker only applies when the card references that marker.</param>
/// <param name="Campaigns">Page-wide campaign end dates; one only applies to a statement naming that campaign.</param>
public sealed record ProductTermsSource(
    string? Description,
    string? Subtitle,
    IReadOnlyList<string> BenefitLabels,
    IReadOnlyList<string> FeeDisclosures,
    IReadOnlyList<string> FinePrintBlocks,
    IReadOnlyList<CampaignDeadline> Campaigns);

/// <summary>
/// Phase 31B: reads a product card's official texts into concise, classified statements. Pure and
/// conservative: only wording the card itself publishes (or fine print it references by footnote) is
/// used; a perk printed in shared fine print is never attached to a card that does not reference it,
/// and a date is only ever one the text states.
/// </summary>
public static partial class OfficialProductTermsReader
{
    private const string FootnoteChars = "*^+†‡§";

    public static IReadOnlyList<ProductDetailStatement> Read(ProductTermsSource source, DateOnly observedOn)
    {
        ArgumentNullException.ThrowIfNull(source);

        var statements = new List<ProductDetailStatement>();

        foreach (var sentence in Sentences(source.Description))
        {
            if (Classify(sentence, fromDescription: true) is { } kind)
            {
                statements.Add(Build(kind, sentence, observedOn, source.Campaigns, footnote: null));
            }
        }

        var referencedMarkers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in source.BenefitLabels)
        {
            var (text, marker) = SplitTrailingMarker(label);
            if (marker is not null)
            {
                referencedMarkers.Add(marker);
            }

            if (text.Length > 0 && ClassifyLabel(text) is { } labelKind)
            {
                statements.Add(Build(labelKind, text, observedOn, source.Campaigns, footnote: null));
            }
        }

        foreach (var marker in FootnoteMarkersIn(source.Description).Concat(FootnoteMarkersIn(source.Subtitle)))
        {
            referencedMarkers.Add(marker);
        }

        foreach (var block in source.FeeDisclosures)
        {
            foreach (var rawSentence in Sentences(StripLeadingMarker(block).Text))
            {
                var sentence = StripLeadingMarker(rawSentence).Text; // "†Haunted mazes…" mid-disclosure reads without its marker
                if (sentence.Length == 0)
                {
                    continue;
                }

                var kind = Classify(sentence, fromDescription: false) ?? ProductDetailKind.General;
                statements.Add(Build(kind, sentence, observedOn, source.Campaigns, footnote: null));
            }
        }

        foreach (var block in source.FinePrintBlocks)
        {
            var (text, marker) = StripLeadingMarker(block);
            if (marker is not null && !referencedMarkers.Contains(marker))
            {
                continue; // footnote for a different product/benefit — never assumed to apply here
            }

            foreach (var rawSentence in Sentences(text))
            {
                var sentence = StripLeadingMarker(rawSentence).Text;
                if (sentence.Length == 0 || IsContactBoilerplate(sentence))
                {
                    continue;
                }

                var kind = Classify(sentence, fromDescription: false) ?? ProductDetailKind.General;
                statements.Add(Build(kind, sentence, observedOn, source.Campaigns, marker));
            }
        }

        return statements
            .DistinctBy(s => (s.Kind, Normalize(s.Text)))
            .ToList();
    }

    /// <summary>Splits text into trimmed sentences; a period inside a date or abbreviation ("1/3/27.", "Nov. 30") does not split mid-date.</summary>
    public static IReadOnlyList<string> Sentences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var result = new List<string>();
        foreach (var part in SentenceSplitRegex().Split(text.Replace("\r", string.Empty, StringComparison.Ordinal)))
        {
            var sentence = part.Trim();
            if (sentence.Length == 0)
            {
                continue;
            }

            // "…get a FREE Bring-A-Friend ticket. Use by 1/3/27." — a bare use-by/valid-through sentence
            // belongs to the statement before it, so the perk keeps its own redemption deadline.
            if (result.Count > 0 && ContinuationRegex().IsMatch(sentence))
            {
                result[^1] = result[^1] + " " + sentence;
                continue;
            }

            result.Add(sentence);
        }

        return result;
    }

    private static ProductDetailStatement Build(
        ProductDetailKind kind, string text, DateOnly observedOn, IReadOnlyList<CampaignDeadline> campaigns, string? footnote)
    {
        var purchase = OfficialOfferText.FindPurchaseDeadline(text, observedOn, DeadlineSource.OfferTerms);
        if (purchase is null && kind is ProductDetailKind.BringAFriend or ProductDetailKind.Incentive)
        {
            // "Buy during the Black Friday Deal and get…" — the purchase window is the named campaign's,
            // taken only from an official page-wide end date for that same campaign.
            var campaign = campaigns
                .Where(c => text.Contains(c.CampaignPhrase, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.CampaignPhrase.Length)
                .FirstOrDefault();
            purchase = campaign?.End;
        }

        var redemption = OfficialOfferText.FindRedemptionDeadline(text, observedOn);
        var window = OfficialOfferText.FindRedemptionWindow(text, observedOn);

        if (kind == ProductDetailKind.General && (purchase is not null || window is not null))
        {
            kind = purchase is not null ? ProductDetailKind.Purchase : ProductDetailKind.VisitValidity;
        }

        return new ProductDetailStatement
        {
            Kind = kind,
            Text = text,
            PurchaseDeadline = purchase,
            RedemptionDeadline = redemption,
            ValidityWindow = window,
            FootnoteMarker = footnote,
        };
    }

    /// <summary>The kind of a free-text sentence, or null when it is ordinary marketing copy with no term in it (descriptions only).</summary>
    public static ProductDetailKind? Classify(string sentence, bool fromDescription)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        bool Has(string phrase) => sentence.Contains(phrase, StringComparison.OrdinalIgnoreCase);

        if (BringAFriendRegex().IsMatch(sentence))
        {
            return ProductDetailKind.BringAFriend;
        }

        if (Has("processing fee") || Has("applicable taxes") || Has("initiation fee") || Has("additional fee") || Has("service fee"))
        {
            return ProductDetailKind.Fee;
        }

        if (Has("blackout") || Has("not valid") || Has("not eligible") || Has("non-transferable") || Has("subject to availability")
            || Has("must first visit") || Has("excluding") || Has("excludes") || Has("required between") || Has("pass required")
            || Has("minimum 12-month") || Has("minimum term"))
        {
            return ProductDetailKind.Restriction;
        }

        if (Has("military") || Has("first responder") || Has("veteran") || Has("ID.me") || Has("valid ID") || Has("65+")
            || Has("passholder exclusive") || Has("must be accompanied") || Has("must be registered") || Has("to qualify")
            || Has("eligib"))
        {
            return ProductDetailKind.Eligibility;
        }

        if (IncentiveRegex().IsMatch(sentence))
        {
            return ProductDetailKind.Incentive;
        }

        if (Has("online only") || Has("book by") || Has("buy by") || Has("purchase by") || Has("up to 6 tickets"))
        {
            return ProductDetailKind.Purchase;
        }

        if (VisitValidityRegex().IsMatch(sentence))
        {
            return ProductDetailKind.VisitValidity;
        }

        return null;
    }

    private static ProductDetailKind? ClassifyLabel(string label)
    {
        if (BringAFriendRegex().IsMatch(label))
        {
            return ProductDetailKind.BringAFriend;
        }

        if (VisitValidityRegex().IsMatch(label) || label.StartsWith("Stays ", StringComparison.OrdinalIgnoreCase))
        {
            return ProductDetailKind.VisitValidity;
        }

        bool Has(string phrase) => label.Contains(phrase, StringComparison.OrdinalIgnoreCase);
        if (Has("ID.me") || Has("Guests 65+") || Has("Passholder Exclusive") || Has("Valid ID"))
        {
            return ProductDetailKind.Eligibility;
        }

        if (Has("Up to ") && Has("ticket"))
        {
            return ProductDetailKind.Purchase;
        }

        return null; // ordinary benefit labels ("Park Admission") are not important details
    }

    private static bool IsContactBoilerplate(string sentence) =>
        sentence.StartsWith("Contact the park", StringComparison.OrdinalIgnoreCase);

    private static (string Text, string? Marker) SplitTrailingMarker(string label)
    {
        var trimmed = label.Trim();
        var end = trimmed.Length;
        while (end > 0 && FootnoteChars.Contains(trimmed[end - 1], StringComparison.Ordinal))
        {
            end--;
        }

        return end == trimmed.Length ? (trimmed, null) : (trimmed[..end].Trim(), trimmed[end..]);
    }

    private static (string Text, string? Marker) StripLeadingMarker(string block)
    {
        var trimmed = block.Trim();
        var start = 0;
        while (start < trimmed.Length && FootnoteChars.Contains(trimmed[start], StringComparison.Ordinal))
        {
            start++;
        }

        return start == 0 ? (trimmed, null) : (trimmed[start..].Trim(), trimmed[..start]);
    }

    private static IEnumerable<string> FootnoteMarkersIn(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        foreach (Match match in InlineMarkerRegex().Matches(text))
        {
            yield return match.Value;
        }
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.ToLower(CultureInfo.InvariantCulture).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');

    // Split after ".", "!" or "?" followed by whitespace and a capital/footnote/digit-free start, or at newlines.
    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z""(†‡§*^+])|\n+")]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"^(?:Use|Redeem|Redeemable|Valid|Expires)\s+(?:by|through|thru|until|on)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ContinuationRegex();

    [GeneratedRegex(@"\bBring[\s-]+a[\s-]+Friend\b", RegexOptions.IgnoreCase)]
    private static partial Regex BringAFriendRegex();

    [GeneratedRegex(@"\b(?:free|bonus|receive|get\s+(?:a|an|one|your))\b.*\b(?:during|when\s+you|with\s+(?:the\s+)?purchase|buy|purchase|enroll)\b|\b(?:during|when\s+you|buy|purchase|enroll)\b.*\b(?:free|bonus|receive)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IncentiveRegex();

    [GeneratedRegex(@"\b(?:visits?|valid|stays?)\b.*\b(?:thru|through|until|all\s+of|season|\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex VisitValidityRegex();

    // A footnote marker run glued to the end of a word ("Bring-A-Friend^+"), never a bare "*" price note.
    [GeneratedRegex(@"(?<=[A-Za-z)])[\*\^\+†‡§]+(?=\s|$|[.,;])")]
    private static partial Regex InlineMarkerRegex();
}

/// <summary>Phase 31B: how one official statement stands today.</summary>
public enum ProductDetailStatus
{
    /// <summary>Listed at the latest successful check and not past any stated deadline.</summary>
    Current,

    /// <summary>A stated deadline/window has passed, or the latest successful check no longer lists the card.</summary>
    Expired,

    /// <summary>Not re-verified recently — shown as previously verified, never as newly confirmed.</summary>
    Stale,

    /// <summary>The latest check could not read the card; these are the last verified details.</summary>
    Unverified,
}

public sealed record ProductDetailAssessment(ProductDetailStatus Status, string Reason);

/// <summary>
/// Phase 31B: pure status rules for official product details, by the Eastern "today". Deadlines are
/// inclusive (valid on the stated day). A passed purchase deadline with an open redemption deadline
/// is reported as both: no longer available with new purchases, earned benefits still usable.
/// </summary>
public static class ProductDetailStatusEvaluator
{
    /// <summary>Details not re-verified within this window are shown as stale (the deals rule).</summary>
    public static TimeSpan StaleAfter => PromotionStatusEvaluator.VerificationStaleAfter;

    public static ProductDetailAssessment Evaluate(
        OfficialProductDetails details,
        ProductDetailStatement statement,
        DateOnly today,
        DateTimeOffset? latestSuccessfulCheck,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(statement);

        if (statement.RedemptionDeadline is { } use && use.Date < today)
        {
            return new(ProductDetailStatus.Expired, string.Create(CultureInfo.GetCultureInfo("en-US"), $"Expired — use-by date {use.Date:MMMM d, yyyy} has passed."));
        }

        if (statement.ValidityWindow is { } window && window.End < today)
        {
            return new(ProductDetailStatus.Expired, string.Create(CultureInfo.GetCultureInfo("en-US"), $"Expired — valid through {window.End:MMMM d, yyyy}."));
        }

        if (statement.PurchaseDeadline is { } buy && buy.Date < today)
        {
            var reason = statement.RedemptionDeadline is { } openUse
                ? string.Create(CultureInfo.GetCultureInfo("en-US"), $"No longer offered with new purchases (ended {buy.Date:MMMM d, yyyy}); already-earned benefits are usable through {openUse.Date:MMMM d, yyyy}.")
                : string.Create(CultureInfo.GetCultureInfo("en-US"), $"Purchase deadline passed ({buy.Date:MMMM d, yyyy}).");
            return new(ProductDetailStatus.Expired, reason);
        }

        if (details.LastCheckUnreadable)
        {
            return new(ProductDetailStatus.Unverified, "The latest official check could not read this card; showing what was verified before.");
        }

        if (latestSuccessfulCheck is { } latest && details.LastVerifiedAt < latest)
        {
            return new(ProductDetailStatus.Expired, "No longer listed on the official page at the latest check.");
        }

        if (now - details.LastVerifiedAt > StaleAfter)
        {
            return new(ProductDetailStatus.Stale, "Not re-verified recently; showing what was verified before.");
        }

        return new(ProductDetailStatus.Current, "Listed on the official page at the last check.");
    }
}
