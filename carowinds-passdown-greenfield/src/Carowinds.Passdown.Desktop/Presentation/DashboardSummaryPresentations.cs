using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.PassPerks;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>Phase 35E Dashboard: one row of "Daily Ticket Pricing" — always an official exact dated price for today.</summary>
public sealed record DashboardPriceRowPresentation(string ProductName, string PriceText, bool IsStale)
{
    public string AccessibleName => IsStale
        ? $"{ProductName}, {PriceText}, may be out of date"
        : $"{ProductName}, {PriceText}";
}

/// <summary>
/// Phase 35E Dashboard: display strings for today's official dated price list
/// (<see cref="DashboardPricingSummary"/>). Only official amounts appear (never a draft override); a price
/// that could not be rechecked says so in visible text; the verification time is success-only. When there
/// are no rows the Dashboard shows the existing <see cref="DashboardAdmissionPresentation"/> instead, which
/// already explains honestly why no price for today is available.
/// </summary>
public sealed record DashboardPricingPresentation(
    IReadOnlyList<DashboardPriceRowPresentation> Rows,
    string? FreshnessText,
    bool IsStale,
    string? MoreText)
{
    public bool HasRows => Rows.Count > 0;

    public bool HasNoRows => Rows.Count == 0;

    public bool HasFreshnessText => FreshnessText is not null;

    public bool HasMoreText => MoreText is not null;

    public static DashboardPricingPresentation Empty { get; } = new([], null, false, null);

    public static DashboardPricingPresentation Build(DashboardPricingSummary summary, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (!summary.HasItems)
        {
            return Empty;
        }

        var rows = summary.Items
            .Select(item => new DashboardPriceRowPresentation(item.ProductName, PriceFormatting.Format(item.OfficialPriceAmount), item.State == AssociateVerificationState.Stale))
            .ToList();

        var isStale = rows.Any(row => row.IsStale);
        var verified = summary.Items.Where(item => item.LastVerifiedAt.HasValue).Select(item => item.LastVerifiedAt!.Value).ToList();
        string? freshness = isStale
            ? "Some prices could not be rechecked — may be out of date"
            : verified.Count > 0 ? $"Verified {EasternTimestampFormat.Relative(verified.Min(), nowUtc)}" : null;

        var more = summary.TotalCount - rows.Count;
        return new DashboardPricingPresentation(rows, freshness, isStale, more > 0 ? $"{more} more in Products" : null);
    }
}

/// <summary>Phase 35E Dashboard: one labelled official date of a Pass Perk ("Redemption period" — "Dec 5 – Jan 3").</summary>
public sealed record DashboardPerkDatePresentation(string Label, string Value);

/// <summary>
/// Phase 35E Dashboard: one advertised Pass Perk. Qualifying, booking and redemption dates stay separate,
/// labelled lines (Phase 32) — never collapsed into one status — and nothing says a guest has earned it.
/// </summary>
public sealed record DashboardPassPerkPresentation(
    string Title,
    IReadOnlyList<DashboardPerkDatePresentation> Dates,
    string? DetailText,
    string? StatusLabel,
    string? NoticeText,
    string AccessibleSummary)
{
    public bool HasDetailText => DetailText is not null;

    public bool HasStatusLabel => StatusLabel is not null;

    public bool HasNoticeText => NoticeText is not null;
}

/// <summary>
/// Phase 35E Dashboard: the operating month's Pass Perks from the cached official document only
/// (<see cref="PassPerksOverview"/>); status per perk is the existing Phase 32 evaluator's. Expired and
/// no-longer-listed perks are left to Products; an unverified month is "not verified", never "no perks".
/// </summary>
public sealed record DashboardPassPerksPresentation(
    string Heading,
    IReadOnlyList<DashboardPassPerkPresentation> Items,
    string? EmptyText,
    string? StatusText,
    bool IsStatusWarning,
    string? MoreText)
{
    /// <summary>Bounded preview; the full list (and View details) is in Products.</summary>
    public const int MaxItems = 2;

    public const string PublicListingNote = "Publicly advertised perks — not what an individual pass has earned.";

    public bool HasItems => Items.Count > 0;

    public bool HasEmptyText => EmptyText is not null;

    public bool HasStatusText => StatusText is not null;

    public bool HasMoreText => MoreText is not null;

    public static DashboardPassPerksPresentation Build(PassPerksOverview? overview, bool loadFailed, DateOnly operatingDate)
    {
        var monthName = operatingDate.ToString("MMMM", PresentationCulture.UnitedStates);
        var heading = $"{monthName} Pass Perks";

        if (loadFailed)
        {
            return new DashboardPassPerksPresentation(heading, [], "Saved Pass Perks could not be read.", null, true, null);
        }

        if (overview is null)
        {
            return new DashboardPassPerksPresentation(heading, [], PassPerksPresentation.NotCheckedText, null, false, null);
        }

        var (statusText, isWarning) = PassPerksPresentation.Status(overview.Status);
        var view = overview.ForMonth(operatingDate.Year, operatingDate.Month);
        var entries = view.Entries
            .Where(entry => entry.Assessment.Status is PassPerkStatus.Current or PassPerkStatus.Upcoming or PassPerkStatus.NotReconfirmed)
            .ToList();

        string? empty = !view.WasObserved
            ? $"{monthName}'s Pass Perks have not been verified."
            : entries.Count == 0 ? $"No current Pass Perks are listed for {monthName}." : null;

        var items = entries.Take(MaxItems).Select(entry => Item(entry, overview.ProgramTerms, operatingDate)).ToList();
        var more = entries.Count - items.Count;
        return new DashboardPassPerksPresentation(heading, items, empty, statusText, isWarning, more > 0 ? $"{more} more in Products" : null);
    }

    public static DashboardPassPerkPresentation Item(PassPerkEntry entry, PassPerksProgramTerms? terms, DateOnly operatingDate)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var perk = entry.Perk;
        var shared = PassPerksPresentation.Item(entry, terms);

        var dates = new List<DashboardPerkDatePresentation>();
        if (perk.QualifyingPeriod is { } qualifying)
        {
            dates.Add(new("Last qualifying visit", Day(qualifying.End, operatingDate)));
        }

        if (perk.DateOf(PassPerkDateRole.BookingDeadline) is { } booking)
        {
            dates.Add(new("Book by", Day(booking.End, operatingDate)));
        }

        if (perk.Redemption is { } redemption)
        {
            dates.Add(new("Redemption period", PassPerkCalendar.IsMonthLong(perk, redemption)
                ? "All " + perk.Campaign.FirstDay.ToString("MMMM", PresentationCulture.UnitedStates)
                : redemption.Start is { } start ? $"{Short(start)} – {Short(redemption.End)}" : "Through " + Short(redemption.End)));
        }

        var detail = dates.Count == 0 ? shared.RequirementText : null;
        var parts = new List<string> { perk.Title };
        parts.AddRange(dates.Select(date => $"{date.Label}: {date.Value}"));
        if (detail is not null)
        {
            parts.Add(detail);
        }

        if (shared.RowStatusLabel is not null)
        {
            parts.Add(shared.RowStatusLabel);
        }

        if (shared.NoticeText is not null)
        {
            parts.Add(shared.NoticeText);
        }

        parts.Add(PublicListingNote.TrimEnd('.'));
        return new DashboardPassPerkPresentation(perk.Title, dates, detail, shared.RowStatusLabel, shared.NoticeText, string.Join(". ", parts) + ".");
    }

    private static string Short(DateOnly date) => date.ToString("MMM d", PresentationCulture.UnitedStates);

    private static string Day(DateOnly date, DateOnly operatingDate) => date == operatingDate
        ? "Today, " + date.ToString("MMMM d", PresentationCulture.UnitedStates)
        : Short(date);
}
