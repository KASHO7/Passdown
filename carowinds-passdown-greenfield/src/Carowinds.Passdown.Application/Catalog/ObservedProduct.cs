using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Catalog;

/// <summary>
/// One product as it came back from a single successful catalog retrieval
/// (spec §7.2), before reconciliation against durable state. <see cref="Key"/>
/// is built by the caller (a retrieval adapter, in a later phase) using
/// <see cref="CatalogProductKey.Create"/>'s priority rules — reconciliation
/// itself never invents or re-derives identity.
///
/// <see cref="IsExplicitlyUnavailable"/> is distinct from a product simply being
/// missing from the retrieved set: it means the source *did* list the product but
/// explicitly flagged it expired/sold out, which archives immediately rather than
/// waiting for a second confirmed-absence check (spec §7.3).
/// </summary>
public sealed record ObservedProduct(
    CatalogProductKey Key,
    string DisplayName,
    decimal? CurrentPriceAmount,
    string? CurrentDisplayPrice,
    decimal? ComparisonPriceAmount,
    string? PriceQualifier,
    bool DateSpecific,
    DateOnly? ApplicableDate,
    bool IsExplicitlyUnavailable = false,
    string? UnavailableReason = null);
