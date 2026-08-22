namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// Process-level classification of one <see cref="IScheduledRefreshRunner"/> run (Phase 6A Part
/// E). Deliberately coarser than <see cref="RefreshSummary"/>: a source returning
/// <c>Unavailable</c>/<c>NotPublished</c>/<c>Closed</c> is a successful completion (spec §5.2 —
/// those are legitimate source statuses, not errors), so it can never push this past
/// <see cref="Success"/> on its own. Only a genuine <see cref="SourceOperationOutcomeKind.Failure"/>
/// does that.
/// </summary>
public enum ScheduledRefreshOutcome
{
    /// <summary>Every planned operation completed without a <see cref="SourceOperationOutcomeKind.Failure"/>.</summary>
    Success,

    /// <summary>At least one planned operation resulted in <see cref="SourceOperationOutcomeKind.Failure"/>, but the job itself ran to completion.</summary>
    PartialFailure,
}
