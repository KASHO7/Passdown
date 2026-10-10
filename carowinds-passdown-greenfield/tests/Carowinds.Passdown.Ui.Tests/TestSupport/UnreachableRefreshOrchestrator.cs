using Carowinds.Passdown.Application.Refresh;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>
/// Phase 13A/15D: startup (composition root through first visible window) never calls
/// <see cref="IRefreshOrchestrator"/> — Associate/Dashboard/Calendar/History's own local loads only
/// read persisted state. Wiring this into a startup-timing harness turns "startup accidentally
/// reaches retrieval" from something that would just make the measurement wrong into something that
/// throws immediately.
/// </summary>
internal sealed class UnreachableRefreshOrchestrator : IRefreshOrchestrator
{
    public Task<RefreshSummary> RunScheduledRefreshAsync(IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Startup must never reach IRefreshOrchestrator.");

    public Task<RefreshSummary> RunUserForcedRefreshAsync(DateOnly date, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Startup must never reach IRefreshOrchestrator.");

    public Task<RefreshSummary> RunDeepRefreshAsync(bool bypassFreshness, DateOnly? focusDate = null, IProgress<RefreshProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Startup must never reach IRefreshOrchestrator.");
}
