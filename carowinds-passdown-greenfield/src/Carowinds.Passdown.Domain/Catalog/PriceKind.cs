namespace Carowinds.Passdown.Domain.Catalog;

/// <summary>
/// The distinct kinds of "price" this app encounters, per <c>docs/retrieval/pricing-semantics.md</c>.
/// This is a presentation/reasoning classification computed from already-persisted fields
/// (<c>PriceQualifier</c>, comparison price, date-specificity) — it is never itself persisted, so
/// no schema migration was needed to add it (Phase 13C Part AM/AN).
/// </summary>
public enum PriceKind
{
    /// <summary>Legacy/unclassified — never assigned by current code; exists only as the default so a
    /// value computed from unexpected input degrades safely instead of asserting a false meaning.</summary>
    Unknown = 0,

    /// <summary>The confirmed price for one specific calendar date (<c>DateSpecific = true</c>).</summary>
    ExactDated,

    /// <summary>A non-date-specific floor/aggregate price, qualifier text exactly "Starting From".</summary>
    StartingFrom,

    /// <summary>The regular, non-promotional, non-date-specific catalog price (no qualifier, no comparison price).</summary>
    StandardCatalog,

    /// <summary>A currently-discounted catalog price, qualifier text exactly "Sale Price" (or any
    /// catalog price carrying a comparison price with no more specific qualifier).</summary>
    Sale,

    /// <summary>The higher "was"/reference price shown alongside a current price. Reserved for
    /// symmetry with the vocabulary doc; the comparison amount's own field already carries this
    /// meaning unambiguously, so no product's primary <see cref="PriceKind"/> is ever this value.</summary>
    ComparisonWas,

    /// <summary>The price attributable to one person within a multi-person bundle.</summary>
    PerPerson,

    /// <summary>A single total price for a package covering more than one person/component.</summary>
    Bundle,
}
