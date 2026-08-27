using Carowinds.Passdown.Application.Concurrency;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Scriptable <see cref="IScheduledRefreshLock"/> for host/startup-catch-up tests — never touches a real file.</summary>
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
