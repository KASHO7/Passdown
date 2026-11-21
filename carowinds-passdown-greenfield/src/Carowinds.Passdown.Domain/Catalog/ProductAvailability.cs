namespace Carowinds.Passdown.Domain.Catalog;

/// <summary>Phase 31B: what the app can honestly say about whether an official product is still sold.</summary>
public enum ProductAvailability
{
    /// <summary>Listed by the latest successful official check.</summary>
    Available,

    /// <summary>
    /// Not confirmed either way — missing from one check only, or sold through a dedicated, verified
    /// channel (e.g. ID.me-gated Military &amp; First Responder checkout) whose public page alone is not
    /// reliable evidence that the product ended.
    /// </summary>
    Unconfirmed,

    /// <summary>Retired on reliable evidence: the source explicitly marked it unavailable, or a general catalog stopped listing it on repeated successful checks.</summary>
    Retired,

    /// <summary>An authoritative end date has passed.</summary>
    Expired,
}

public sealed record ProductAvailabilityAssessment(ProductAvailability Availability, string Reason)
{
    /// <summary>Short label for lists ("Retired", "Availability unconfirmed").</summary>
    public string Label => Availability switch
    {
        ProductAvailability.Available => "Available",
        ProductAvailability.Unconfirmed => "Availability unconfirmed",
        ProductAvailability.Expired => "Expired",
        _ => "Retired",
    };
}

/// <summary>
/// Phase 31B: the single rule that turns a product's stored lifecycle into what is shown. Lifecycle
/// itself is unchanged (spec §7.3 absence counting still governs current pricing); this only decides
/// whether "retired" may be asserted. A product sold through a dedicated official channel is never
/// required to appear on another page, and its absence from its own public listing is not, by itself,
/// proof that it ended — unless the source explicitly marked it unavailable or an end date passed.
/// </summary>
public static class ProductAvailabilityClassifier
{
    /// <summary>The prefix <see cref="CatalogProductLifecycle.ConfirmAbsent"/> writes for an absence-based archive.</summary>
    public const string ConfirmedAbsencePrefix = "Confirmed absent on ";

    /// <summary>Categories sold through an eligibility-verified dedicated channel (purchase completes only after identity verification).</summary>
    public static bool IsDedicatedVerifiedChannel(SourceCategory category) =>
        category is SourceCategory.MilitaryFirstResponder or SourceCategory.DatedMilitaryFirstResponder;

    public static ProductAvailabilityAssessment Classify(ProductLifecycleState state, SourceCategory category, string? archivedReason)
    {
        switch (state)
        {
            case ProductLifecycleState.Active:
                return new(ProductAvailability.Available, "Listed at the latest successful official check.");

            case ProductLifecycleState.PendingConfirmation:
                return new(ProductAvailability.Unconfirmed, "Missing from the latest official check; not confirmed as retired.");

            case ProductLifecycleState.Expired:
                return new(ProductAvailability.Expired, archivedReason ?? "Its official end date has passed.");

            case ProductLifecycleState.Archived:
            default:
                var absenceOnly = archivedReason is null || archivedReason.StartsWith(ConfirmedAbsencePrefix, StringComparison.Ordinal);
                if (absenceOnly && IsDedicatedVerifiedChannel(category))
                {
                    return new(
                        ProductAvailability.Unconfirmed,
                        "Sold through the official ID.me-verified Military & First Responder purchase flow, which can't be checked without signing in. It was missing from the public page, but that alone does not confirm it ended.");
                }

                return new(
                    ProductAvailability.Retired,
                    absenceOnly
                        ? "No longer listed on the official page after repeated successful checks."
                        : archivedReason!);
        }
    }
}
