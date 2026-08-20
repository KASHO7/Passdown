using Carowinds.Passdown.Application.Catalog;

namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// One successful catalog retrieval batch (Phase 3F1; see
/// docs/retrieval/catalog-source-inventory.md), ready to hand to
/// <see cref="IProductReconciliationService.ReconcileAsync"/>. Only ever attached to a
/// <see cref="Domain.Retrieval.RetrievalStatus.Verified"/> <see cref="SourceResult{TPayload}"/> —
/// same convention as every dated payload (<c>DatedAdmissionPayload</c>,
/// <c>DatedFastLanePayload</c>, <c>DatedHauntedPayload</c>).
///
/// Deliberately reuses Phase 2's existing <see cref="ObservedProduct"/> rather than introducing a
/// parallel catalog-specific product type: investigation found no field spec §7.2/this phase asks
/// for for a retrieved catalog product that <see cref="ObservedProduct"/> doesn't already carry
/// (id/URL via <see cref="Domain.Catalog.CatalogProductKey"/>, category via the key, display name,
/// current/comparison price, qualifier, date-specific flag, explicit-unavailable flag). This
/// record is the one genuinely new piece needed: a named wrapper so a catalog adapter can
/// implement <c>IOfficialDataSource&lt;CatalogPayload&gt;</c> the same way every dated adapter
/// implements <c>IOfficialDataSource&lt;TItsOwnPayload&gt;</c>. A products list, not a single
/// product, because one catalog page/retrieval typically returns several products at once — unlike
/// the dated sources, catalog products don't need a per-batch "applicable date" here (each
/// <see cref="ObservedProduct"/> already carries its own <c>DateSpecific</c>/<c>ApplicableDate</c>
/// when relevant).
/// </summary>
public sealed record CatalogPayload(IReadOnlyList<ObservedProduct> Products);
