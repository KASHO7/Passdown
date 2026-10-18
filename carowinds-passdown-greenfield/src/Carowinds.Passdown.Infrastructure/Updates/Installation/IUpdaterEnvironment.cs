using System.Diagnostics;

namespace Carowinds.Passdown.Infrastructure.Updates.Installation;

/// <summary>
/// Phase 23D: the operating-system effects the updater needs — waiting for processes, starting the app,
/// free disk space and time. Separate so the whole install/rollback engine runs against temporary folders in
/// tests, with no real process ever started and the real installation never touched.
/// </summary>
public interface IUpdaterEnvironment
{
    /// <summary>Waits until the process with this id (and start time, guarding against id reuse) has exited.</summary>
    Task<bool> WaitForExitAsync(int processId, DateTimeOffset? startUtc, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Number of other Carowinds Operations processes running from <paramref name="installDirectory"/> (best effort; -1 when unknown).</summary>
    int CountInstancesRunningFrom(string installDirectory);

    /// <summary>Starts the installed app (no shell, no arguments). Null when it could not be started.</summary>
    IStartedApp? StartApp(string executablePath, string workingDirectory);

    long? AvailableFreeSpace(string path);

    /// <summary>The clock every updater deadline is measured against.</summary>
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>A process the updater started itself — the only process it may ever stop.</summary>
public interface IStartedApp : IDisposable
{
    bool HasExited { get; }

    void Kill();

    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Phase 23D: the real environment (Windows in production).</summary>
public sealed class SystemUpdaterEnvironment : IUpdaterEnvironment
{
    public const string AppProcessName = "Carowinds.Passdown.Desktop";

    public async Task<bool> WaitForExitAsync(int processId, DateTimeOffset? startUtc, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return true; // Not running.
        }

        using (process)
        {
            try
            {
                if (startUtc is { } expected && Math.Abs((process.StartTime.ToUniversalTime() - expected.UtcDateTime).TotalSeconds) > 2)
                {
                    return true; // The id now belongs to an unrelated process.
                }

                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(timeout);
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return true;
            }
        }
    }

    public int CountInstancesRunningFrom(string installDirectory)
    {
        var count = 0;
        var unknown = false;
        foreach (var process in Process.GetProcessesByName(AppProcessName))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is not null && UpdatePaths.IsWithin(path, installDirectory))
                    {
                        count++;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Another user's process: its path cannot be read. The file-lock probe still protects its files.
                    unknown = true;
                }
            }
        }

        return count == 0 && unknown ? -1 : count;
    }

    public IStartedApp? StartApp(string executablePath, string workingDirectory)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
            });
            return process is null ? null : new StartedApp(process);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    public long? AvailableFreeSpace(string path) => DiskSpace.AvailableBytes(path);

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);

    private sealed class StartedApp(Process process) : IStartedApp
    {
        public bool HasExited
        {
            get
            {
                try
                {
                    return process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public void Kill()
        {
            try
            {
                process.Kill(entireProcessTree: false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }

        public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        public void Dispose() => process.Dispose();
    }
}
