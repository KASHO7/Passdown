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
    Uri SourceUrl,
    string? InferredDatesNote = null)
{
    public const string HeaderText = "Important Details & Deadlines";

    /// <summary>Shown under the lines whenever any displayed date is inferred.</summary>
    public const string InferredDatesNoteText =
        "Dates marked \"inferred\" are not stated in full as this item's own deadline on the official page. " +
        "They are shown for reference and are not used on their own to mark anything expired.";

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public bool HasSummary => SummaryText is not null;

    public bool HasDiscrepancies => Discrepancies.Count > 0;

    public bool HasFreshnessWarning => FreshnessWarning is not null;

    public bool HasPreviousTerms => PreviousTermsLines.Count > 0;

    public bool HasInferredDatesNote => InferredDatesNote is not null;

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
            source.PrimarySourceUrl,
            details.Lines.Any(l => HasInferredDate(l.Statement)) ? InferredDatesNoteText : null);
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

    private static bool HasInferredDate(ProductDetailStatement statement) =>
        statement.PurchaseDeadline?.IsInferred() == true || statement.RedemptionDeadline?.IsInferred() == true;

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

    /// <summary>
    /// Purchase and redemption deadlines are always labeled separately. A confirmed date (stated in full
    /// in the card's own terms) is shown plain; an inferred one says so and why, in its own words.
    /// </summary>
    public static string? DeadlineText(ProductDetailStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var parts = new List<string>();
        if (statement.PurchaseDeadline is { } buy)
        {
            parts.Add(string.Create(Us, $"Buy by {buy.Date:MMMM d, yyyy}{Inference(buy)}"));
        }

        if (statement.RedemptionDeadline is { } use)
        {
            parts.Add(string.Create(Us, $"Use by {use.Date:MMMM d, yyyy}{Inference(use)}"));
        }

        if (statement.ValidityWindow is { } window)
        {
            parts.Add(string.Create(Us, $"Valid {window.Start:MMMM d, yyyy} – {window.End:MMMM d, yyyy}"));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string Inference(OfficialDateStatement date) =>
        date.InferenceNote() is { } note ? $" ({note})" : string.Empty;

    private static string ChannelPageName(OfficialProductChannel channel) => channel switch
    {
        OfficialProductChannel.SeasonPasses => "Memberships & Season Passes",
        OfficialProductChannel.MilitaryFirstResponder => "Military & First Responders",
        _ => "Special Offers",
    };
}
