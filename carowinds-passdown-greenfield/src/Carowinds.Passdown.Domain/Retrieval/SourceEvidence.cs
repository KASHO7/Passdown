namespace Carowinds.Passdown.Domain.Retrieval;

/// <summary>
/// Evidence retained for a single source retrieval attempt, per spec §5.3.
/// Immutable — a new attempt produces a new instance rather than mutating this one.
/// </summary>
public sealed record SourceEvidence
{
    public string SourceName { get; }
    public Uri SourceUrl { get; }
    public DateOnly? RequestedDate { get; }
    public DateOnly? ConfirmedDate { get; }
    public DateTimeOffset TimeChecked { get; }
    public RetrievalStatus Status { get; }
    public string Detail { get; }
    public string? TechnicalDetail { get; }
    public RetrievalStrategy Strategy { get; }

    public SourceEvidence(
        string sourceName,
        Uri sourceUrl,
        DateTimeOffset timeChecked,
        RetrievalStatus status,
        string detail,
        RetrievalStrategy strategy,
        DateOnly? requestedDate = null,
        DateOnly? confirmedDate = null,
        string? technicalDetail = null)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("Source name is required.", nameof(sourceName));
        if (string.IsNullOrWhiteSpace(detail))
            throw new ArgumentException("Human-readable detail is required.", nameof(detail));
        if (status == RetrievalStatus.Stale)
        {
            throw new ArgumentException(
                "Stale is a derived status, not a raw retrieval outcome (spec §5.2).",
                nameof(status));
        }

        if (status == RetrievalStatus.Verified && requestedDate is not null && confirmedDate != requestedDate)
        {
            throw new ArgumentException(
                "A verified result for a specific date must confirm that exact date — " +
                "never a fallback to today or another date (spec §5.4).",
                nameof(confirmedDate));
        }

        SourceName = sourceName;
        SourceUrl = sourceUrl ?? throw new ArgumentNullException(nameof(sourceUrl));
        TimeChecked = timeChecked;
        Status = status;
        Detail = detail;
        Strategy = strategy;
        RequestedDate = requestedDate;
        ConfirmedDate = confirmedDate;
        TechnicalDetail = technicalDetail;
    }
}
