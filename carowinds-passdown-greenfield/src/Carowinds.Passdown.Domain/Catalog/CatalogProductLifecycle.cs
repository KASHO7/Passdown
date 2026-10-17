namespace Carowinds.Passdown.Domain.Catalog;

using Carowinds.Passdown.Domain.Retrieval;

/// <summary>
/// Add/update/remove lifecycle for one catalog product, per spec §7.3. Immutable —
/// each transition returns a new instance. Persistence and pricing/display fields
/// belong to later phases; this holds only what governs the lifecycle itself.
/// </summary>
public sealed record CatalogProductLifecycle
{
    /// <summary>
    /// Consecutive successful absence checks required before archiving (spec §7.3: "a confirmation
    /// retry or a second successful absence check"). Phase 19B: this archive is the product's
    /// "retired from the current catalog" state; an Error never advances the count.
    /// </summary>
    public const int ConfirmedAbsenceThreshold = 2;

    public CatalogProductKey Key { get; }
    public ProductLifecycleState State { get; }
    public DateTimeOffset FirstSeenAt { get; }
    public DateTimeOffset LastSeenAt { get; }
    public DateTimeOffset? LastVerifiedAt { get; }
    public int ConsecutiveConfirmedAbsenceCount { get; }
    public DateTimeOffset? ArchivedAt { get; }
    public string? ArchivedReason { get; }
    public SourceEvidence LastEvidence { get; }

    private CatalogProductLifecycle(
        CatalogProductKey key,
        ProductLifecycleState state,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        DateTimeOffset? lastVerifiedAt,
        int consecutiveConfirmedAbsenceCount,
        DateTimeOffset? archivedAt,
        string? archivedReason,
        SourceEvidence lastEvidence)
    {
        Key = key;
        State = state;
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
        LastVerifiedAt = lastVerifiedAt;
        ConsecutiveConfirmedAbsenceCount = consecutiveConfirmedAbsenceCount;
        ArchivedAt = archivedAt;
        ArchivedReason = archivedReason;
        LastEvidence = lastEvidence;
    }

    /// <summary>
    /// Reconstitutes a lifecycle instance from previously persisted state, for
    /// callers (e.g. reconciliation) that load a product's current state from a
    /// repository and need to apply a further transition. Every field is taken
    /// as-is with no re-derivation — the values are assumed to already be a
    /// lifecycle this type itself produced, so this performs no business-rule
    /// validation beyond requiring the two fields every state must have.
    /// </summary>
    public static CatalogProductLifecycle Rehydrate(
        CatalogProductKey key,
        ProductLifecycleState state,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        DateTimeOffset? lastVerifiedAt,
        int consecutiveConfirmedAbsenceCount,
        DateTimeOffset? archivedAt,
        string? archivedReason,
        SourceEvidence lastEvidence)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(lastEvidence);

        return new CatalogProductLifecycle(
            key,
            state,
            firstSeenAt,
            lastSeenAt,
            lastVerifiedAt,
            consecutiveConfirmedAbsenceCount,
            archivedAt,
            archivedReason,
            lastEvidence);
    }

    /// <summary>A successfully retrieved new official product appears automatically (spec §7.3).</summary>
    public static CatalogProductLifecycle FirstObserved(CatalogProductKey key, SourceEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(key);
        RequireVerified(evidence, "A new catalog product can only be created from a verified retrieval");

        return new CatalogProductLifecycle(
            key,
            ProductLifecycleState.Active,
            firstSeenAt: evidence.TimeChecked,
            lastSeenAt: evidence.TimeChecked,
            lastVerifiedAt: evidence.TimeChecked,
            consecutiveConfirmedAbsenceCount: 0,
            archivedAt: null,
            archivedReason: null,
            lastEvidence: evidence);
    }

    /// <summary>
    /// Records that the product was seen again on a successful retrieval. Resets any
    /// pending-absence count and reactivates an archived product (spec §7.3: "If a
    /// product returns later, reactivate its record").
    /// </summary>
    public CatalogProductLifecycle ConfirmPresent(SourceEvidence evidence)
    {
        RequireVerified(evidence, "Confirming a product present requires a verified retrieval");

        return new CatalogProductLifecycle(
            Key,
            ProductLifecycleState.Active,
            FirstSeenAt,
            lastSeenAt: evidence.TimeChecked,
            lastVerifiedAt: evidence.TimeChecked,
            consecutiveConfirmedAbsenceCount: 0,
            archivedAt: null,
            archivedReason: null,
            lastEvidence: evidence);
    }

    /// <summary>
    /// Records that a successful retrieval did not contain this product. The first
    /// such check moves it to <see cref="ProductLifecycleState.PendingConfirmation"/>;
    /// a second consecutive one archives it (spec §7.3). An <see cref="RetrievalStatus.Error"/>
    /// is never a valid absence signal and is rejected.
    /// </summary>
    public CatalogProductLifecycle ConfirmAbsent(SourceEvidence evidence)
    {
        RequireNotError(evidence);

        var absenceCount = ConsecutiveConfirmedAbsenceCount + 1;

        if (absenceCount >= ConfirmedAbsenceThreshold)
        {
            return new CatalogProductLifecycle(
                Key,
                ProductLifecycleState.Archived,
                FirstSeenAt,
                LastSeenAt,
                LastVerifiedAt,
                consecutiveConfirmedAbsenceCount: absenceCount,
                archivedAt: evidence.TimeChecked,
                archivedReason: $"Confirmed absent on {absenceCount} consecutive successful checks.",
                lastEvidence: evidence);
        }

        return new CatalogProductLifecycle(
            Key,
            ProductLifecycleState.PendingConfirmation,
            FirstSeenAt,
            LastSeenAt,
            LastVerifiedAt,
            consecutiveConfirmedAbsenceCount: absenceCount,
            archivedAt: null,
            archivedReason: null,
            lastEvidence: evidence);
    }

    /// <summary>
    /// Archives immediately, without waiting for a second confirmation, because the
    /// source explicitly marked the product expired/unavailable (spec §7.3).
    /// </summary>
    public CatalogProductLifecycle MarkExplicitlyUnavailable(SourceEvidence evidence, string reason)
    {
        RequireNotError(evidence);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Archive reason is required.", nameof(reason));

        return new CatalogProductLifecycle(
            Key,
            ProductLifecycleState.Archived,
            FirstSeenAt,
            LastSeenAt,
            LastVerifiedAt,
            ConsecutiveConfirmedAbsenceCount,
            archivedAt: evidence.TimeChecked,
            archivedReason: reason,
            lastEvidence: evidence);
    }

    /// <summary>
    /// Archives immediately because an authoritative valid-through/end date has passed (spec Phase
    /// 17A §2) — distinct from <see cref="MarkExplicitlyUnavailable"/> (the source itself flags,
    /// right now, that the product is gone/sold out) and from <see cref="ConfirmAbsent"/> (the source
    /// simply stopped listing the product at all). Both this and
    /// <see cref="MarkExplicitlyUnavailable"/> skip <see cref="ConfirmAbsent"/>'s two-check
    /// confirmation delay, because the evidence for "not valid anymore" is already conclusive —
    /// waiting for a second check would only delay hiding a product an associate should not be
    /// offered today. Inclusive of <paramref name="validThroughDate"/> itself: the product is still
    /// valid ON that date and only becomes expired the day after (spec Phase 17A §22 end-date
    /// semantics).
    /// </summary>
    public CatalogProductLifecycle MarkExpired(SourceEvidence evidence, DateOnly validThroughDate)
    {
        RequireNotError(evidence);

        var currentDate = DateOnly.FromDateTime(evidence.TimeChecked.DateTime);
        if (currentDate <= validThroughDate)
        {
            throw new ArgumentException(
                $"Cannot mark expired: {validThroughDate:yyyy-MM-dd} has not yet passed as of {currentDate:yyyy-MM-dd} (spec §22 inclusive end-date semantics).",
                nameof(validThroughDate));
        }

        return new CatalogProductLifecycle(
            Key,
            ProductLifecycleState.Expired,
            FirstSeenAt,
            LastSeenAt,
            LastVerifiedAt,
            ConsecutiveConfirmedAbsenceCount,
            archivedAt: evidence.TimeChecked,
            archivedReason: $"Promotion expired: valid through {validThroughDate:yyyy-MM-dd}.",
            lastEvidence: evidence);
    }

    private static void RequireVerified(SourceEvidence evidence, string message)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Status != RetrievalStatus.Verified)
            throw new ArgumentException($"{message} (spec §7.3).", nameof(evidence));
    }

    private static void RequireNotError(SourceEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Status == RetrievalStatus.Error)
        {
            throw new ArgumentException(
                "An error must never count as confirmed product absence (spec §7.3).",
                nameof(evidence));
        }
    }
}
