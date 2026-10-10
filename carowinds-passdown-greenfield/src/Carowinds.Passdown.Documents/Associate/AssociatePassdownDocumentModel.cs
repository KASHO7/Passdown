using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>Effective park-hours status for the PDF's hours banner — a 1:1 relabeling of <c>Domain.Hours.EffectiveHoursStatus</c> kept local so this project never needs to reference the calculator, only its already-computed result (via the builder).</summary>
public enum PdfParkHoursStatus
{
    NotCheckedYet,
    Closed,
    Open,
}

/// <summary>
/// Already-formatted park-hours content for the PDF (spec §11.2's hours banner). Every string here
/// is display-ready — no time arithmetic, verification-state mapping, or override precedence is
/// decided in the Documents project; all of that already happened upstream (<c>ParkHoursCalculator</c>,
/// <see cref="AssociatePassdownDocumentModelBuilder"/>).
/// </summary>
public sealed record PdfParkHours(
    PdfParkHoursStatus Status,
    string HeadlineText,
    string? BadgeText,
    string? NorthGateText,
    string? AvenueText,
    string? VerificationText,
    string? PrivateBuyoutDetails);

/// <summary>Projected attendance and pre-solds (spec §8.1) — null means not entered, printed as "Not entered" rather than a fabricated zero.</summary>
public sealed record PdfAttendance(int? ProjectedAttendance, int? PreSolds);

/// <summary>
/// Early Ride Time content (Phase 17B spec Part 13; revised Phase 17D Part 2/10 — rides only, no
/// time) — built directly from <c>Application.Associate.AssociateWorkspace.EarlyRideTime</c>, the one
/// resolver-produced value; the PDF never derives ERT independently. <see cref="StartTimeText"/> is
/// kept populated (when a legacy/compat start time exists) for any historical/diagnostic reader, but
/// no Phase 17D+ renderer prints it — associates already know Early Ride happens 30 minutes before
/// park open, so the PDF shows rides only.
/// </summary>
public sealed record PdfEarlyRideTime(
    EffectiveErtKind Kind,
    EffectiveErtSource? Source,
    string? StartTimeText,
    IReadOnlyList<string> Rides,
    string? Note);

/// <summary>One of the three Daily Feature types (Phase 8A; formerly "Daily Fun"), pre-labeled by the builder — Documents never decides how <c>DailyFeatureType</c> reads as text.</summary>
public sealed record PdfDailyFeature(string? TypeLabel, string? Text)
{
    public bool HasContent => TypeLabel is not null || !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// One printable product row. Only ever built for a product the draft marked included (spec Part C)
/// — an excluded product never reaches this model at all, so there is no "excluded" flag to check
/// here. <see cref="EffectivePriceText"/> already reflects the display-price override when one
/// exists, or the official price otherwise (spec Part C) — never both; a "Starting From"-classified
/// price already carries its "From $x" prefix (Phase 17D Part 9) rather than a separate qualifier.
/// <see cref="ComparisonPriceText"/> and <see cref="Qualifier"/> are retained on the shape (and on
/// <c>Application.Finalization.FinalizedProductRowSnapshot</c>) for schema compatibility with rows
/// finalized before Phase 17D, but are always <see langword="null"/> going forward — Phase 17D §8/9
/// removes comparison ("was $x") pricing and ecommerce qualifier wording ("Only", "Online Price",
/// "Sale Price") from the PDF entirely; that information remains available in the Products screen and
/// product history, never on the printed handoff.
/// </summary>
public sealed record PdfProductRow(
    string DisplayName,
    string? EffectivePriceText,
    string? ComparisonPriceText,
    string? Qualifier,
    string? Notes);

/// <summary>
/// Phase 8B's presentation tier for one <see cref="PdfProductCategory"/> (spec Parts 3/5): which
/// visual treatment the PDF gives it, from the associate's-eye view of "must remember quickly" down
/// to "reference only". Purely a rendering decision — never persisted, never seen outside Documents.
/// </summary>
public enum PdfProductSectionTier
{
    /// <summary>Daily Admission / Fast Lane (spec Part 3 Level 3) — rendered as price tiles, one product's name and price immediately identifiable per tile.</summary>
    KeySelling,

    /// <summary>Promotions (spec Part 4) — rendered as promotional callout cards with the current price emphasized over any comparison price.</summary>
    Promotional,

    /// <summary>Season Passes, Add-Ons, SCarowinds/Haunt, and custom/internal entries (spec Part 5) — rendered as compact reference rows, more information-dense than the other two tiers.</summary>
    Reference,
}

/// <summary>
/// One product category section (spec §11.2's "grouped cleanly by category"). Only built when it has
/// at least one included row — an empty category never appears (spec Part B/§11.2's "do not reserve
/// empty space for hidden categories"). <see cref="Section"/> is <see langword="null"/> only for the
/// custom/internal-items pseudo-category (no official display section); every renderer uses it —
/// never <see cref="CategoryName"/> string matching — to place a category in the Phase 17D §11/§12
/// master layout's Daily Admission/Fast Lane and Promotions/Season Passes pairings.
/// </summary>
public sealed record PdfProductCategory(string CategoryName, ProductDisplaySection? Section, PdfProductSectionTier Tier, IReadOnlyList<PdfProductRow> Products);

/// <summary>
/// The complete, print-ready content for one Associate Passdown PDF (spec §11.2) — everything
/// <see cref="AssociatePassdownPdfGenerator"/> needs and nothing it must compute. Built once per
/// generation by <see cref="AssociatePassdownDocumentModelBuilder"/> from an
/// <c>Application.Associate.AssociateWorkspace</c>; never mutated afterward, so a given model
/// always renders the same bytes (spec Part H's "deterministic for a given document model").
/// </summary>
public sealed record AssociatePassdownDocumentModel(
    DateOnly? OperatingDate,
    string OperatingDateText,
    PdfParkHours ParkHours,
    PdfAttendance Attendance,
    PdfEarlyRideTime EarlyRideTime,
    PdfDailyFeature DailyFeature,
    IReadOnlyList<string> OperationalNotes,
    IReadOnlyList<PdfProductCategory> ProductCategories,
    bool HasUnverifiedProductPricing,
    string GeneratedAtText)
{
    public int TotalProductCount => ProductCategories.Sum(category => category.Products.Count);
}
