namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// The outcome of one refresh job (Part I). Never discards an individual source's result just
/// because another source in the same job failed (Part G/I) — <see cref="Results"/> always holds
/// one entry per planned operation, including cancelled ones.
/// </summary>
public sealed record RefreshSummary(
    RefreshJobType JobType,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int OperationsAttempted,
    int Succeeded,
    int Failed,
    int Cancelled,
    IReadOnlyList<SourceOperationResult> Results);
