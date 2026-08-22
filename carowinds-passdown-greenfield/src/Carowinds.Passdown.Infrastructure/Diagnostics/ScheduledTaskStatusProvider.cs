using System.Diagnostics;
using Carowinds.Passdown.Application.Diagnostics;

namespace Carowinds.Passdown.Infrastructure.Diagnostics;

/// <summary>
/// Reports whether the "Carowinds Passdown Daily Refresh" Windows Task Scheduler task is currently
/// registered (Phase 7F Part K), by shelling out to <c>schtasks.exe /Query</c> and checking its exit
/// code only — deliberately not parsing <c>schtasks</c>' detailed, locale-dependent text output for
/// last-run/next-run detail (Part K explicitly marks a full status helper optional; existence is
/// enough for a diagnostic export, and full detail remains available via Task Scheduler's own UI or
/// <c>Get-ScheduledTaskInfo</c>, documented in the tester/support guides). Never throws: an
/// unavailable platform, a missing <c>schtasks.exe</c>, or any query failure is reported as a status
/// value, never an exception that would fail the whole diagnostic export.
/// </summary>
public sealed class ScheduledTaskStatusProvider : IScheduledTaskStatusProvider
{
    /// <summary>
    /// Kept in sync by hand with <c>Carowinds.Passdown.Desktop.ScheduledRefresh.ScheduledTaskPlan.TaskName</c>.
    /// Infrastructure cannot reference the Desktop project (CLAUDE.md architecture: dependencies
    /// only flow Desktop → Infrastructure, never the reverse) — this is the same hand-sync approach
    /// already used by <c>packaging/windows/Install-ScheduledRefresh.ps1</c>/<c>Uninstall-ScheduledRefresh.ps1</c>.
    /// <c>ScheduledTaskStatusProviderTaskNameSyncTests</c> (Ui.Tests, which references both) asserts
    /// these stay equal.
    /// </summary>
    public const string TaskName = "Carowinds Passdown Daily Refresh";

    public async Task<ScheduledTaskDiagnostics> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.NotAvailableOnThisPlatform, "Task Scheduler is a Windows-only feature.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/Query");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add(TaskName);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.Unknown, "Could not start schtasks.exe.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return process.ExitCode == 0
                ? new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.Enabled, null)
                : new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.NotFound, $"No scheduled task named \"{TaskName}\" was found.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.Unknown, "Could not query Task Scheduler status.");
        }
    }
}
