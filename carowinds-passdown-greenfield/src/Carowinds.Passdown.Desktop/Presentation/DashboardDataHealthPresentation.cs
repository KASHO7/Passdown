using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>One compact "N Label" chip in the Data Status summary row (Phase 10D Part K), e.g. "5 Current".</summary>
public sealed record DashboardCountChip(string Label, string CountText);

/// <summary>
/// One compact source row in the Data Status breakdown (Phase 10D Part H/S). Never a raw exception,
/// URL, or HTTP status (Part T) — <see cref="StatusText"/>/<see cref="TimestampText"/> come only
/// from <see cref="DashboardSourceStatusEntry"/>'s own enum/timestamp fields.
/// </summary>
public sealed record DashboardSourceStatusPresentation(string CategoryLabel, string StatusText, string? TimestampText);

/// <summary>
/// Turns a <see cref="DashboardDataHealthSummary"/> into display-ready strings (Phase 10D Part R/S) —
/// no business rule (freshness, precedence, staleness) is recomputed here; every value already
/// exists on the summary. Sources are split into the same two groups the Data Status card shows
/// (Part I): "Today's Operation" (Park Hours, Admission, Fast Lane, Haunted) and "Products / Offers"
/// (Passes, Add-Ons, Promotions).
/// </summary>
public sealed record DashboardDataHealthPresentation(
    string OverallHeadline,
    string OverallDetailText,
    IReadOnlyList<DashboardCountChip> Counts,
    string? LastVerifiedText,
    IReadOnlyList<DashboardSourceStatusPresentation> OperationalSources,
    IReadOnlyList<DashboardSourceStatusPresentation> ProductSources)
{
    private static readonly SourceCategory[] OperationalCategories =
    [
        SourceCategory.ParkHours,
        SourceCategory.DatedAdmissionTickets,
        SourceCategory.DatedFastLane,
        SourceCategory.HauntedAttractions,
    ];

    private static readonly SourceCategory[] ProductCategories =
    [
        SourceCategory.SeasonPasses,
        SourceCategory.AddOns,
        SourceCategory.Promotions,
    ];

    public static DashboardDataHealthPresentation Build(DashboardDataHealthSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var (headline, detail) = summary.OverallStatus switch
        {
            DashboardOverallDataStatus.Current => ("Current", "All source data is current."),
            DashboardOverallDataStatus.SomeStale => ("Some information is stale", Pluralize(summary.StaleCount, "source is", "sources are") + " showing older information."),
            DashboardOverallDataStatus.SomeUnavailable => ("Some data unavailable", Pluralize(summary.UnavailableCount, "source is", "sources are") + " unavailable."),
            DashboardOverallDataStatus.VerificationIssue => ("Verification issue", Pluralize(summary.VerificationIssueCount, "source could", "sources could") + " not be verified."),
            DashboardOverallDataStatus.NoPublishedData => ("No published data yet", "Nothing has been published for today yet."),
            _ => ("Status unknown", string.Empty),
        };

        var counts = new List<DashboardCountChip> { new("Current", summary.CurrentCount.ToString()) };
        AddIfNonZero(counts, "Stale", summary.StaleCount);
        AddIfNonZero(counts, "Unavailable", summary.UnavailableCount);
        AddIfNonZero(counts, "Not published", summary.NotPublishedCount);
        AddIfNonZero(counts, "Issues", summary.VerificationIssueCount);
        AddIfNonZero(counts, "Not checked", summary.NotCheckedCount);

        var lastVerifiedText = summary.LatestSuccessfulVerification is { } verifiedAt
            ? $"Last verified {OperationsDashboardPresentation.FormatTimestamp(verifiedAt)}"
            : null;

        var byCategory = summary.Sources.ToDictionary(s => s.Category);
        var operational = OperationalCategories.Select(c => BuildSourceRow(byCategory[c])).ToList();
        var products = ProductCategories.Select(c => BuildSourceRow(byCategory[c])).ToList();

        return new DashboardDataHealthPresentation(headline, detail, counts, lastVerifiedText, operational, products);
    }

    private static void AddIfNonZero(List<DashboardCountChip> counts, string label, int count)
    {
        if (count > 0)
        {
            counts.Add(new DashboardCountChip(label, count.ToString()));
        }
    }

    private static string Pluralize(int count, string singular, string plural) => count == 1 ? $"1 {singular}" : $"{count} {plural}";

    private static DashboardSourceStatusPresentation BuildSourceRow(DashboardSourceStatusEntry entry)
    {
        var statusText = entry.Status switch
        {
            DashboardSourceStatus.Current => "Current",
            DashboardSourceStatus.Stale => "Stale",
            DashboardSourceStatus.NotPublished => "Not published",
            DashboardSourceStatus.Unavailable => "Unavailable",
            DashboardSourceStatus.VerificationIssue => "Verification issue",
            _ => "Not checked yet",
        };

        // Never both: LastVerifiedAt (success-only) is preferred and clearly labeled "Last verified";
        // LastCheckedAt (attempt-only, the three dated categories' only available timestamp) is
        // labeled "Last checked" instead — never presented as a verification (Phase 10D Part J).
        var timestampText = entry.LastVerifiedAt is { } verifiedAt
            ? $"Last verified {OperationsDashboardPresentation.FormatTimestamp(verifiedAt)}"
            : entry.LastCheckedAt is { } checkedAt
                ? $"Last checked {OperationsDashboardPresentation.FormatTimestamp(checkedAt)}"
                : null;

        return new DashboardSourceStatusPresentation(SourceCategoryDisplay.FriendlyName(entry.Category), statusText, timestampText);
    }
}
