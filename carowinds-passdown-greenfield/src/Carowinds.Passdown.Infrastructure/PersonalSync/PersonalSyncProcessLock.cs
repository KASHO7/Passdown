namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// Phase 16F §26: the minimum safe cross-process guard against two instances of this app (same device,
/// same Windows profile — the scenario the phase spec calls out, not two different devices, which
/// reconciliation/conflict detection already handles) running a Personal Sync pass at the same moment.
/// <see cref="PersonalSyncService"/>'s own in-process <c>SemaphoreSlim</c> only ever protects one
/// process; this adds one exclusively-held local lock file, mirroring
/// <see cref="Concurrency.FileScheduledRefreshLock"/>'s own proven approach (an OS-level
/// <see cref="FileShare.None"/> open refuses a second exclusive holder on both Windows and the Unix
/// hosts this solution is developed/tested on) rather than a named <see cref="Mutex"/> — a
/// <see cref="Mutex"/>'s ownership is thread-affine (<c>ReleaseMutex</c> must run on the exact thread
/// that acquired it), which a long-lived <c>async</c> sync pass cannot guarantee across its many
/// <c>await</c> points; a file handle has no such requirement. Deliberately a dedicated file, never
/// the shared OneDrive folder itself (spec's explicit "do NOT use the shared OneDrive folder as a
/// distributed lock across devices") and never <c>FileScheduledRefreshLock</c>'s own file (that lock
/// serializes scheduled *retrieval* refreshes; sharing it would make a sync pass wait on an unrelated,
/// possibly long-running refresh for no reason).
/// </summary>
internal static class PersonalSyncProcessLock
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "personal-sync.lock");

    /// <summary>
    /// Attempts to acquire the lock without blocking. Returns null if another process already holds it
    /// (the caller should behave exactly like <see cref="Carowinds.Passdown.Application.PersonalSync.PersonalSyncOutcome.AlreadyRunning"/>).
    /// </summary>
    public static IDisposable? TryAcquire(string? lockFilePath = null)
    {
        var path = lockFilePath ?? DefaultPath;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new Holder(stream);
        }
        catch (IOException)
        {
            // Sharing violation: another process already holds it.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Treated the same as "can't become the holder right now," not a fatal error — matches
            // FileScheduledRefreshLock's own handling of a transient permissions/AV-scan hiccup.
            return null;
        }
    }

    private sealed class Holder(FileStream stream) : IDisposable
    {
        public void Dispose() => stream.Dispose();
    }
}
