namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// The three Daily Feature content types (Phase 8A; formerly "Daily Fun"). Plain manual text — never
/// AI-generated. Underlying values are unchanged from the original <c>DailyFunType</c> enum so a
/// schema-v1 draft's persisted integer still decodes correctly after the rename — see
/// <see cref="AssociatePassdownService.DeserializeDraft"/>.
/// </summary>
public enum DailyFeatureType
{
    QuestionOfTheDay,
    UselessFactOfTheDay,
    QuoteOfTheDay,
}

/// <summary>
/// Which optional Passdown sections a supervisor has chosen to include (Phase 8A's Passdown
/// Builder). Day of week, date, Park Hours, Attendance, Early Ride Time, and Daily Feature are
/// mandatory and have no corresponding flag here — the Documents project's document-model builder
/// always renders them regardless of this selection.
/// </summary>
public sealed record PassdownSectionSelection(
    bool IncludeDailyAdmission,
    bool IncludeFastLane,
    bool IncludePromotions,
    bool IncludeSeasonPasses,
    bool IncludeAddOns,
    bool IncludeOperationalNotes)
{
    /// <summary>Every optional section included — the default for a new draft and for a schema-v1 draft migrated forward, matching what the PDF already rendered before Phase 8A existed.</summary>
    public static PassdownSectionSelection AllIncluded { get; } = new(true, true, true, true, true, true);
}

/// <summary>Manual correction to the official park hours (spec §8.1/§8.2) — distinct from a private-buyout override, see <see cref="PrivateBuyoutOverride"/>.</summary>
public sealed record ManualHoursOverride(bool Enabled, TimeOnly? OpeningTime, TimeOnly? ClosingTime)
{
    public static ManualHoursOverride Disabled { get; } = new(false, null, null);
}

/// <summary>
/// Private-event buyout/closed override (spec §8.1). Spec §8.2 names this — not
/// <see cref="ManualHoursOverride"/> — as the one mechanism that can show hours on an officially
/// closed date.
/// </summary>
public sealed record PrivateBuyoutOverride(bool Enabled, TimeOnly? OpeningTime, TimeOnly? ClosingTime, string? Details)
{
    public static PrivateBuyoutOverride Disabled { get; } = new(false, null, null, null);
}

/// <summary>Early Ride Time details and participating attractions (spec §8.1). Free text plus a plain list — no attraction catalog exists to validate names against.</summary>
public sealed record EarlyRideTimeDetails(string? Details, IReadOnlyList<string> ParticipatingAttractions)
{
    public static EarlyRideTimeDetails Empty { get; } = new(null, []);
}

/// <summary>
/// Per-draft manual correction for one official product, keyed by <see cref="AssociateProductIdentity"/>
/// (spec §7.5: include/exclude and a display-price override that never touches the official cache).
/// Absent from the draft's dictionary is equivalent to <see cref="Default"/> — every official
/// product is included by default until an associate excludes it.
/// </summary>
public sealed record ProductDraftOverride(bool IncludeOnPdf, decimal? DisplayPriceOverride)
{
    public static ProductDraftOverride Default { get; } = new(true, null);
}

/// <summary>A manager-added internal product/event (spec §7.5) — always draft-local, never written to or reconciled against the official catalog.</summary>
public sealed record CustomProductEntry(string Id, string DisplayName, decimal? Price, string? Notes, bool IncludeOnPdf);

/// <summary>
/// Everything an associate enters by hand for one operating date (spec §8.1), independent of any
/// officially retrieved data. This is the exact shape persisted as
/// <see cref="Persistence.AssociateDraft.PayloadJson"/> — kept entirely separate from cached
/// official data (spec §6.2/§7.5's "manual draft changes must never modify official cached
/// values"), so nothing here is ever written back into <c>dated-results</c>/<c>catalog-results</c>.
/// </summary>
public sealed record AssociateDraftPayload(
    ManualHoursOverride ManualHoursOverride,
    PrivateBuyoutOverride PrivateBuyout,
    int? ProjectedAttendance,
    int? PreSolds,
    EarlyRideTimeDetails EarlyRideTime,
    IReadOnlyList<string> OperationalNotes,
    DailyFeatureType? DailyFeatureType,
    string? DailyFeatureText,
    IReadOnlyDictionary<string, ProductDraftOverride> ProductOverrides,
    IReadOnlyList<CustomProductEntry> CustomProducts,
    PassdownSectionSelection SectionSelection)
{
    /// <summary>
    /// Current schema version for <see cref="Persistence.AssociateDraft.SchemaVersion"/> — bump when
    /// this shape changes incompatibly. Phase 8A bumped 1 -> 2 for the Daily Fun -> Daily Feature
    /// rename plus <see cref="SectionSelection"/>; see <see cref="AssociatePassdownService.DeserializeDraft"/>
    /// for how an older persisted payload is migrated forward on read.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The draft an operating date starts with before an associate has entered anything (spec §8.3's "no draft -> default draft").</summary>
    public static AssociateDraftPayload CreateDefault() => new(
        ManualHoursOverride.Disabled,
        PrivateBuyoutOverride.Disabled,
        ProjectedAttendance: null,
        PreSolds: null,
        EarlyRideTimeDetails.Empty,
        OperationalNotes: [],
        DailyFeatureType: null,
        DailyFeatureText: null,
        ProductOverrides: new Dictionary<string, ProductDraftOverride>(),
        CustomProducts: [],
        SectionSelection: PassdownSectionSelection.AllIncluded);
}
