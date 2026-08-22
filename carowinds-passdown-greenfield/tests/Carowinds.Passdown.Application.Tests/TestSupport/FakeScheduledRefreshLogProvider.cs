using Carowinds.Passdown.Application.Diagnostics;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class FakeScheduledRefreshLogProvider(IReadOnlyList<string> lines) : IScheduledRefreshLogProvider
{
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<string>> GetRecentLogLinesAsync(int maxLines, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(lines);
    }
}
