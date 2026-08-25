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

/// <summary>Early Ride Time content (spec §8.1). <see cref="Enabled"/> false means "not applicable" for this date, not merely "no details entered".</summary>
public sealed record PdfEarlyRideTime(bool Enabled, string? Details, IReadOnlyList<string> ParticipatingAttractions);

/// <summary>One of the three Daily Feature types (Phase 8A; formerly "Daily Fun"), pre-labeled by the builder — Documents never decides how <c>DailyFeatureType</c> reads as text.</summary>
public sealed record PdfDailyFeature(string? TypeLabel, string? Text)
{
    public bool HasContent => TypeLabel is not null || !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// One printable product row. Only ever built for a product the draft marked included (spec Part C)
/// — an excluded product never reaches this model at all, so there is no "excluded" flag to check
/// here. <see cref="EffectivePriceText"/> already reflects the display-price override when one
/// exists, or the official price otherwise (spec Part C) — never both.
/// </summary>
public sealed record PdfProductRow(
    string DisplayName,
    string? EffectivePriceText,
    string? ComparisonPriceText,
    string? Qualifier,
    string? Notes);

/// <summary>One product category section (spec §11.2's "grouped cleanly by category"). Only built when it has at least one included row — an empty category never appears (spec Part B/§11.2's "do not reserve empty space for hidden categories").</summary>
public sealed record PdfProductCategory(string CategoryName, IReadOnlyList<PdfProductRow> Products);

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
