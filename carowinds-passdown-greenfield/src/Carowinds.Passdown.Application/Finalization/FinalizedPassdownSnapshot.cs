namespace Carowinds.Passdown.Application.Finalization;

/// <summary>
/// Who/what triggered a <see cref="Persistence.FinalizedPassdown"/> (spec Phase 9 Part G). No
/// authenticated-user identity exists yet (accounts are Phase 16+), so this is the only
/// "who finalized this" metadata Phase 9 records.
/// </summary>
public enum FinalizationOrigin
{
    /// <summary>Created by the future Phase 9B 6:00 AM automatic finalization job. Not produced by any code yet.</summary>
    Scheduled,

    /// <summary>Created by an explicit associate/support action rather than the schedule.</summary>
    Manual,

    /// <summary>Created by a future catch-up/recovery pass covering a cutoff the app missed (e.g. the machine was off at 6:00 AM).</summary>
    Recovery,
}

/// <summary>Effective park-hours status for the frozen snapshot — mirrors <c>Documents.Associate.PdfParkHoursStatus</c> (spec Phase 9 Part K: same values, kept independent so Application never references Documents).</summary>
public enum FinalizedParkHoursStatus
{
    NotCheckedYet,
    Closed,
    Open,
}

/// <summary>Which visual tier a product category was rendered in at finalization time — mirrors <c>Documents.Associate.PdfProductSectionTier</c> (spec Phase 9 Part K).</summary>
public enum FinalizedProductSectionTier
{
    KeySelling,
    Promotional,
    Reference,
}

/// <summary>Frozen park-hours banner content (spec Phase 9 Part C/D) — every string here is already display-ready at finalization time; nothing is re-derived from hours data on read.</summary>
public sealed record FinalizedParkHoursSnapshot(
    FinalizedParkHoursStatus Status,
    string HeadlineText,
    string? BadgeText,
    string? NorthGateText,
    string? AvenueText,
    string? VerificationText,
    string? PrivateBuyoutDetails);

/// <summary>Frozen projected attendance and pre-solds (spec Phase 9 Part C).</summary>
public sealed record FinalizedAttendanceSnapshot(int? ProjectedAttendance, int? PreSolds);

/// <summary>Frozen Early Ride Time content (spec Phase 9 Part C).</summary>
public sealed record FinalizedEarlyRideTimeSnapshot(bool Enabled, string? Details, IReadOnlyList<string> ParticipatingAttractions);

/// <summary>Frozen Daily Feature content (spec Phase 9 Part C).</summary>
public sealed record FinalizedDailyFeatureSnapshot(string? TypeLabel, string? Text);

/// <summary>
/// One frozen printable product row (spec Phase 9 Part D) — <see cref="DisplayName"/>,
/// <see cref="EffectivePriceText"/>, and <see cref="ComparisonPriceText"/> are the already-resolved
/// display strings an associate saw at finalization time, never a product id/key that would need
/// resolving against a live catalog later.
/// </summary>
public sealed record FinalizedProductRowSnapshot(
    string DisplayName,
    string? EffectivePriceText,
    string? ComparisonPriceText,
    string? Qualifier,
    string? Notes);

/// <summary>One frozen product category section (spec Phase 9 Part C) — only categories that actually rendered a row are present, matching what the PDF printed.</summary>
public sealed record FinalizedProductCategorySnapshot(
    string CategoryName,
    FinalizedProductSectionTier Tier,
    IReadOnlyList<FinalizedProductRowSnapshot> Products);

/// <summary>
/// The complete, versioned, immutable content of one finalized Associate Passdown (spec Phase 9
/// Parts C/D/E) — everything needed to reconstruct exactly what associates received for
/// <see cref="PassdownDate"/>, with every display value already frozen to text. Built once by
/// <see cref="IFinalizedPassdownSnapshotBuilder"/> from whatever local data is already stored;
/// never mutated afterward, and never re-resolved against live catalog/hours/notes data.
/// </summary>
public sealed record FinalizedPassdownSnapshot(
    int SchemaVersion,
    DateOnly PassdownDate,
    string OperatingDateText,
    FinalizedParkHoursSnapshot ParkHours,
    FinalizedAttendanceSnapshot Attendance,
    FinalizedEarlyRideTimeSnapshot EarlyRideTime,
    FinalizedDailyFeatureSnapshot DailyFeature,
    IReadOnlyList<string> OperationalNotes,
    IReadOnlyList<FinalizedProductCategorySnapshot> ProductCategories,
    bool HasUnverifiedProductPricing,
    string GeneratedAtText)
{
    /// <summary>
    /// The current structural version of <see cref="FinalizedPassdownSnapshot"/> (spec Phase 9 Part
    /// F) — NOT the app version. Bump this, and only this constant, when the snapshot shape changes
    /// incompatibly; a future reader uses <see cref="Persistence.FinalizedPassdown.SnapshotSchemaVersion"/>
    /// to detect which shape a historical row's <see cref="Persistence.FinalizedPassdown.SnapshotJson"/> is in.
    /// </summary>
    public const int CurrentSchemaVersion = 1;
}
