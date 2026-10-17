using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Shows;

/// <summary>Phase 21C: reads one date's official show schedule from local storage. Never goes online — retrieval belongs to the refresh coordinator.</summary>
public interface IShowScheduleService
{
    Task<ShowScheduleView> GetAsync(DateOnly date, CancellationToken cancellationToken = default);
}

public sealed class ShowScheduleService(IDatedResultRepository datedResultRepository, IClock clock) : IShowScheduleService
{
    public async Task<ShowScheduleView> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var record = await datedResultRepository.GetAsync(new DatedResultKey(SourceCategory.ShowSchedule, date), cancellationToken).ConfigureAwait(false);
        return ShowScheduleView.Compose(date, record, clock.UtcNow);
    }
}
