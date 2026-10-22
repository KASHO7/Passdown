using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;

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

    /// <summary>
    /// Phase 25: recurring Memberships from the official Memberships &amp; Season Passes page — a
    /// different product from a Season Pass, so a separate group. Appended (not inserted) so no existing
    /// value changes; <see cref="ProductDisplaySectionResolver.DisplayOrder"/> places it right after
    /// <see cref="SeasonPasses"/>. Not part of the Associate PDF/Teams Copy contract (deferred).
    /// </summary>
    Memberships,
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
    /// <item><see cref="SourceCategory.DatedMilitaryFirstResponder"/> (Phase 21A) — its exact per-date
    /// price is merged into the Military &amp; First Responder catalog row, or composed as rows under
    /// <see cref="SourceCategory.MilitaryFirstResponder"/>, by
    /// <c>AssociatePassdownService.MergeExactDatedMilitaryFirstResponder</c>; its status never gets a
    /// banner of its own (Daily Admission already has one).</item>
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
        SourceCategory.DatedMilitaryFirstResponder => null,
        SourceCategory.ShowSchedule => null, // Phase 21C: operational information, never a product row.
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
    /// <summary>
    /// Season-pass tier words a product's display name may carry (spec Phase 17A §5/§20) — checked
    /// alongside the word "Pass" itself so this recognizes any current or future tier ("2027 Silver
    /// Pass", "2027 Gold Pass", "2027 Prestige Pass", "Military Gold Pass", "First Responder Gold
    /// Pass", ...) without hardcoding a marketing year or a full product name. Deliberately just the
    /// tier vocabulary, not a list of exact product names — a name-agnostic fallback used only when a
    /// product's raw <see cref="SourceCategory"/> alone is not already unambiguous (spec §5 preference
    /// order: structured category signal first, resilient name fallback second).
    /// </summary>
    private static readonly string[] SeasonPassTierWords = ["Silver", "Gold", "Prestige"];

    public static ProductDisplaySection? Resolve(AssociateProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (product.Category is not { } category)
        {
            return null;
        }

        var section = Resolve(category);
        if (section is null)
        {
            // Categories with no dedicated section (Bring-A-Friend Duo, dated ordinary Bring-A-Friend)
            // stay null regardless of name — their real-world offer is always represented elsewhere
            // (spec Phase 15A), and neither ever carries season-pass wording in practice.
            return null;
        }

        // Product-family precedence (spec Phase 17A §4/§6): a season-pass product name is a stronger
        // signal than any category or audience wording, so it wins even when the raw retrieval
        // category is something else entirely — e.g. "Military Gold Pass" scraped alongside ordinary
        // Military/First-Responder daily tickets under SourceCategory.MilitaryFirstResponder (which
        // otherwise defaults to DailyAdmission) still resolves to SeasonPasses, while "Military 1-Day
        // Ticket"/"Military & First Responder Tickets" (no season-pass tier wording) correctly stays
        // DailyAdmission.
        // Phase 25: a Membership (monthly rate, or an unfolded initiation fee) is never a Season Pass.
        if (product.PassOffer is { Product: PassProductType.Membership })
        {
            return ProductDisplaySection.Memberships;
        }

        if (IsSeasonPassProduct(product.DisplayName))
        {
            return ProductDisplaySection.SeasonPasses;
        }

        return section == ProductDisplaySection.Promotions && IsScarowindsSpecific(product.DisplayName)
            ? ProductDisplaySection.SCarowinds
            : section;
    }

    /// <summary>
    /// Resolves every product to its display section, then removes an exact-name duplicate that only
    /// reached the same section via this resolver's name-based reclassification (spec Phase 17A
    /// §9/§10's equivalence-model ask) — e.g. "2027 Gold Pass" is deliberately tracked as two
    /// independent catalog rows (one under the native <see cref="SourceCategory.SeasonPasses"/>, one
    /// under <see cref="SourceCategory.Promotions"/> from the same page's promoted-offer card; spec
    /// §7.2 category-scoped identity, see docs/retrieval/catalog-source-inventory.md's Phase 3F4/3F5
    /// addendum) that, before this method, printed once in each of two different sections — Promotions
    /// and Season Passes — and so was never visually a duplicate. <see cref="Resolve(AssociateProduct)"/>'s
    /// new season-pass override now reclassifies both rows into Season Passes, which would otherwise
    /// print the same offer twice in the same section; this keeps only the row whose own
    /// <see cref="AssociateProduct.Category"/> natively resolves to that section (Season Passes here),
    /// preferring it over one that only arrived there via the name override. A product whose section
    /// has no collision is returned untouched, so this never overrides
    /// <see cref="Resolve(AssociateProduct)"/>'s per-product decision — it only resolves a collision
    /// that decision cannot see by construction. The one authoritative entry point every consumer
    /// (Associate Products screen, Associate PDF) should use instead of resolving products one at a
    /// time (spec §13/§14: no separate classification implementation per consumer).
    /// </summary>
    public static IReadOnlyList<(AssociateProduct Product, ProductDisplaySection Section)> ResolveAll(IEnumerable<AssociateProduct> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        var resolved = products
            .Select(product => (Product: product, Section: Resolve(product)))
            .Where(entry => entry.Section is not null)
            .Select(entry => (entry.Product, Section: entry.Section!.Value))
            .ToList();

        var duplicatesToDrop = resolved
            .GroupBy(entry => (entry.Section, Name: DuplicateKey(entry.Product, entry.Section)))
            .Where(group => group.Count() > 1)
            .SelectMany(group => group
                .OrderByDescending(entry => IsNativeToSection(entry.Product.Category, entry.Section))
                .Skip(1))
            .ToList();

        return duplicatesToDrop.Count == 0
            ? resolved
            : resolved.Except(duplicatesToDrop).ToList();
    }

    /// <summary>
    /// Phase 25: the duplicate-detection key within a section. In Season Passes it is the pass offer
    /// (the native row's <see cref="AssociateProduct.PassOffer"/>, or the strict legacy-name rule for a
    /// reclassified card such as a promoted "2027 Gold Pass"), so the same pass still prints once even
    /// though the redesigned page names it "Gold Season Pass — 2027"; everywhere else, and for any
    /// unrecognized name, the normalized name as before.
    /// </summary>
    private static string DuplicateKey(AssociateProduct product, ProductDisplaySection section)
    {
        if (section == ProductDisplaySection.SeasonPasses &&
            (product.PassOffer ?? PassOffer.FromLegacyName(product.DisplayName)) is { } offer)
        {
            return "pass-offer:" + offer.IdentityText;
        }

        return NormalizeName(product.DisplayName);
    }

    /// <summary>Phase 25: on-screen order of the sections — enum order, except Memberships directly follows Season Passes.</summary>
    public static int DisplayOrder(ProductDisplaySection section) => section switch
    {
        ProductDisplaySection.Memberships => (int)ProductDisplaySection.SeasonPasses * 10 + 5,
        _ => (int)section * 10,
    };

    /// <summary>True when a product's own retrieval category resolves to <paramref name="section"/> without needing the name-based season-pass/SCarowinds overrides — i.e. it belongs there "natively", not just by reclassification.</summary>
    private static bool IsNativeToSection(SourceCategory? category, ProductDisplaySection section) =>
        category is { } c && Resolve(c) == section;

    /// <summary>
    /// The season-pass tier-word rule (a tier word plus "Pass"). Public since Phase 20A so
    /// <see cref="Catalog.PriceHistoryTrackingPolicy"/> reuses the exact same rule rather than a copy.
    /// </summary>
    public static bool IsSeasonPassName(string displayName) => IsSeasonPassProduct(displayName);

    private static bool IsSeasonPassProduct(string displayName)
    {
        if (!displayName.Contains("Pass", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SeasonPassTierWords.Any(tier => displayName.Contains(tier, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Phase 19B: the one SCarowinds name rule, shared with season tagging (<see cref="ProductSeasonClassifier"/>).</summary>
    private static bool IsScarowindsSpecific(string displayName) =>
        ProductSeasonClassifier.FromOfficialName(displayName).HasFlag(ProductSeasonTags.SCarowinds);

    /// <summary>Same normalization as <see cref="ProductDisplayIdentity"/> — trims, lowercases, and collapses internal whitespace so cosmetic source variation never creates a false duplicate or a false mismatch.</summary>
    private static string NormalizeName(string displayName) => ProductDisplayIdentity.FromDisplayName(displayName).NormalizedDisplayName;
}
