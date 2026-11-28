using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Dashboard;

/// <summary>One official dated price shown in the Dashboard's "Daily Ticket Pricing" list (Phase 35E Dashboard).</summary>
/// <param name="ProductName">The official product's own display name.</param>
/// <param name="OfficialPriceAmount">Always the official amount — never a draft's manual display override.</param>
/// <param name="State">The product's own verification state (a stale price stays marked stale).</param>
/// <param name="LastVerifiedAt">Success-only verification time of this price, or null.</param>
public sealed record DashboardPriceItem(
    string ProductName,
    decimal OfficialPriceAmount,
    ProductDisplaySection Section,
    AssociateVerificationState State,
    DateTimeOffset? LastVerifiedAt);

/// <summary>
/// Phase 35E Dashboard: today's official dated ticket and Fast Lane prices, a pure projection over the
/// already-composed <see cref="AssociateWorkspace.Products"/> (no retrieval, no persistence, no new
/// pricing rule). <see cref="Items"/> is bounded to <see cref="MaxItems"/>; <see cref="TotalCount"/> is the
/// true number so the Dashboard can say how many more Products holds.
/// </summary>
public sealed record DashboardPricingSummary(IReadOnlyList<DashboardPriceItem> Items, int TotalCount)
{
    /// <summary>A summary surface, not the Products list: the rest is one click away in Products.</summary>
    public const int MaxItems = 5;

    public static DashboardPricingSummary Empty { get; } = new([], 0);

    public bool HasItems => Items.Count > 0;
}

/// <summary>
/// Phase 35E Dashboard pricing policy — identity only, no heuristics:
/// <list type="number">
/// <item>Only official (non-custom) rows whose Products section is Daily Admission or Fast Lane
/// (<see cref="ProductDisplaySectionResolver"/>, the same grouping the Products screen uses).</item>
/// <item>Only an exact dated price (<see cref="PriceKind.ExactDated"/>) that applies to exactly the
/// workspace's operating date — an advertised "Starting From" price is never listed as today's price
/// (the Dashboard falls back to <see cref="DashboardAdmissionSummary"/> for that).</item>
/// <item>Never for a historical workspace (Phase 21A).</item>
/// <item>Order: Daily Admission rows, then Fast Lane, each in the workspace's own product order;
/// an exact duplicate (same name and price) is listed once.</item>
/// </list>
/// </summary>
public static class DashboardPricingSelector
{
    public static DashboardPricingSummary Select(AssociateWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (workspace.PricingTimeframe == AssociatePricingTimeframe.Historical)
        {
            return DashboardPricingSummary.Empty;
        }

        var candidates = workspace.Products
            .Where(product => !product.IsCustom
                && product.Category is not null
                && product.OfficialPriceAmount is not null
                && product.PriceKind == PriceKind.ExactDated
                && product.DateSpecific
                && product.ApplicableDate == workspace.OperatingDate)
            .Select(product => (Product: product, Section: ProductDisplaySectionResolver.Resolve(product)))
            .Where(entry => entry.Section is ProductDisplaySection.DailyAdmission or ProductDisplaySection.FastLane)
            .Select((entry, index) => (entry.Product, Section: entry.Section!.Value, Index: index))
            .OrderBy(entry => entry.Section == ProductDisplaySection.DailyAdmission ? 0 : 1)
            .ThenBy(entry => entry.Index)
            .Select(entry => new DashboardPriceItem(
                entry.Product.DisplayName.Trim(),
                entry.Product.OfficialPriceAmount!.Value,
                entry.Section,
                entry.Product.Verification.State,
                entry.Product.Verification.LastVerifiedAt))
            .DistinctBy(item => (item.ProductName.ToUpperInvariant(), item.OfficialPriceAmount))
            .ToList();

        return new DashboardPricingSummary(candidates.Take(DashboardPricingSummary.MaxItems).ToList(), candidates.Count);
    }
}
