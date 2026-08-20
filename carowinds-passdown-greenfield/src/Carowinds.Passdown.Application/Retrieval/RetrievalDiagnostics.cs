using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Consistent construction of <see cref="SourceEvidence"/> for failed retrievals, so every
/// adapter reports an <see cref="RetrievalStatus.Error"/> the same way (spec §5.3): a plain,
/// non-alarming <see cref="SourceEvidence.Detail"/> suitable for display, with the exception's
/// full detail kept separately in <see cref="SourceEvidence.TechnicalDetail"/> for logs only.
/// Never derives <c>Detail</c> from the exception itself — a human-readable sentence must always
/// be supplied by the caller, who knows what the user was trying to do; the exception is not
/// user-facing text (spec §5.2's "never fabricate").
/// </summary>
public static class RetrievalDiagnostics
{
    public static SourceEvidence Error(
        string sourceName,
        Uri sourceUrl,
        DateTimeOffset timeChecked,
        RetrievalStrategy strategy,
        string userFacingDetail,
        Exception exception,
        DateOnly? requestedDate = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new SourceEvidence(
            sourceName,
            sourceUrl,
            timeChecked,
            RetrievalStatus.Error,
            userFacingDetail,
            strategy,
            requestedDate: requestedDate,
            technicalDetail: exception.ToString());
    }
}
