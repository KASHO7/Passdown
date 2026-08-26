using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Application.Dashboard;

/// <summary>
/// A supervisor-facing classification of today's operating status (Phase 10A Part F), derived
/// entirely from the same <see cref="AssociateParkHoursView"/> the Associate screen already
/// computes — never a second hours calculation.
/// </summary>
public enum DashboardParkStatus
{
    NotChecked,
    Closed,
    Open,
    CustomHours,
    PrivateBuyout,
    NotPublished,
    Unavailable,
}

/// <summary>Today's park-status classification plus the raw hours view it was derived from, so a UI can reuse the existing <c>ParkHoursPresentation</c> formatter directly instead of a second one.</summary>
public sealed record DashboardParkStatusSummary(DashboardParkStatus Status, AssociateParkHoursView Hours);

/// <summary>
/// Today's passdown draft/finalization state (Phase 10A Part G). A <c>PassdownDate</c> is never
/// eligible for finalization until 6:00 AM Eastern the day <em>after</em> it
/// (<c>Finalization.FinalizationEligibility</c>), so <see cref="Finalized"/> is not a state a
/// same-day dashboard snapshot for "today" will ever actually observe — it exists here so the same
/// read model stays correct if a caller ever loads a past operating date.
/// </summary>
public enum DashboardPassdownStatus
{
    NoDraft,
    DraftInProgress,
    Finalized,
}

public sealed record DashboardPassdownStatusSummary(
    DashboardPassdownStatus Status,
    DateTimeOffset? LastSavedAt,
    DateTimeOffset? FinalizedAt);

/// <summary>A compact aggregate over every source category's existing verification state (Phase 10A Part I) — no new freshness system, just a projection of what <see cref="AssociateWorkspace.CategoryStatuses"/>/<see cref="AssociateParkHoursView.Verification"/> already say.</summary>
public enum DashboardSourceHealth
{
    AllCurrent,
    SomeStale,
    SomeUnavailable,
}

public sealed record DashboardSourceHealthSummary(
    DashboardSourceHealth Health,
    int CurrentCount,
    int StaleCount,
    int UnavailableCount,
    int NotCheckedCount);

/// <summary>A read-only preview of today's operational notes (Phase 10A Part H) — never a second notes persistence model; <see cref="PreviewLines"/> is a prefix of the same <see cref="AssociateDraftPayload.OperationalNotes"/> list the Associate Notes screen edits.</summary>
public sealed record DashboardNotesSummary(bool HasNotes, IReadOnlyList<string> PreviewLines, int TotalLineCount)
{
    public static DashboardNotesSummary Empty { get; } = new(false, [], 0);
}

/// <summary>
/// The Application-layer projection a supervisor's Operations Dashboard is built from (Phase 10A
/// Part E) — a summary over already-composed <see cref="AssociateWorkspace"/> state and existing
/// finalization metadata, never a second business-rule implementation of park hours, freshness,
/// draft composition, or finalization eligibility. Produced entirely from local persisted state
/// (Phase 10A Part K/J) — no HTTP, no Playwright, no Task Scheduler API.
/// </summary>
public sealed record OperationsDashboardSnapshot(
    DateOnly OperatingDate,
    DashboardParkStatusSummary ParkStatus,
    DashboardPassdownStatusSummary PassdownStatus,
    int? ProjectedAttendance,
    int? PreSolds,
    bool EarlyRideTimeEnabled,
    string? EarlyRideTimeDetails,
    DailyFeatureType? DailyFeatureType,
    string? DailyFeatureText,
    DashboardNotesSummary Notes,
    DashboardSourceHealthSummary SourceHealth,
    DateTimeOffset GeneratedAt);
