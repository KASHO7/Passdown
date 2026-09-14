using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class InMemoryErtDateOverrideRepository : IErtDateOverrideRepository
{
    private readonly Dictionary<DateOnly, ErtDateOverride> _overrides = new();

    public Task<ErtDateOverride?> GetAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        Task.FromResult(_overrides.GetValueOrDefault(operatingDate));

    public Task UpsertAsync(ErtDateOverride dateOverride, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _overrides[dateOverride.OperatingDate] = dateOverride;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(DateOnly operatingDate, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _overrides.Remove(operatingDate);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ErtDateOverride>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ErtDateOverride>>(_overrides.Values.OrderBy(o => o.OperatingDate).ToList());
}
