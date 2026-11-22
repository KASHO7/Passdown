using System.Globalization;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Promotions;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>Phase 31: one official deal as displayed on the Products screen.</summary>
public sealed record DealItemPresentation(
    string Title,
    string? Badge,
    string CategoryText,
    string? PriceText,
    string? RegularPriceText,
    string? SavingsText,
    IReadOnlyList<string> OptionLines,
    IReadOnlyList<string> DateLines,
    IReadOnlyList<string> TermLines,
    IReadOnlyList<string> WarningLines,
    string StatusText,
    string VerifiedText,
    IReadOnlyList<Uri> SourceUrls)
{
    // Phase 31C: compact-row fields (the full detail stays in the lists above and the View details dialog).

    /// <summary>The row's price: the advertised price or official benefit wording, "From $x" across several priced options, or null.</summary>
    public string? RowPriceText { get; init; }

    /// <summary>One short official description (the card's subtitle, or its purchase options), or null.</summary>
    public string? ShortDescription { get; init; }

    /// <summary>A confirmed (never inferred) key date for the row: "Buy by …", or "On sale …" for an upcoming offer.</summary>
    public string? KeyDeadlineText { get; init; }

    /// <summary>Short status word for rows outside Current Deals ("Upcoming", "Previously verified", "Expired"); null when current.</summary>
    public string? RowStatusLabel { get; init; }

    /// <summary>A one-line pointer to something the dialog explains (sources disagree, unreadable last check), or null.</summary>
    public string? NoticeText { get; init; }

    public string? Subtitle { get; init; }

    public IReadOnlyList<string> PurchaseDateLines { get; init; } = [];

    public IReadOnlyList<string> VisitLines { get; init; } = [];

    public IReadOnlyList<string> RestrictionLines { get; init; } = [];

    public IReadOnlyList<string> FeeLines { get; init; } = [];

    /// <summary>Contradictions between official sources (a subset of <see cref="WarningLines"/>).</summary>
    public IReadOnlyList<string> DiscrepancyLines { get; init; } = [];

    public bool HasInferredDates { get; init; }
}

/// <summary>
/// Phase 31: the Products screen's Deals panel — Current Deals, Upcoming, Previously verified (not
/// re-confirmed), and Expired — with an explicit status line and empty state so the section is never
/// blank without explanation. Pure formatting of <see cref="DealsOverview"/>.
/// </summary>
public sealed record DealsPresentation(
    IReadOnlyList<DealItemPresentation> Current,
    IReadOnlyList<DealItemPresentation> Upcoming,
    IReadOnlyList<DealItemPresentation> PreviouslyVerified,
    IReadOnlyList<DealItemPresentation> Expired,
    string StatusText,
    bool IsStatusWarning,
    string? EmptyStateText)
{
    private static readonly CultureInfo Us = PresentationCulture.UnitedStates;

    public static DealsPresentation Build(DealsOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        var status = overview.Status;
        var (statusText, warning) = status.Freshness switch
        {
            OfficialDocumentFreshness.NotChecked => ("Deals not checked yet. They are checked with the other official data at startup, on Check Online, and on the daily schedule.", true),
            OfficialDocumentFreshness.LastCheckFailed when status.HasVerifiedData =>
                ($"Last check failed {SeasonContextPresentation.When(status.LastAttemptAt)}; showing previously verified offers (verified {SeasonContextPresentation.When(status.LastSuccessAt)}).", true),
            OfficialDocumentFreshness.LastCheckFailed => ($"Last deals check failed {SeasonContextPresentation.When(status.LastAttemptAt)}: {status.LastError}", true),
            _ => ($"Official deals checked {SeasonContextPresentation.When(status.LastSuccessAt)}.", false),
        };

        string? empty = null;
        if (overview.Current.Count == 0)
        {
            empty = status.Freshness switch
            {
                OfficialDocumentFreshness.NotChecked => "Deals not checked yet.",
                OfficialDocumentFreshness.LastCheckFailed when !status.HasVerifiedData => "Last check failed; no previously verified offers to show.",
                _ => "No current offers verified on the official Carowinds pages.",
            };
        }

        return new DealsPresentation(
            overview.Current.Select(Item).ToList(),
            overview.Upcoming.Select(Item).ToList(),
            overview.PreviouslyVerified.Select(Item).ToList(),
            overview.Expired.Select(Item).ToList(),
            statusText,
            warning,
            empty);
    }

    private static DealItemPresentation Item(DealEntry entry)
    {
        var p = entry.Promotion;

        var price = p.AdvertisedPriceText is { } text
            ? string.Join(" ", new[] { p.PriceQualifier, text, p.PriceUnitText }.Where(s => !string.IsNullOrWhiteSpace(s)))
            : null;

        var options = p.PriceOptions
            .Select(o => string.Join(" — ", new[] { o.Title, o.PriceText is null ? null : o.PriceText + (o.PriceUnitText ?? string.Empty), o.Subtitle, o.SavingsText }
                .Where(s => !string.IsNullOrWhiteSpace(s))))
            .ToList();

        var dates = new List<string>();
        var visit = new List<string>();
        if (p.PurchaseStart is { } start)
        {
            dates.Add($"On sale from {Date(start.Date)} (\"{start.SourceText}\")");
        }

        if (p.PurchaseDeadline is { } deadline)
        {
            dates.Add(deadline.InferenceNote() is { } note
                ? $"Purchase by {Date(deadline.Date)} ({note}; \"{deadline.SourceText}\")"
                : $"Purchase by {Date(deadline.Date)} (\"{deadline.SourceText}\")");
        }

        if (p.CampaignEnd is { } campaign && (p.PurchaseDeadline is null || campaign.Date != p.PurchaseDeadline.Date))
        {
            dates.Add($"Page banner (inferred end date, not stated in this offer's own terms): \"{campaign.SourceText}\"{(campaign.YearInferred ? $" — read as {Date(campaign.Date)}, year not stated" : string.Empty)}");
        }

        if (p.PurchaseDeadline is null && p.CampaignEnd is null)
        {
            dates.Add("No purchase deadline is stated on the official page.");
        }

        var purchaseDates = dates.ToList();
        if (p.RedemptionWindow is { } window)
        {
            var line = $"Use/visit window: {Date(window.Start)} – {Date(window.End)} (\"{window.SourceText}\")";
            dates.Insert(dates.Count - (p.PurchaseDeadline is null && p.CampaignEnd is null ? 1 : 0), line);
            visit.Add(line);
        }

        visit.AddRange(p.ValidityNotes);

        var terms = p.ValidityNotes.Concat(p.Terms).Concat(p.FeeDisclosures).Distinct(StringComparer.Ordinal).ToList();

        var warnings = new List<string>(p.Discrepancies);
        if (p.LastCheckUnreadable)
        {
            warnings.Add("The latest official check could not read this offer; details are from its last successful verification.");
        }

        var rowStatus = entry.Assessment.Status switch
        {
            PromotionStatus.Expired => "Expired",
            PromotionStatus.Unknown => "Previously verified",
            PromotionStatus.Upcoming => "Upcoming",
            _ => (string?)null,
        };

        var statusText = entry.Assessment.Status switch
        {
            PromotionStatus.Expired => "Offer has expired — " + entry.Assessment.Reason,
            PromotionStatus.Unknown => "Previously verified — " + entry.Assessment.Reason,
            PromotionStatus.Upcoming => "Upcoming — " + entry.Assessment.Reason,
            _ => "Current — " + entry.Assessment.Reason,
        };

        return new DealItemPresentation(
            p.Title,
            p.Highlight,
            CategoryText(p.Category),
            price,
            p.RegularPriceAmount is { } regular ? $"Regular {regular.ToString("C0", Us)}" : null,
            p.SavingsText,
            options,
            dates,
            terms,
            warnings,
            statusText,
            $"Last verified {SeasonContextPresentation.When(p.LastVerifiedAt)}",
            p.SourceUrls.Count > 0 ? p.SourceUrls : [p.PrimarySourceUrl])
        {
            RowPriceText = price ?? FromPrice(p.PriceOptions),
            ShortDescription = ShortDescription(p),
            KeyDeadlineText = KeyDeadline(p, entry.Assessment.Status),
            RowStatusLabel = rowStatus,
            NoticeText = p.Discrepancies.Count > 0
                ? "Official sources disagree — see details."
                : p.LastCheckUnreadable ? "The latest check could not read this offer — see details." : null,
            Subtitle = p.Subtitle,
            PurchaseDateLines = purchaseDates,
            VisitLines = visit,
            RestrictionLines = p.Terms.Distinct(StringComparer.Ordinal).ToList(),
            FeeLines = p.FeeDisclosures.Distinct(StringComparer.Ordinal).ToList(),
            DiscrepancyLines = p.Discrepancies,
            HasInferredDates = p.CampaignEnd is not null || p.PurchaseDeadline?.IsInferred() == true || p.PurchaseStart?.IsInferred() == true,
        };
    }

    /// <summary>
    /// Phase 31C: one price for several purchase options — the single option's own price text, or
    /// "From" the lowest explicit amount when two or more options carry one. Never computed from fees
    /// or guessed; null when the source states no amount.
    /// </summary>
    internal static string? FromPrice(IReadOnlyList<PromotionPriceOption> options)
    {
        var priced = options.Where(o => o.PriceAmount is not null && !string.IsNullOrWhiteSpace(o.PriceText)).ToList();
        if (priced.Count == 0)
        {
            return null;
        }

        if (priced.Count == 1)
        {
            return priced[0].PriceText;
        }

        var lowest = priced.MinBy(o => o.PriceAmount!.Value)!;
        return priced.All(o => o.PriceAmount == lowest.PriceAmount) ? lowest.PriceText : "From " + lowest.PriceText;
    }

    private static string? ShortDescription(OfficialPromotion p)
    {
        if (!string.IsNullOrWhiteSpace(p.Subtitle))
        {
            return p.Subtitle.Trim();
        }

        return p.PriceOptions.Count switch
        {
            0 => null,
            1 => string.Join(" — ", new[] { p.PriceOptions[0].Title, p.PriceOptions[0].Subtitle }.Where(s => !string.IsNullOrWhiteSpace(s))),
            _ => $"{p.PriceOptions.Count} purchase options: {string.Join(", ", p.PriceOptions.Select(o => o.Title))}",
        };
    }

    /// <summary>Only a date stated in full in the offer's own terms is promoted to the row.</summary>
    private static string? KeyDeadline(OfficialPromotion p, PromotionStatus status)
    {
        if (status == PromotionStatus.Upcoming && p.PurchaseStart is { } start && !start.IsInferred())
        {
            return "On sale " + Date(start.Date);
        }

        return p.PurchaseDeadline is { } deadline && !deadline.IsInferred() ? "Buy by " + Date(deadline.Date) : null;
    }

    private static string Date(DateOnly date) => date.ToString("MMM d, yyyy", Us);

    private static string CategoryText(PromotionCategory category) => category switch
    {
        PromotionCategory.SeasonPass => "Season pass",
        PromotionCategory.Bundle => "Bundle",
        PromotionCategory.DailyTicket => "Daily ticket",
        PromotionCategory.Lodging => "Lodging",
        PromotionCategory.Membership => "Membership",
        PromotionCategory.Admission => "Admission",
        _ => "Offer",
    };
}
