using Carowinds.Passdown.Application.Concurrency;

namespace Carowinds.Passdown.Infrastructure.Concurrency;

/// <summary>
/// <see cref="IScheduledRefreshLock"/> backed by one exclusively-held local file (Phase 6B Part
/// A). <see cref="TryAcquire"/> opens <see cref="LockFilePath"/> with <see cref="FileShare.None"/>:
/// the OS itself refuses a second exclusive open of the same path while the first handle is still
/// open, on both Windows and the Unix hosts this solution is developed/tested on, so no second
/// scheduled-refresh process can ever proceed past the check. The file's contents are never read
/// or written — its only purpose is to exist as something the OS can hold exclusively — and it is
/// deliberately never deleted, so a concurrent second attempt always has the same stable path to
/// contend on rather than racing a create/delete cycle.
/// </summary>
public sealed class FileScheduledRefreshLock(string lockFilePath) : IScheduledRefreshLock
{
    /// <summary>The default per-user lock path, shared by every caller that needs to agree on the same lock (the scheduled-refresh host and the interactive startup catch-up trigger alike).</summary>
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "scheduled-refresh.lock");

    public string LockFilePath { get; } = lockFilePath;

    private FileStream? _stream;

    public bool TryAcquire()
    {
        if (_stream is not null)
        {
            return true; // Already the holder.
        }

        try
        {
            var directory = Path.GetDirectoryName(LockFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _stream = new FileStream(LockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            // Sharing violation: another holder already has it (Windows and Unix both surface
            // this as IOException, not a distinct "locked" status code).
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Treated the same as "can't become the holder right now" rather than a crash — a
            // transient permissions/AV-scan hiccup should behave like a busy lock, not a fatal error.
            return false;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
