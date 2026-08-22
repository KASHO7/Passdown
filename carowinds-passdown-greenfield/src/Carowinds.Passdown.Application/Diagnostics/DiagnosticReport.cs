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
    IReadOnlyList<string> RecentLogLines);
