namespace Carowinds.Passdown.Domain.Catalog;

/// <summary>
/// Where a catalog product sits in its add/update/remove lifecycle, per spec §7.3.
/// </summary>
public enum ProductLifecycleState
{
    /// <summary>Currently confirmed present and shown.</summary>
    Active,

    /// <summary>Missing from one successful retrieval; awaiting a confirming second check before archiving.</summary>
    PendingConfirmation,

    /// <summary>Confirmed absent (no longer listed by the source at all), or the source explicitly flagged it gone/sold out right now; hidden from default views.</summary>
    Archived,

    /// <summary>
    /// Confirmed inactive because an authoritative valid-through/end date has passed (spec Phase 17A
    /// §1/§2) — distinct from <see cref="Archived"/>: the source never had to say anything new at
    /// all, we simply know from previously-observed validity metadata that today is past the offer's
    /// last valid day. Hidden from default views exactly like <see cref="Archived"/>, but kept fully
    /// queryable in catalog/history for diagnostics and finalized History (never deleted).
    /// </summary>
    Expired,
}
