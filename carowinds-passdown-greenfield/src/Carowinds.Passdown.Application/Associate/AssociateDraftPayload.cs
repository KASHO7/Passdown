using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Shows;

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
/// Builder; <see cref="IncludeHauntedAttractions"/> added Phase 8B to correct a Phase 8A omission).
/// Day of week, date, Park Hours, Attendance, Early Ride Time, and Daily Feature are mandatory and
/// have no corresponding flag here — the Documents project's document-model builder always renders
/// them regardless of this selection.
/// </summary>
public sealed record PassdownSectionSelection(
    bool IncludeDailyAdmission,
    bool IncludeFastLane,
    bool IncludePromotions,
    bool IncludeSeasonPasses,
    bool IncludeAddOns,
    bool IncludeOperationalNotes,
    bool IncludeHauntedAttractions)
{
    /// <summary>
    /// Every optional section included — used only as the fallback for a legacy draft whose persisted
    /// JSON predates this concept entirely (spec Phase 8A; see
    /// <see cref="AssociateDraftPayloadSerializer.Deserialize"/>), so an existing user's already-saved
    /// draft never silently loses a section it always used to print. No longer the default for a
    /// brand-new draft — see <see cref="Default"/> (Phase 17D §6).
    /// </summary>
    public static PassdownSectionSelection AllIncluded { get; } = new(true, true, true, true, true, true, true);

    /// <summary>
    /// The default for a brand-new draft (Phase 17D §6's "normal intent"): Daily Admission, Fast Lane,
    /// and Promotions checked — the sections normally relevant to a typical shift; Season Passes,
    /// Add-Ons, SCarowinds/Haunt, and Operational Notes unchecked, since those are seasonal/occasional
    /// rather than everyday content. Add-Ons remain fully available for price/history/reference
    /// elsewhere in the app (Products screen) regardless of this PDF-inclusion default.
    /// </summary>
    public static PassdownSectionSelection Default { get; } = new(true, true, true, false, false, false, false);
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
/// A frozen, per-draft copy of a persistent <see cref="OperationalNote"/> a supervisor explicitly
/// chose to include in one specific day's Associate Passdown (Phase 11D Part A/B). Every display
/// field is copied at selection time and never re-read from the live <c>operational_notes</c> row
/// afterward — editing, resolving, or otherwise changing the source note must never silently rewrite
/// an already-selected copy (Phase 11D Part B/C). <see cref="SourceOperationalNoteId"/> is retained
/// only as source identity (duplicate-selection prevention, Part H), never as a live reference the
/// draft depends on to render or finalize.
/// </summary>
public sealed record AssociateOperationalNoteSelection(
    Guid SourceOperationalNoteId,
    string Title,
    string? Body,
    OperationalNoteCategory Category,
    OperationalNotePriority Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    DateTimeOffset SelectedAtUtc);

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
    IReadOnlyList<string> OperationalNotes,
    DailyFeatureType? DailyFeatureType,
    string? DailyFeatureText,
    IReadOnlyDictionary<string, ProductDraftOverride> ProductOverrides,
    IReadOnlyList<CustomProductEntry> CustomProducts,
    PassdownSectionSelection SectionSelection,
    IReadOnlyList<AssociateOperationalNoteSelection> SelectedOperationalNotes)
{
    /// <summary>
    /// Current schema version for <see cref="Persistence.AssociateDraft.SchemaVersion"/> — bump when
    /// this shape changes incompatibly. Phase 8A bumped 1 -> 2 for the Daily Fun -> Daily Feature
    /// rename plus <see cref="SectionSelection"/>; Phase 11D bumped 2 -> 3 for <see cref="SelectedOperationalNotes"/>;
    /// Phase 17B bumps 3 -> 4 for removing the old free-text <c>EarlyRideTime</c> field — Early Ride
    /// Time is now authoritative monthly/override configuration
    /// (<see cref="Ert.MonthlyErtConfiguration"/>/<see cref="Ert.ErtDateOverride"/>), never per-draft
    /// content. A pre-17B draft's persisted "EarlyRideTime" JSON property is simply ignored on read —
    /// nothing is migrated forward from it (spec Part 34: never fabricate persisted monthly intent
    /// from old data). See <see cref="AssociateDraftPayloadSerializer.Deserialize"/> for every other
    /// migrated-forward field.
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    /// <summary>
    /// Phase 18D: manual North Gate opening time for this operating date, or null for "use the
    /// calculated default" (never a copy of the default). Additive and optional — a pre-18D draft
    /// simply has no such property and loads as "no override", so <see cref="CurrentSchemaVersion"/>
    /// is unchanged. Omitted from the JSON while null so a draft without overrides serializes (and
    /// therefore content-hashes, Personal Sync/REL-004) byte-identically to a pre-18D draft.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimeOnly? NorthGateOpeningOverride { get; init; }

    /// <summary>Phase 18D: manual Avenue opening time for this operating date — same contract as <see cref="NorthGateOpeningOverride"/>, fully independent of it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimeOnly? AvenueOpeningOverride { get; init; }

    /// <summary>
    /// Phase 21C: the optional Shows section — whether it prints and which official shows were chosen
    /// for this date (<see cref="AssociateShowsSelection"/>). Additive and optional, following the
    /// Phase 18D precedent: a pre-21C draft has no such property and loads as "section off, nothing
    /// selected" (never a fabricated selection), so <see cref="CurrentSchemaVersion"/> is unchanged.
    /// Null — and therefore omitted from the JSON — whenever it carries nothing, so a draft that never
    /// used Shows serializes and content-hashes byte-identically to a pre-21C draft.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssociateShowsSelection? Shows { get; init; }

    /// <summary>The draft an operating date starts with before an associate has entered anything (spec §8.3's "no draft -> default draft").</summary>
    public static AssociateDraftPayload CreateDefault() => new(
        ManualHoursOverride.Disabled,
        PrivateBuyoutOverride.Disabled,
        ProjectedAttendance: null,
        PreSolds: null,
        OperationalNotes: [],
        DailyFeatureType: null,
        DailyFeatureText: null,
        ProductOverrides: new Dictionary<string, ProductDraftOverride>(),
        CustomProducts: [],
        SectionSelection: PassdownSectionSelection.Default,
        SelectedOperationalNotes: []);
}
