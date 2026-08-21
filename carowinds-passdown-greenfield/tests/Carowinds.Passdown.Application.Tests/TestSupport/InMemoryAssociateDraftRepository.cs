using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class InMemoryAssociateDraftRepository : IAssociateDraftRepository
{
    private readonly Dictionary<DateOnly, AssociateDraft> _drafts = new();

    public Task<AssociateDraft?> GetAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        Task.FromResult(_drafts.GetValueOrDefault(operatingDate));

    public Task SaveAsync(AssociateDraft draft, CancellationToken cancellationToken = default)
    {
        _drafts[draft.OperatingDate] = draft;
        return Task.CompletedTask;
    }
}
