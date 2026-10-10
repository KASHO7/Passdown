using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Catalog;

/// <summary>Per-product outcome of one reconciliation pass, for callers that want to log/report what happened.</summary>
public enum ReconciliationOutcome
{
    /// <summary>Newly discovered; added as Active.</summary>
    Added,

    /// <summary>Already known and observed again; catalog row refreshed (display/price fields may or may not have changed).</summary>
    Confirmed,

    /// <summary>Missing from this retrieval for the first time; moved to PendingConfirmation.</summary>
    PendingRemoval,

    /// <summary>Missing from this retrieval for a second consecutive time, or explicitly marked unavailable; archived.</summary>
    Archived,

    /// <summary>Was PendingConfirmation or Archived and is present again; moved back to Active.</summary>
    Reactivated,

    /// <summary>An authoritative valid-through date has passed (spec Phase 17A §2); archived immediately as <see cref="Domain.Catalog.ProductLifecycleState.Expired"/>, distinct from a confirmed-absent or explicitly-unavailable archive.</summary>
    Expired,
}

public sealed record ReconciledProduct(string ProductKey, ReconciliationOutcome Outcome, bool HistoryAppended);

public sealed record ReconciliationResult(SourceCategory Category, IReadOnlyList<ReconciledProduct> Products);

/// <summary>
/// Converts one successful official catalog retrieval into durable product state
/// and history (spec §7.1–§7.3). The sole place lifecycle transitions get decided
/// for catalog products — repositories only persist whatever this service tells
/// them to.
/// </summary>
public interface IProductReconciliationService
{
    /// <summary>
    /// Reconciles <paramref name="observedProducts"/> — everything a single
    /// successful retrieval for <paramref name="category"/> returned — against
    /// the durably stored catalog. <paramref name="evidence"/> must be a
    /// successful completion (spec §5.2); a failed/errored retrieval must never
    /// be reconciled at all; retain last-known-good state instead (spec §6.7/§7.1).
    /// </summary>
    Task<ReconciliationResult> ReconcileAsync(
        SourceCategory category,
        SourceEvidence evidence,
        IReadOnlyList<ObservedProduct> observedProducts,
        CancellationToken cancellationToken = default);
}
