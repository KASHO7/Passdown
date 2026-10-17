using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// Phase 21C: a <see cref="DatedSourceRegistration{TPayload}"/> that only ever retrieves today and
/// future Eastern dates. A past date is skipped before any network request and before any write, so a
/// Check Online on a historical date can never replace that date's previously verified result with a
/// new "unavailable" answer (spec Part H "historical schedules are not fabricated", Part C "last-known-
/// good"). Used for the official show schedule, whose past-date answers are not an established
/// historical source (docs/phase21c-official-show-schedules.md).
/// </summary>
public sealed class CurrentAndFutureDatedSourceRegistration<TPayload> : SourceRegistration
{
    public const string PastDateDetail = "Past show schedules are not retrieved; previously recorded schedules are kept.";

    private readonly DatedSourceRegistration<TPayload> _inner;
    private readonly IClock _clock;

    public CurrentAndFutureDatedSourceRegistration(IOfficialDataSource<TPayload> source, IDatedResultRepository datedResultRepository, IClock clock)
        : base(source.SourceId, source.Category, requiresBrowserSession: false)
    {
        _inner = new DatedSourceRegistration<TPayload>(source, datedResultRepository);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public override SourceScope Scope => SourceScope.Dated;

    public override Task<SourceOperationResult> ExecuteAsync(DateOnly? requestedDate, CancellationToken cancellationToken)
    {
        if (requestedDate is { } date && date < DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(_clock.UtcNow).DateTime))
        {
            return Task.FromResult(SourceOperationResult.Success(SourceId, Category, date, RetrievalStatus.Unavailable, PastDateDetail));
        }

        return _inner.ExecuteAsync(requestedDate, cancellationToken);
    }
}
