using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain;

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

/// <summary>
/// A per-source, supervisor-facing classification (Phase 10D Part A/T) — a 1:1 relabeling of
/// <see cref="AssociateVerificationState"/> (never a new freshness system): <see cref="Verified"/>
/// and <see cref="Closed"/> above both collapse to <see cref="Current"/> because a genuinely
/// verified "the park is closed today" is an operationally valid result, not a confidence problem
/// (Part D); <see cref="Error"/> is renamed <see cref="VerificationIssue"/> for plain-language
/// display (Part T) without changing its meaning.
/// </summary>
public enum DashboardSourceStatus
{
    NotChecked,
    Current,
    Stale,
    NotPublished,
    Unavailable,
    VerificationIssue,
}

/// <summary>
/// One source category's compact status (Phase 10D Part H). Exactly one of
/// <see cref="LastVerifiedAt"/>/<see cref="LastCheckedAt"/> is ever populated, never both, so a
/// caller can never accidentally present an attempt-only timestamp as "Last verified" (Part J):
/// <see cref="LastVerifiedAt"/> is set only when the underlying data can prove the timestamp
/// belongs to a successful completion (<see cref="AssociateVerificationInfo.LastVerifiedAt"/> for
/// Park Hours, <see cref="Persistence.CatalogProductRecord.LastVerifiedAt"/> for catalog
/// categories — both success-only by construction); <see cref="LastCheckedAt"/> is used instead for
/// the three dated product categories, whose only available timestamp
/// (<see cref="AssociateProductCategoryStatus.LastCheckedAt"/>) is documented as "most recent
/// attempt of any outcome" and so is never safe to relabel "verified".
/// </summary>
public sealed record DashboardSourceStatusEntry(
    SourceCategory Category,
    DashboardSourceStatus Status,
    DateTimeOffset? LastVerifiedAt,
    DateTimeOffset? LastCheckedAt);

/// <summary>
/// The Dashboard's compact overall data-confidence classification (Phase 10D Part C), aggregated
/// over every <see cref="DashboardSourceStatusEntry"/>. Deterministic precedence, worst first:
/// <see cref="VerificationIssue"/> &gt; <see cref="SomeStale"/> &gt; <see cref="SomeUnavailable"/> &gt;
/// <see cref="NoPublishedData"/> (nothing is currently confirmed at all) &gt; <see cref="Current"/>.
/// A <see cref="DashboardSourceStatus.NotPublished"/> source never degrades the overall state by
/// itself when at least one other source is <see cref="DashboardSourceStatus.Current"/> (Part E:
/// "not published yet" is informational, not a confidence failure) — it only surfaces as
/// <see cref="NoPublishedData"/> when nothing else is confirmed either.
/// </summary>
public enum DashboardOverallDataStatus
{
    Current,
    SomeStale,
    SomeUnavailable,
    VerificationIssue,
    NoPublishedData,
}

/// <summary>
/// The Dashboard's compact data-confidence read model (Phase 10D Part C/R) — a richer replacement
/// for Phase 10A's original 3-state <c>DashboardSourceHealthSummary</c>, built over the exact same
/// per-category <see cref="AssociateVerificationState"/> values (never a new freshness system).
/// <see cref="LatestSuccessfulVerification"/> is the max of every source's own success-only
/// timestamp (Part J) — deliberately excludes the three dated categories' attempt-only
/// <see cref="DashboardSourceStatusEntry.LastCheckedAt"/>, so this can never be a failed attempt's
/// time.
/// </summary>
public sealed record DashboardDataHealthSummary(
    DashboardOverallDataStatus OverallStatus,
    int CurrentCount,
    int StaleCount,
    int UnavailableCount,
    int VerificationIssueCount,
    int NotPublishedCount,
    int NotCheckedCount,
    IReadOnlyList<DashboardSourceStatusEntry> Sources,
    DateTimeOffset? LatestSuccessfulVerification);

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
/// <param name="LatestFinalized">
/// The most recently operated finalized passdown — "latest" meaning the highest
/// <see cref="FinalizedPassdown.PassdownDate"/>, not the most-recently-inserted row or the most
/// recent <see cref="FinalizedPassdown.FinalizedAtUtc"/> (Phase 10C Part C). <c>null</c> when no
/// passdown has ever been finalized. Reuses the same metadata-only
/// <see cref="FinalizedPassdownSummary"/> type <see cref="IFinalizedPassdownRepository.ListAsync"/>
/// and the History screen already use — never <see cref="FinalizedPassdown.SnapshotJson"/> or
/// <see cref="FinalizedPassdown.PdfBytes"/> (Phase 10C Parts A/L/O).
/// </param>
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
    DashboardDataHealthSummary DataHealth,
    FinalizedPassdownSummary? LatestFinalized,
    DateTimeOffset GeneratedAt);
