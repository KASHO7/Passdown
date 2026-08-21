using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>Outcome bucket for one completed source/date operation, independent of the raw <see cref="RetrievalStatus"/> (Phase 3G Part I/H).</summary>
public enum SourceOperationOutcomeKind
{
    /// <summary>The source completed successfully (dated: any <see cref="RetrievalStatusExtensions.IsSuccessfulCompletion"/> status; catalog: <see cref="RetrievalStatus.Verified"/>).</summary>
    Success,

    /// <summary>The source errored, or the operation threw unexpectedly.</summary>
    Failure,

    /// <summary>The job was cancelled before this operation completed.</summary>
    Cancelled,
}

/// <summary>
/// The result of one planned source/date operation within a refresh job (Phase 3G Part H/I) —
/// what a future UI's per-row progress display and the job's final <see cref="RefreshSummary"/>
/// are both built from. <see cref="RequestedDate"/> is null for catalog-scoped sources.
/// </summary>
public sealed record SourceOperationResult(
    string SourceId,
    SourceCategory Category,
    DateOnly? RequestedDate,
    SourceOperationOutcomeKind Outcome,
    RetrievalStatus? Status,
    string Detail)
{
    public static SourceOperationResult Success(string sourceId, SourceCategory category, DateOnly? requestedDate, RetrievalStatus status, string detail) =>
        new(sourceId, category, requestedDate, SourceOperationOutcomeKind.Success, status, detail);

    public static SourceOperationResult Failure(string sourceId, SourceCategory category, DateOnly? requestedDate, RetrievalStatus? status, string detail) =>
        new(sourceId, category, requestedDate, SourceOperationOutcomeKind.Failure, status, detail);

    public static SourceOperationResult Cancelled(string sourceId, SourceCategory category, DateOnly? requestedDate) =>
        new(sourceId, category, requestedDate, SourceOperationOutcomeKind.Cancelled, Status: null, "Cancelled before completion.");
}
