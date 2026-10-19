using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Dashboard;

/// <summary>What kind of admission price (if any) the Dashboard can truthfully headline for today (Phase 22C).</summary>
public enum DashboardAdmissionKind
{
    /// <summary>No single, unambiguous official general-admission price exists for today.</summary>
    NotAvailable,

    /// <summary>The official dated price of the ordinary "Park Ticket" for exactly today's operating date.</summary>
    ExactDated,

    /// <summary>No exact dated price; the official advertised "Starting From" price of the same "Park Ticket" product.</summary>
    StartingFrom,
}

/// <summary>Why <see cref="DashboardAdmissionKind.NotAvailable"/> was chosen — lets the Dashboard say something honest instead of a bare "unavailable".</summary>
public enum DashboardAdmissionUnavailableReason
{
    None,

    /// <summary>No official "Park Ticket" row with a price exists for today (see <see cref="DashboardAdmissionSummary.CategoryState"/> for the dated source's own status).</summary>
    NoProduct,

    /// <summary>More than one distinct official "Park Ticket" price exists for today and the source does not say which is the general one — never guessed.</summary>
    Ambiguous,

    /// <summary>The operating date is a historical date (Phase 21A): today's catalog prices are never shown for it.</summary>
    Historical,
}

/// <summary>
/// Phase 22C: the Dashboard's one-line admission reference for today's Eastern operating date — a pure
/// projection over the already-composed <see cref="AssociateWorkspace.Products"/> (no new retrieval, no
/// persistence). <see cref="PriceAmount"/> is always the <em>official</em> amount, never a draft's manual
/// display override, so a manual draft change can never be presented as today's official price.
/// </summary>
/// <param name="ProductName">The official product's own display name (e.g. "Park Ticket"), so a price is never shown without its product.</param>
/// <param name="CategoryState">The dated admission source's own status for today (the "why" behind a missing exact price).</param>
/// <param name="CategoryLastCheckedAt">Most recent attempt of any outcome for the dated admission source — never presented as "verified".</param>
public sealed record DashboardAdmissionSummary(
    DashboardAdmissionKind Kind,
    DashboardAdmissionUnavailableReason UnavailableReason,
    string? ProductName,
    decimal? PriceAmount,
    DateOnly? ApplicableDate,
    AssociateVerificationState PriceState,
    DateTimeOffset? LastVerifiedAt,
    AssociateVerificationState CategoryState,
    DateTimeOffset? CategoryLastCheckedAt)
{
    public static DashboardAdmissionSummary NotChecked { get; } = new(
        DashboardAdmissionKind.NotAvailable,
        DashboardAdmissionUnavailableReason.NoProduct,
        ProductName: null,
        PriceAmount: null,
        ApplicableDate: null,
        AssociateVerificationState.NotCheckedYet,
        LastVerifiedAt: null,
        AssociateVerificationState.NotCheckedYet,
        CategoryLastCheckedAt: null);
}

/// <summary>
/// Phase 22C admission product policy — deliberately narrow, no heuristics beyond identity:
/// <list type="number">
/// <item>Only the ordinary single-day general-admission product, identified by its official name
/// "Park Ticket" (<see cref="GeneralAdmissionNames"/>). Bundles ("Ticket + Meal…", "Ticket + Fast Lane…"),
/// Fast Lane, parking, add-ons, passes and Military/First Responder tickets are never candidates, and no
/// "lowest price" is ever picked across different products.</item>
/// <item>Exact first: an official (non-custom) <see cref="SourceCategory.DatedAdmissionTickets"/> row
/// whose <see cref="PriceKind.ExactDated"/> price applies to exactly <c>operatingDate</c>.</item>
/// <item>Otherwise the same product's official advertised <see cref="PriceKind.StartingFrom"/> price
/// (never relabeled as today's price).</item>
/// <item>Two or more distinct candidate prices of the same kind → <see cref="DashboardAdmissionUnavailableReason.Ambiguous"/>,
/// never a choice. A historical workspace never yields a price (Phase 21A).</item>
/// </list>
/// </summary>
public static class DashboardAdmissionSelector
{
    /// <summary>Normalized official names of the ordinary single-day ticket (the dated API's package name and the checkout page's card title).</summary>
    public static readonly IReadOnlySet<string> GeneralAdmissionNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "park ticket",
        "carowinds park ticket",
    };

    private static readonly SourceCategory[] NeverGeneralAdmission =
    [
        SourceCategory.MilitaryFirstResponder,
        SourceCategory.DatedMilitaryFirstResponder,
        SourceCategory.DatedFastLane,
        SourceCategory.AddOns,
        SourceCategory.SeasonPasses,
    ];

    public static DashboardAdmissionSummary Select(AssociateWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var categoryStatus = workspace.CategoryStatuses.FirstOrDefault(status => status.Category == SourceCategory.DatedAdmissionTickets);
        var categoryState = categoryStatus?.State ?? AssociateVerificationState.NotCheckedYet;
        var categoryCheckedAt = categoryStatus?.LastCheckedAt;

        DashboardAdmissionSummary Unavailable(DashboardAdmissionUnavailableReason reason) => new(
            DashboardAdmissionKind.NotAvailable, reason, ProductName: null, PriceAmount: null, ApplicableDate: null,
            categoryState, LastVerifiedAt: null, categoryState, categoryCheckedAt);

        if (workspace.PricingTimeframe == AssociatePricingTimeframe.Historical)
        {
            return Unavailable(DashboardAdmissionUnavailableReason.Historical);
        }

        var generalAdmission = workspace.Products
            .Where(product => !product.IsCustom
                && product.OfficialPriceAmount is not null
                && product.Category is { } category
                && !NeverGeneralAdmission.Contains(category)
                && IsGeneralAdmissionName(product.DisplayName))
            .ToList();

        var exact = generalAdmission
            .Where(product => product.Category == SourceCategory.DatedAdmissionTickets
                && product.PriceKind == PriceKind.ExactDated
                && product.DateSpecific
                && product.ApplicableDate == workspace.OperatingDate)
            .ToList();

        if (exact.Count > 0)
        {
            return FromCandidates(exact, DashboardAdmissionKind.ExactDated, categoryState, categoryCheckedAt, Unavailable);
        }

        var startingFrom = generalAdmission.Where(product => product.PriceKind == PriceKind.StartingFrom).ToList();
        return startingFrom.Count > 0
            ? FromCandidates(startingFrom, DashboardAdmissionKind.StartingFrom, categoryState, categoryCheckedAt, Unavailable)
            : Unavailable(DashboardAdmissionUnavailableReason.NoProduct);
    }

    public static bool IsGeneralAdmissionName(string displayName) =>
        GeneralAdmissionNames.Contains(ProductDisplayIdentity.FromDisplayName(displayName).NormalizedDisplayName);

    private static DashboardAdmissionSummary FromCandidates(
        List<AssociateProduct> candidates,
        DashboardAdmissionKind kind,
        AssociateVerificationState categoryState,
        DateTimeOffset? categoryCheckedAt,
        Func<DashboardAdmissionUnavailableReason, DashboardAdmissionSummary> unavailable)
    {
        if (candidates.Select(product => product.OfficialPriceAmount).Distinct().Count() > 1)
        {
            return unavailable(DashboardAdmissionUnavailableReason.Ambiguous);
        }

        // Same product, same price (e.g. the dated row and an identical duplicate) — the most recently
        // verified one carries the freshness evidence.
        var chosen = candidates.OrderByDescending(product => product.Verification.LastVerifiedAt ?? DateTimeOffset.MinValue).First();

        return new DashboardAdmissionSummary(
            kind,
            DashboardAdmissionUnavailableReason.None,
            chosen.DisplayName.Trim(),
            chosen.OfficialPriceAmount,
            kind == DashboardAdmissionKind.ExactDated ? chosen.ApplicableDate : null,
            chosen.Verification.State,
            chosen.Verification.LastVerifiedAt,
            categoryState,
            categoryCheckedAt);
    }
}
