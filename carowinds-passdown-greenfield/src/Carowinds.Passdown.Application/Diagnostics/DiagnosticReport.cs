using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Diagnostics;

/// <summary>Derived, non-fabricated health of one registered source (Phase 7F Part I) — computed from the same <see cref="Persistence.SourceStateRecord"/> the refresh orchestrator already persists, never from a fresh network call (Part I "must be local-only").</summary>
public enum DiagnosticSourceStatus
{
    NeverChecked,
    Ok,
    Error,
}

/// <summary>One row of the source-status summary (Phase 7F Part I/D) — timestamps and a derived status only, never <see cref="Domain.Retrieval.SourceEvidence.TechnicalDetail"/> or any retrieved payload.</summary>
public sealed record SourceStatusSummary(
    string SourceId,
    string DisplayName,
    SourceCategory Category,
    DiagnosticSourceStatus Status,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    int ConsecutiveFailureCount);

/// <summary>Local database metadata only (Phase 7F Part J) — existence, schema version, file size, WAL presence. Never table contents, drafts, or product rows.</summary>
public sealed record DatabaseDiagnostics(
    bool Exists,
    int? SchemaVersion,
    long? FileSizeBytes,
    bool WalPresent,
    string? Note);

/// <summary>Whether the "Carowinds Passdown Daily Refresh" task appears registered (Phase 7F Part K) — existence only, no attempt to parse every Task Scheduler field.</summary>
public enum ScheduledTaskDiagnosticStatus
{
    Enabled,
    NotFound,
    NotAvailableOnThisPlatform,
    Unknown,
}

public sealed record ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus Status, string? Detail);

/// <summary>
/// The full sanitized diagnostic snapshot (Phase 7F Parts D/E/I/J/K) handed to
/// <see cref="Carowinds.Passdown.Application.Diagnostics.IDiagnosticReportBuilder"/>'s caller. Everything on this
/// type is either already-persisted scheduling metadata, already-sanitized log lines (Part H), or
/// local file/OS metadata — never a draft, a product row, a full HTTP payload, or a secret.
/// </summary>
public sealed record DiagnosticReport(
    string AppVersion,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<SourceStatusSummary> Sources,
    DatabaseDiagnostics Database,
    ScheduledTaskDiagnostics ScheduledTask,
    IReadOnlyList<string> RecentLogLines,
    IReadOnlyList<string>? RecentAppLogLines = null)
{
    /// <summary>Phase 18B REL-005: bounded, path-redacted tail of the interactive app log; empty when none exists.</summary>
    public IReadOnlyList<string> RecentAppLogLines { get; init; } = RecentAppLogLines ?? [];

    /// <summary>Phase 18D: the running executable's full path. Exported files only ever print it redacted (<see cref="DiagnosticReportFormatter"/>).</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Phase 19A: the aggregate official-data status the in-app automatic refresh works from (derived from <see cref="Sources"/>' persisted state; never a network call).</summary>
    public OfficialDataStatus? OfficialData { get; init; }

    /// <summary>Phase 19B: aggregate catalog lifecycle and season-tag counts — counts only, never product rows.</summary>
    public CatalogLifecycleDiagnostics? CatalogLifecycle { get; init; }

    /// <summary>Phase 20A: season price-history counts and the latest change time — never product names or prices.</summary>
    public Persistence.SeasonPriceHistorySummary? SeasonPriceHistory { get; init; }
}

/// <summary>
/// Phase 19B: how many official catalog products are currently active, awaiting a confirming absence
/// check, or inactive (retired after confirmed absence, explicitly unavailable, or expired), plus the
/// active products' non-exclusive season-tag counts (a product with two tags counts under both).
/// </summary>
public sealed record CatalogLifecycleDiagnostics(
    int Active,
    int PendingConfirmation,
    int Inactive,
    int ActiveCarowinds,
    int ActiveSCarowinds,
    int ActiveWinterFest,
    int ActiveUnclassified);
