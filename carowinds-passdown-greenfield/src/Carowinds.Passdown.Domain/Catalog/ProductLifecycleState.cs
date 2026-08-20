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

    /// <summary>Confirmed absent (or explicitly marked expired/unavailable); hidden from default views.</summary>
    Archived,
}
