using Carowinds.Passdown.Application.Finalization;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Scriptable <see cref="IScheduledPassdownFinalizer"/> for host/startup-catch-up ordering tests — never touches a real draft/finalized-passdown repository.</summary>
internal sealed class FakeScheduledPassdownFinalizer : IScheduledPassdownFinalizer
{
    public int CallCount { get; private set; }

    public Func<CancellationToken, Task<ScheduledFinalizationSummary>>? OnFinalize { get; set; }

    public ScheduledFinalizationSummary ResultToReturn { get; set; } = ScheduledFinalizationSummary.None;

    public Task<ScheduledFinalizationSummary> FinalizeEligibleAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        return OnFinalize is not null ? OnFinalize(cancellationToken) : Task.FromResult(ResultToReturn);
    }
}
