namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Phase 31C: one line in the View details dialog — the official wording, plus (for Important Details
/// lines) its status, deadline and the reason it is not current. Text is never rewritten here.
/// </summary>
public sealed record OfficialDetailsLine(
    string Text,
    string? StatusLabel = null,
    string? DeadlineText = null,
    string? Note = null,
    bool IsMuted = false)
{
    public bool HasStatus => StatusLabel is not null;

    public bool HasDeadline => DeadlineText is not null;

    public bool HasNote => Note is not null;

    /// <summary>" · Buy by …" after the status word, or empty (no dangling separator).</summary>
    public string DeadlineSuffix => DeadlineText is null ? string.Empty : " · " + DeadlineText;

    /// <summary>A deadline with no status word, shown on its own line.</summary>
    public bool ShowsPlainDeadline => StatusLabel is null && DeadlineText is not null;

    /// <summary>Never color-only: status and deadline are part of what Narrator reads.</summary>
    public string AutomationName => string.Join(". ", new[]
    {
        StatusLabel is null ? Text : $"{StatusLabel}: {Text}",
        DeadlineText,
        Note,
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Phase 31C: one compact, headed group in the View details dialog.</summary>
public sealed record OfficialDetailsSection(string Heading, IReadOnlyList<OfficialDetailsLine> Lines, bool IsWarning = false);

/// <summary>
/// Phase 31C: everything the View details dialog shows for one product or official offer. Built only
/// from data the app already verified (Phase 31 deals, Phase 31B Important Details, the product's own
/// official price fields); never fetched, never invented, never simplified in a way that changes meaning.
/// </summary>
public sealed record OfficialDetailsPresentation(
    string Title,
    string? Subtitle,
    string? PriceText,
    IReadOnlyList<string> PriceNotes,
    string? StatusText,
    bool IsStatusWarning,
    IReadOnlyList<OfficialDetailsSection> Sections,
    string? InferredDatesNote,
    string? EmptyTermsText,
    string VerifiedText,
    IReadOnlyList<Uri> SourceUrls)
{
    public const string NoTermsRecordedText = "No additional official terms have been recorded for this item. Check the official page for current terms.";

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public bool HasPriceText => !string.IsNullOrWhiteSpace(PriceText);

    public bool HasPriceNotes => PriceNotes.Count > 0;

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    public bool HasInferredDatesNote => InferredDatesNote is not null;

    public bool HasEmptyTermsText => EmptyTermsText is not null;

    public string DialogAutomationName => $"Details for {Title}";

    /// <summary>Phase 31C: the dialog content for an official deal (Current Deals and its other groups).</summary>
    public static OfficialDetailsPresentation ForDeal(DealItemPresentation deal)
    {
        ArgumentNullException.ThrowIfNull(deal);

        var priceNotes = new[] { deal.RegularPriceText, deal.SavingsText }.OfType<string>().ToList();
        var sections = new List<OfficialDetailsSection>();
        Add(sections, "Purchase options", deal.OptionLines);
        Add(sections, "Purchase dates", deal.PurchaseDateLines);
        Add(sections, "Validity and visit windows", deal.VisitLines);
        Add(sections, "Terms and restrictions", deal.RestrictionLines);
        Add(sections, "Fees", deal.FeeLines);
        Add(sections, "Official sources disagree", deal.DiscrepancyLines, isWarning: true);
        Add(sections, "Verification", deal.WarningLines.Except(deal.DiscrepancyLines, StringComparer.Ordinal).ToList(), isWarning: true);

        var hasTerms = deal.VisitLines.Count + deal.RestrictionLines.Count + deal.FeeLines.Count > 0;
        return new OfficialDetailsPresentation(
            deal.Title,
            string.Join(" · ", new[] { deal.Badge, deal.CategoryText, deal.Subtitle }.Where(s => !string.IsNullOrWhiteSpace(s))),
            deal.PriceText,
            priceNotes,
            deal.StatusText,
            deal.RowStatusLabel is not null,
            sections,
            deal.HasInferredDates ? ProductImportantDetailsPresentation.InferredDatesNoteText : null,
            hasTerms ? null : "No other terms were listed with this offer on the official page.",
            deal.VerifiedText,
            deal.SourceUrls);
    }

    /// <summary>
    /// Phase 31C: the dialog content for a regular product row — its official price fields as already
    /// shown, plus the Phase 31B Important Details grouped by kind, or an explicit "none recorded" note.
    /// </summary>
    public static OfficialDetailsPresentation ForProduct(
        string displayName,
        string categoryText,
        string officialPriceText,
        IReadOnlyList<string> priceNotes,
        string? verificationWarning,
        string fallbackVerifiedText,
        ProductImportantDetailsPresentation? details,
        Uri? fallbackSourceUrl)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(priceNotes);

        var sections = new List<OfficialDetailsSection>();
        string? statusText = verificationWarning;
        var statusWarning = verificationWarning is not null;
        if (details is not null)
        {
            foreach (var group in details.Lines.GroupBy(l => SectionOf(l.KindLabel)).OrderBy(g => g.Key.Order))
            {
                sections.Add(new OfficialDetailsSection(group.Key.Heading, group.Select(Line).ToList()));
            }

            if (details.HasDiscrepancies)
            {
                sections.Add(new OfficialDetailsSection("Official sources disagree", details.Discrepancies.Select(d => new OfficialDetailsLine(d)).ToList(), IsWarning: true));
            }

            if (details.HasPreviousTerms)
            {
                sections.Add(new OfficialDetailsSection("Earlier official terms", details.PreviousTermsLines.Select(t => new OfficialDetailsLine(t, IsMuted: true)).ToList()));
            }

            if (details.FreshnessWarning is { } freshness)
            {
                statusText = statusText is null ? freshness : statusText + " " + freshness;
                statusWarning = true;
            }
        }

        var sources = details is not null
            ? new List<Uri> { details.SourceUrl }
            : fallbackSourceUrl is not null ? [fallbackSourceUrl] : [];

        return new OfficialDetailsPresentation(
            displayName,
            categoryText,
            officialPriceText,
            priceNotes,
            statusText,
            statusWarning,
            sections,
            details?.InferredDatesNote,
            details is null || details.Lines.Count == 0 ? NoTermsRecordedText : null,
            details?.ProvenanceText ?? fallbackVerifiedText,
            sources);
    }

    private static OfficialDetailsLine Line(ProductDetailLinePresentation line) => new(
        line.Text,
        line.StatusLabel,
        line.DeadlineText,
        line.IsNotCurrent ? line.StatusReason : null,
        line.IsNotCurrent);

    private static void Add(List<OfficialDetailsSection> sections, string heading, IReadOnlyList<string> lines, bool isWarning = false)
    {
        if (lines.Count > 0)
        {
            sections.Add(new OfficialDetailsSection(heading, lines.Select(l => new OfficialDetailsLine(l)).ToList(), isWarning));
        }
    }

    /// <summary>Groups the Phase 31B kinds under the dialog's headings (order = reading order).</summary>
    private static (int Order, string Heading) SectionOf(string kindLabel) => kindLabel switch
    {
        "Eligibility" => (0, "Eligibility"),
        "Bring-A-Friend" or "Incentive" => (1, "Included benefits and perks"),
        "Purchase" => (2, "Purchase terms and deadlines"),
        "Visit validity" => (3, "Validity and visit windows"),
        "Restriction" => (4, "Exclusions and restrictions"),
        "Fees & charges" => (5, "Fees"),
        _ => (6, "Other official terms"),
    };
}
