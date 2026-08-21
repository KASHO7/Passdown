using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// UI-neutral verification state for one piece of official data (spec §5.2/§6.7/§6.3's "Not
/// checked yet"). A 1:1 relabeling of <see cref="RetrievalStatus"/> plus the one extra state
/// <see cref="RetrievalStatus"/> itself cannot express: nothing has ever been attempted at all.
/// Carries no Avalonia/formatting concerns — a future ViewModel decides how each state renders.
/// </summary>
public enum AssociateVerificationState
{
    /// <summary>No retrieval attempt has ever completed for this data (spec §6.3).</summary>
    NotCheckedYet,
    Verified,
    Closed,
    NotPublished,
    Unavailable,
    Error,
    Stale,
}

/// <summary>
/// What a future UI needs to render a "last verified"/staleness indicator (spec §5.3/§6.7), with
/// no presentation formatting baked in.
/// </summary>
public sealed record AssociateVerificationInfo(
    AssociateVerificationState State,
    DateTimeOffset? LastVerifiedAt,
    TimeSpan? StaleAge,
    string? Detail)
{
    public static AssociateVerificationInfo NotCheckedYet { get; } = new(AssociateVerificationState.NotCheckedYet, null, null, null);

    /// <summary>Projects Domain's <see cref="LastKnownGood{TValue}"/> reconstruction (already the single source of truth for stale/verified logic, spec §6.7) into this UI-neutral shape.</summary>
    public static AssociateVerificationInfo From<TValue>(LastKnownGood<TValue> lastKnownGood, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(lastKnownGood);

        var effectiveStatus = lastKnownGood.EffectiveStatus;
        if (effectiveStatus is null)
        {
            return NotCheckedYet;
        }

        var detail = lastKnownGood.IsStale
            ? lastKnownGood.LastAttemptEvidence?.Detail ?? lastKnownGood.SuccessEvidence?.Detail
            : lastKnownGood.SuccessEvidence?.Detail ?? lastKnownGood.LastAttemptEvidence?.Detail;

        return new AssociateVerificationInfo(Map(effectiveStatus.Value), lastKnownGood.SuccessEvidence?.TimeChecked, lastKnownGood.Age(asOf), detail);
    }

    private static AssociateVerificationState Map(RetrievalStatus status) => status switch
    {
        RetrievalStatus.Verified => AssociateVerificationState.Verified,
        RetrievalStatus.Closed => AssociateVerificationState.Closed,
        RetrievalStatus.NotPublished => AssociateVerificationState.NotPublished,
        RetrievalStatus.Unavailable => AssociateVerificationState.Unavailable,
        RetrievalStatus.Error => AssociateVerificationState.Error,
        RetrievalStatus.Stale => AssociateVerificationState.Stale,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unrecognized retrieval status."),
    };
}
