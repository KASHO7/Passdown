using System.Globalization;
using Carowinds.Passdown.Application.ProductDetails;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Domain.Promotions;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>Phase 31B: one displayed line of a product's Important Details &amp; Deadlines.</summary>
public sealed record ProductDetailLinePresentation(
    string KindLabel,
    string Text,
    string? DeadlineText,
    string StatusLabel,
    string StatusReason,
    bool IsExpired,
    bool IsNotCurrent)
{
    public bool HasDeadline => DeadlineText is not null;

    /// <summary>Never color-only: the status word is part of the accessible name.</summary>
    public string AutomationName => DeadlineText is null
        ? $"{KindLabel}, {StatusLabel}: {Text}"
        : $"{KindLabel}, {StatusLabel}: {Text}. {DeadlineText}";
}

/// <summary>Phase 31B: the whole expandable section for one Products row.</summary>
public sealed record ProductImportantDetailsPresentation(
    string Header,
    string? SummaryText,
    IReadOnlyList<ProductDetailLinePresentation> Lines,
    IReadOnlyList<string> Discrepancies,
    string ProvenanceText,
    string? FreshnessWarning,
    IReadOnlyList<string> PreviousTermsLines,
    Uri SourceUrl)
{
    public const string HeaderText = "Important Details & Deadlines";

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public bool HasSummary => SummaryText is not null;

    public bool HasDiscrepancies => Discrepancies.Count > 0;

    public bool HasFreshnessWarning => FreshnessWarning is not null;

    public bool HasPreviousTerms => PreviousTermsLines.Count > 0;

    /// <summary>Discrepancies as one wrapped block (one per line).</summary>
    public string DiscrepanciesText => string.Join(Environment.NewLine, Discrepancies);

    /// <summary>Earlier official terms as one wrapped block (one per line).</summary>
    public string PreviousTermsText => string.Join(Environment.NewLine + Environment.NewLine, PreviousTermsLines);

    public static ProductImportantDetailsPresentation Build(ProductImportantDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var lines = details.Lines.Select(Line).ToList();
        var source = details.Source;
        var provenance = string.Create(Us, $"From the official {source.Title} card on the {ChannelPageName(source.Channel)} page · Last verified {EasternTimestampFormat.DateYearAndTime(source.LastVerifiedAt)}");

        string? freshness = null;
        if (details.Status.Freshness == OfficialDocumentFreshness.LastCheckFailed)
        {
            freshness = "The latest official check failed; these are the last verified details.";
        }

        var previous = source.PreviousTerms
            .Select(t => string.Create(Us, $"Earlier terms (verified {t.FirstSeenAt:MMM d, yyyy} – {t.LastVerifiedAt:MMM d, yyyy}): ") +
                string.Join(" ", t.Statements.Select(s => s.Text)))
            .ToList();

        return new ProductImportantDetailsPresentation(
            HeaderText,
            Summary(details),
            lines,
            source.Discrepancies,
            provenance,
            freshness,
            previous,
            source.PrimarySourceUrl);
    }

    /// <summary>The next open deadline, shown under the header ("Buy by Nov 27, 2026 · Use by Jan 3, 2027"), or null.</summary>
    private static string? Summary(ProductImportantDetails details)
    {
        var next = details.Deadlines
            .Where(l => l.Assessment.Status != ProductDetailStatus.Expired)
            .Select(l => DeadlineText(l.Statement))
            .OfType<string>()
            .FirstOrDefault();
        return next;
    }

    public static ProductDetailLinePresentation Line(ProductDetailLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new ProductDetailLinePresentation(
            KindLabel(line.Kind),
            line.Text,
            DeadlineText(line.Statement),
            StatusLabel(line.Assessment.Status),
            line.Assessment.Reason,
            line.Assessment.Status == ProductDetailStatus.Expired,
            line.Assessment.Status != ProductDetailStatus.Current);
    }

    public static string KindLabel(ProductDetailKind kind) => kind switch
    {
        ProductDetailKind.Eligibility => "Eligibility",
        ProductDetailKind.Purchase => "Purchase",
        ProductDetailKind.VisitValidity => "Visit validity",
        ProductDetailKind.BringAFriend => "Bring-A-Friend",
        ProductDetailKind.Incentive => "Incentive",
        ProductDetailKind.Restriction => "Restriction",
        ProductDetailKind.Fee => "Fees & charges",
        _ => "General terms",
    };

    public static string StatusLabel(ProductDetailStatus status) => status switch
    {
        ProductDetailStatus.Current => "Current",
        ProductDetailStatus.Expired => "Expired",
        ProductDetailStatus.Stale => "Previously verified",
        _ => "Unverified",
    };

    /// <summary>Purchase and redemption deadlines are always labeled separately; an inferred year says so.</summary>
    public static string? DeadlineText(ProductDetailStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var parts = new List<string>();
        if (statement.PurchaseDeadline is { } buy)
        {
            var from = buy.Source == DeadlineSource.CampaignBanner ? " (campaign end date)" : string.Empty;
            parts.Add(string.Create(Us, $"Buy by {buy.Date:MMMM d, yyyy}{from}{(buy.YearInferred ? " — year inferred" : string.Empty)}"));
        }

        if (statement.RedemptionDeadline is { } use)
        {
            parts.Add(string.Create(Us, $"Use by {use.Date:MMMM d, yyyy}{(use.YearInferred ? " — year inferred" : string.Empty)}"));
        }

        if (statement.ValidityWindow is { } window)
        {
            parts.Add(string.Create(Us, $"Valid {window.Start:MMMM d, yyyy} – {window.End:MMMM d, yyyy}"));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string ChannelPageName(OfficialProductChannel channel) => channel switch
    {
        OfficialProductChannel.SeasonPasses => "Memberships & Season Passes",
        OfficialProductChannel.MilitaryFirstResponder => "Military & First Responders",
        _ => "Special Offers",
    };
}
