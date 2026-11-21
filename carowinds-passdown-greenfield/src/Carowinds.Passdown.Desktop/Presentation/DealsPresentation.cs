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
    IReadOnlyList<Uri> SourceUrls);

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
        if (p.PurchaseStart is { } start)
        {
            dates.Add($"On sale from {Date(start.Date)} (\"{start.SourceText}\")");
        }

        if (p.PurchaseDeadline is { } deadline)
        {
            dates.Add($"Purchase by {Date(deadline.Date)} (\"{deadline.SourceText}\")");
        }

        if (p.CampaignEnd is { } campaign && (p.PurchaseDeadline is null || campaign.Date != p.PurchaseDeadline.Date))
        {
            dates.Add($"Page banner: \"{campaign.SourceText}\"{(campaign.YearInferred ? $" (read as {Date(campaign.Date)})" : string.Empty)}");
        }

        if (p.RedemptionWindow is { } window)
        {
            dates.Add($"Use/visit window: {Date(window.Start)} – {Date(window.End)} (\"{window.SourceText}\")");
        }

        if (p.PurchaseDeadline is null && p.CampaignEnd is null)
        {
            dates.Add("No purchase deadline is stated on the official page.");
        }

        var terms = p.ValidityNotes.Concat(p.Terms).Concat(p.FeeDisclosures).Distinct(StringComparer.Ordinal).ToList();

        var warnings = new List<string>(p.Discrepancies);
        if (p.LastCheckUnreadable)
        {
            warnings.Add("The latest official check could not read this offer; details are from its last successful verification.");
        }

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
            p.SourceUrls.Count > 0 ? p.SourceUrls : [p.PrimarySourceUrl]);
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
