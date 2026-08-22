using Carowinds.Passdown.Infrastructure.Concurrency;

namespace Carowinds.Passdown.Infrastructure.Tests.Concurrency;

/// <summary>Phase 6B Part A/N (#1-5): the cross-process overlap guard behind <c>--scheduled-refresh</c>.</summary>
public class FileScheduledRefreshLockTests : IDisposable
{
    private readonly string _lockPath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-lock-tests-{Guid.NewGuid():N}.lock");

    public void Dispose()
    {
        if (File.Exists(_lockPath))
        {
            File.Delete(_lockPath);
        }
    }

    // 1. First scheduled instance acquires the lock.
    [Fact]
    public void TryAcquire_FirstHolder_Succeeds()
    {
        using var first = new FileScheduledRefreshLock(_lockPath);

        Assert.True(first.TryAcquire());
    }

    // 2. Second overlapping instance detects the active run.
    [Fact]
    public void TryAcquire_SecondHolderWhileFirstStillHolds_Fails()
    {
        using var first = new FileScheduledRefreshLock(_lockPath);
        using var second = new FileScheduledRefreshLock(_lockPath);

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    // 4. Lock releases on normal completion, letting a subsequent run acquire it.
    [Fact]
    public void TryAcquire_AfterFirstHolderDisposes_SecondHolderSucceeds()
    {
        var first = new FileScheduledRefreshLock(_lockPath);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var second = new FileScheduledRefreshLock(_lockPath);
        Assert.True(second.TryAcquire());
    }

    // 5. Lock releases after exception/process cleanup as safely as the platform permits: Dispose
    // must be safe to call from a finally block even if TryAcquire never succeeded, and safe to
    // call more than once.
    [Fact]
    public void Dispose_WithoutHavingAcquired_DoesNotThrow()
    {
        using var scheduledLock = new FileScheduledRefreshLock(_lockPath);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var scheduledLock = new FileScheduledRefreshLock(_lockPath);
        Assert.True(scheduledLock.TryAcquire());

        scheduledLock.Dispose();
        scheduledLock.Dispose();
    }

    [Fact]
    public void TryAcquire_CalledTwiceOnSameHolder_StaysTheHolder()
    {
        using var first = new FileScheduledRefreshLock(_lockPath);

        Assert.True(first.TryAcquire());
        Assert.True(first.TryAcquire());
    }

    [Fact]
    public void DefaultPath_IsUnderAppPathsLocalApplicationDataDirectory()
    {
        Assert.StartsWith(AppPaths.LocalApplicationDataDirectory, FileScheduledRefreshLock.DefaultPath);
    }
}
