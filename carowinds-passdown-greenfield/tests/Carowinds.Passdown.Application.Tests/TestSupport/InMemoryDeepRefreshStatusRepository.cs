using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class InMemoryDeepRefreshStatusRepository : IDeepRefreshStatusRepository
{
    private DeepRefreshStatusRecord? _status;

    public Task<DeepRefreshStatusRecord?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_status);

    public Task SaveAsync(DeepRefreshStatusRecord status, CancellationToken cancellationToken = default)
    {
        _status = status;
        return Task.CompletedTask;
    }
}
