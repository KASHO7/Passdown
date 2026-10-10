using System.Reflection;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>
/// Phase 16F §26: the minimum cross-process guard against two instances of this app running a
/// Personal Sync pass at the same moment. Uses reflection to reach the internal
/// <c>PersonalSyncProcessLock</c> type with an explicit temp path (never the real per-user
/// <c>AppPaths.LocalApplicationDataDirectory</c> location the production default uses, so this never
/// touches shared host state or races other tests).
/// </summary>
public class PersonalSyncProcessLockTests : IDisposable
{
    private readonly string _lockPath = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-lock-tests-{Guid.NewGuid():N}.lock");

    private static readonly Type LockType = typeof(Infrastructure.PersonalSync.PersonalSyncService).Assembly
        .GetType("Carowinds.Passdown.Infrastructure.PersonalSync.PersonalSyncProcessLock")!;

    public void Dispose()
    {
        if (File.Exists(_lockPath))
        {
            File.Delete(_lockPath);
        }
    }

    private static IDisposable? TryAcquire(string path)
    {
        var method = LockType.GetMethod("TryAcquire", BindingFlags.Public | BindingFlags.Static)!;
        return (IDisposable?)method.Invoke(null, [path]);
    }

    [Fact]
    public void TryAcquire_WhenFree_Succeeds()
    {
        using var handle = TryAcquire(_lockPath);

        Assert.NotNull(handle);
    }

    [Fact]
    public void TryAcquire_WhileAlreadyHeld_ReturnsNull_SimulatingASecondAppInstance()
    {
        using var first = TryAcquire(_lockPath);
        Assert.NotNull(first);

        var second = TryAcquire(_lockPath);

        Assert.Null(second);
    }

    [Fact]
    public void TryAcquire_AfterTheHolderReleases_SucceedsAgain()
    {
        var first = TryAcquire(_lockPath);
        Assert.NotNull(first);
        first!.Dispose();

        using var second = TryAcquire(_lockPath);

        Assert.NotNull(second);
    }
}
