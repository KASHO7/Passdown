using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// The Associate/PDF product display groupings (Phase 15A; docs/phase13-hardening.md's Phase 15A
/// section). Deliberately distinct from <see cref="SourceCategory"/>, which identifies where a
/// product was retrieved from and drives freshness/persistence/reconciliation — untouched by this
/// phase. A display section is a *presentation* concept only: several source categories can resolve
/// to the same section (Military/First Responder folds into <see cref="DailyAdmission"/>), and a
/// source category can resolve to no section at all (Bring-A-Friend Duo and the new dated ordinary
/// Bring-A-Friend category both have their real-world offer already represented by a Promotions
/// catalog row, so neither ever gets its own section).
/// </summary>
public enum ProductDisplaySection
{
    DailyAdmission,
    FastLane,
    SCarowinds,
    SeasonPasses,
    AddOns,
    Promotions,
}

/// <summary>
/// One authoritative section-selection function (spec Phase 15A §5) that every included
/// product/category resolves through, so a product is assigned to exactly one display section rather
/// than through independent per-section boolean filters (<c>IsPromotion</c>, <c>IsSCarowinds</c>, ...)
/// that could both fire for the same product.
/// </summary>
public static class ProductDisplaySectionResolver
{
    /// <summary>
    /// Resolves the <see cref="SourceCategory"/> a product/category-status entry was retrieved under
    /// to its display section, or <see langword="null"/> when that category never gets a section of
    /// its own for display purposes:
    /// <list type="bullet">
    /// <item><see cref="SourceCategory.BringAFriendDuo"/> — its real-world offer is already shown once
    /// under Promotions via the generically-discovered "Celebrate America! Bring-A-Friend Duo" catalog
    /// card; a second, separately-named row from this category would duplicate it.</item>
    /// <item><see cref="SourceCategory.DatedBringAFriend"/> — never an independent row at all; its
    /// exact per-date price is merged directly into the existing Promotions "Bring-A-Friend Ticket"
    /// catalog row by <c>AssociatePassdownService.MergeExactDatedBringAFriend</c>.</item>
    /// </list>
    /// </summary>
    public static ProductDisplaySection? Resolve(SourceCategory category) => category switch
    {
        SourceCategory.DatedAdmissionTickets => ProductDisplaySection.DailyAdmission,
        SourceCategory.MilitaryFirstResponder => ProductDisplaySection.DailyAdmission,
        SourceCategory.DatedFastLane => ProductDisplaySection.FastLane,
        SourceCategory.HauntedAttractions => ProductDisplaySection.SCarowinds,
        SourceCategory.SeasonPasses => ProductDisplaySection.SeasonPasses,
        SourceCategory.AddOns => ProductDisplaySection.AddOns,
        SourceCategory.Promotions => ProductDisplaySection.Promotions,
        SourceCategory.BringAFriendDuo => null,
        SourceCategory.DatedBringAFriend => null,
        _ => ProductDisplaySection.Promotions,
    };

    /// <summary>
    /// Resolves one product to its display section (spec Phase 15A example concept
    /// <c>ProductDisplaySection ResolveSection(product)</c>). A Promotions-category product whose
    /// official name identifies it as SCarowinds-specific (e.g. "SCarowinds Bundles" — park admission
    /// bundled with haunted-maze access, discovered generically on the same pages as every other
    /// Promotions card) is reclassified into <see cref="ProductDisplaySection.SCarowinds"/> — a
    /// generic, content-based rule that applies to any current or future Promotions card naming
    /// SCarowinds, not a one-off match on a single product's exact display string. Returns
    /// <see langword="null"/> for a custom product (no official category) or a category with no
    /// dedicated section (see <see cref="Resolve(SourceCategory)"/>).
    /// </summary>
    public static ProductDisplaySection? Resolve(AssociateProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (product.Category is not { } category)
        {
            return null;
        }

        var section = Resolve(category);
        return section == ProductDisplaySection.Promotions && IsScarowindsSpecific(product.DisplayName)
            ? ProductDisplaySection.SCarowinds
            : section;
    }

    private static bool IsScarowindsSpecific(string displayName) =>
        displayName.Contains("SCarowinds", StringComparison.OrdinalIgnoreCase);
}
