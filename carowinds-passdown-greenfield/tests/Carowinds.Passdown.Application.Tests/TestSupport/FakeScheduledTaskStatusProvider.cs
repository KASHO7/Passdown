using Carowinds.Passdown.Application.Diagnostics;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

internal sealed class FakeScheduledTaskStatusProvider(ScheduledTaskDiagnostics result) : IScheduledTaskStatusProvider
{
    public Task<ScheduledTaskDiagnostics> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
}
