using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IOfficialDataSource{TPayload}"/> for orchestration tests. Never touches the network.</summary>
internal sealed class FakeOfficialDataSource<TPayload>(
    string sourceId,
    SourceCategory category,
    Func<RetrievalRequest, CancellationToken, Task<SourceResult<TPayload>>> handler) : IOfficialDataSource<TPayload>
{
    public string SourceId { get; } = sourceId;

    public SourceCategory Category { get; } = category;

    public List<RetrievalRequest> Requests { get; } = [];

    public int CallCount { get; private set; }

    public Task<SourceResult<TPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        CallCount++;
        return handler(request, cancellationToken);
    }
}

internal static class FakeEvidence
{
    private static readonly Uri Url = new("https://example.test/fake-source");

    public static SourceEvidence Verified(DateTimeOffset timeChecked, DateOnly? requestedDate = null) =>
        new("Fake Source", Url, timeChecked, RetrievalStatus.Verified, "Verified.", RetrievalStrategy.DirectApi, requestedDate, confirmedDate: requestedDate);

    public static SourceEvidence Error(DateTimeOffset timeChecked, DateOnly? requestedDate = null) =>
        new("Fake Source", Url, timeChecked, RetrievalStatus.Error, "Failed.", RetrievalStrategy.DirectApi, requestedDate, technicalDetail: "Simulated failure.");
}

internal sealed record FakePayload(string Value);
