using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class InMemoryAssociateDraftRepository : IAssociateDraftRepository
{
    private readonly Dictionary<DateOnly, AssociateDraft> _drafts = new();

    public Task<AssociateDraft?> GetAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        Task.FromResult(_drafts.GetValueOrDefault(operatingDate));

    public Task SaveAsync(AssociateDraft draft, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _drafts[draft.OperatingDate] = draft;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(DateOnly operatingDate, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _drafts.Remove(operatingDate);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DateOnly>> ListSavedDraftDatesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DateOnly> dates = _drafts.Keys.OrderBy(d => d).ToList();
        return Task.FromResult(dates);
    }

    public Task<IReadOnlyList<AssociateDraft>> GetByDateRangeAsync(DateOnly startDate, DateOnly endDateInclusive, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AssociateDraft> drafts = _drafts.Values
            .Where(d => d.OperatingDate >= startDate && d.OperatingDate <= endDateInclusive)
            .OrderBy(d => d.OperatingDate)
            .ToList();
        return Task.FromResult(drafts);
    }
}
