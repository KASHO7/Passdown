using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class InMemoryMonthlyErtRepository : IMonthlyErtRepository
{
    private readonly Dictionary<string, MonthlyErtConfiguration> _configurations = new();

    public Task<MonthlyErtConfiguration?> GetAsync(int year, int month, CancellationToken cancellationToken = default) =>
        Task.FromResult(_configurations.GetValueOrDefault(MonthlyErtConfiguration.FormatKey(year, month)));

    public Task UpsertAsync(MonthlyErtConfiguration configuration, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _configurations[configuration.YearMonthKey] = configuration;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int year, int month, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _configurations.Remove(MonthlyErtConfiguration.FormatKey(year, month));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MonthlyErtConfiguration>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MonthlyErtConfiguration>>(_configurations.Values.OrderBy(c => c.YearMonthKey).ToList());
}
