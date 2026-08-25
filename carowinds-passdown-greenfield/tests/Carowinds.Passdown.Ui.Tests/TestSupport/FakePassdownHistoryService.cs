using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IPassdownHistoryService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.PassdownHistoryViewModel"/> tests — never touches SQLite.</summary>
internal sealed class FakePassdownHistoryService : IPassdownHistoryService
{
    public List<FinalizedPassdownSummary> Summaries { get; set; } = [];

    public Dictionary<DateOnly, PassdownHistoryDetail?> Details { get; } = new();

    public List<DateOnly> GetCalls { get; } = [];

    public int ListCallCount { get; private set; }

    public Func<Task>? ListGate { get; set; }

    public Func<Task>? GetGate { get; set; }

    public Exception? ListException { get; set; }

    public Exception? GetException { get; set; }

    public async Task<IReadOnlyList<FinalizedPassdownSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCallCount++;
        if (ListGate is not null)
        {
            await ListGate();
        }

        if (ListException is not null)
        {
            throw ListException;
        }

        return Summaries.OrderByDescending(s => s.PassdownDate).ToList();
    }

    public async Task<PassdownHistoryDetail?> GetAsync(DateOnly passdownDate, CancellationToken cancellationToken = default)
    {
        GetCalls.Add(passdownDate);
        if (GetGate is not null)
        {
            await GetGate();
        }

        if (GetException is not null)
        {
            throw GetException;
        }

        return Details.GetValueOrDefault(passdownDate);
    }
}
