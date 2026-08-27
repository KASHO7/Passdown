using Carowinds.Passdown.Application.Concurrency;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>Scriptable <see cref="IScheduledRefreshLock"/> for <c>AssociatePassdownService</c> overlap tests (Phase 13B Part K/L) — never touches a real file.</summary>
internal sealed class FakeScheduledRefreshLock : IScheduledRefreshLock
{
    public bool AcquireSucceeds { get; set; } = true;

    public int TryAcquireCallCount { get; private set; }

    public int DisposeCallCount { get; private set; }

    public bool TryAcquire()
    {
        TryAcquireCallCount++;
        return AcquireSucceeds;
    }

    public void Dispose() => DisposeCallCount++;
}
