namespace Carowinds.Passdown.Application.Associate;

/// <summary>Spec §8.1's three Daily Fun content types. Plain manual text — never AI-generated (spec §8.1).</summary>
public enum DailyFunType
{
    QuestionOfTheDay,
    FactOfTheDay,
    QuoteOfTheDay,
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
    DailyFunType? DailyFunType,
    string? DailyFunText,
    IReadOnlyDictionary<string, ProductDraftOverride> ProductOverrides,
    IReadOnlyList<CustomProductEntry> CustomProducts)
{
    /// <summary>Current schema version for <see cref="Persistence.AssociateDraft.SchemaVersion"/> — bump when this shape changes incompatibly.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The draft an operating date starts with before an associate has entered anything (spec §8.3's "no draft -> default draft").</summary>
    public static AssociateDraftPayload CreateDefault() => new(
        ManualHoursOverride.Disabled,
        PrivateBuyoutOverride.Disabled,
        ProjectedAttendance: null,
        PreSolds: null,
        EarlyRideTimeDetails.Empty,
        OperationalNotes: [],
        DailyFunType: null,
        DailyFunText: null,
        ProductOverrides: new Dictionary<string, ProductDraftOverride>(),
        CustomProducts: []);
}
