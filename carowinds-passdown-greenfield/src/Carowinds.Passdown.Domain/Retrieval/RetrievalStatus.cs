namespace Carowinds.Passdown.Domain.Retrieval;

/// <summary>
/// The explicit outcome of a source retrieval, per spec §5.2. Every retrieval
/// must resolve to exactly one of these values — never a guessed substitute.
/// </summary>
public enum RetrievalStatus
{
    /// <summary>Exact date or current catalog value was confirmed.</summary>
    Verified,

    /// <summary>The official calendar explicitly marks the date closed.</summary>
    Closed,

    /// <summary>The requested date or schedule is not yet published.</summary>
    NotPublished,

    /// <summary>The date exists but that product/value is unavailable.</summary>
    Unavailable,

    /// <summary>The source could not be completed.</summary>
    Error,

    /// <summary>
    /// Derived only: only older last-known-good information exists. Never a raw
    /// outcome of a single retrieval attempt — see <see cref="Retrieval.LastKnownGood{TValue}"/>.
    /// </summary>
    Stale,
}

public static class RetrievalStatusExtensions
{
    /// <summary>
    /// True when the source was reached and gave a definitive answer — including
    /// negative answers like <see cref="RetrievalStatus.Closed"/> or
    /// <see cref="RetrievalStatus.Unavailable"/>. False only for <see cref="RetrievalStatus.Error"/>
    /// and the derived <see cref="RetrievalStatus.Stale"/> status. Spec §6.7: a failed source must
    /// not overwrite a previously verified value, so this is what gates last-known-good updates.
    /// </summary>
    public static bool IsSuccessfulCompletion(this RetrievalStatus status) =>
        status is RetrievalStatus.Verified
            or RetrievalStatus.Closed
            or RetrievalStatus.NotPublished
            or RetrievalStatus.Unavailable;
}
