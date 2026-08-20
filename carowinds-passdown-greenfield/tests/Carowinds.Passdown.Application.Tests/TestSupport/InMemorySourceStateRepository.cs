using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class InMemorySourceStateRepository : ISourceStateRepository
{
    private readonly Dictionary<string, SourceStateRecord> _states = new();

    public Task<SourceStateRecord?> GetAsync(string sourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_states.GetValueOrDefault(sourceId));

    public Task SaveAsync(SourceStateRecord state, CancellationToken cancellationToken = default)
    {
        _states[state.SourceId] = state;
        return Task.CompletedTask;
    }
}
