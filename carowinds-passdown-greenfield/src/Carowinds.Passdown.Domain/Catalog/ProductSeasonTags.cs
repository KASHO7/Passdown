namespace Carowinds.Passdown.Domain.Catalog;

/// <summary>
/// Phase 19B: the operating seasons a product belongs to, as non-exclusive tags. A product may carry
/// none, one, or several (e.g. a regular admission bundle that includes SCarowinds is
/// <c>Carowinds | SCarowinds</c>), so this is a <see cref="FlagsAttribute"/> set — never a single
/// "current season" mode. Tags only classify; they never decide whether a product is currently
/// offered — official source publication (catalog lifecycle and dated results) is authoritative for
/// that, and no tag is ever derived from the calendar date.
/// </summary>
[Flags]
public enum ProductSeasonTags
{
    /// <summary>No confident seasonal classification — the honest default rather than a guess.</summary>
    None = 0,

    /// <summary>The regular Carowinds park operating season.</summary>
    Carowinds = 1,

    /// <summary>The SCarowinds Halloween event.</summary>
    SCarowinds = 2,

    /// <summary>The WinterFest holiday event.</summary>
    WinterFest = 4,
}
