using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Application.Retrieval;

/// <summary>
/// Input to a single <see cref="IOfficialDataSource{TPayload}.RetrieveAsync"/> call — what to
/// check and, for dated products, which date (spec §16's suggested <c>RetrievalRequest</c>).
/// Immutable and adapter-neutral: no HTTP/Playwright-specific fields belong here, only what
/// every adapter needs before it decides how to fetch. Cancellation is deliberately not part of
/// this value — it is a per-call concern passed alongside the request to
/// <see cref="IOfficialDataSource{TPayload}.RetrieveAsync"/>, the same way every repository port
/// under <c>Persistence</c> takes its own <see cref="CancellationToken"/> parameter rather than
/// carrying one on the request object.
/// </summary>
public sealed record RetrievalRequest
{
    public string SourceId { get; }

    public SourceCategory Category { get; }

    /// <summary>The date to check, for per-date sources (spec §6.2's dated-results store). Null for catalog-scoped sources.</summary>
    public DateOnly? RequestedDate { get; }

    public RetrievalRequest(string sourceId, SourceCategory category, DateOnly? requestedDate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        SourceId = sourceId;
        Category = category;
        RequestedDate = requestedDate;
    }
}
