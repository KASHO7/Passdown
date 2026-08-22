using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Infrastructure.Diagnostics;

namespace Carowinds.Passdown.Infrastructure.Tests.Diagnostics;

/// <summary>
/// Phase 7F Part K: this dev host is never Windows (CLAUDE.md "Development host: Fedora"), so only
/// the non-Windows branch is exercised here — mirrors the existing platform-gated pattern in
/// <c>FileLoggerProviderTests</c>. The Windows branch itself (shelling to <c>schtasks.exe</c>) is
/// covered by manual Windows validation, not by an automated test on this host.
/// </summary>
public class ScheduledTaskStatusProviderTests
{
    [Fact]
    public async Task GetStatusAsync_OnNonWindowsHost_ReportsNotAvailableWithoutThrowing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await new ScheduledTaskStatusProvider().GetStatusAsync();

        Assert.Equal(ScheduledTaskDiagnosticStatus.NotAvailableOnThisPlatform, result.Status);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public void TaskName_IsTheDocumentedScheduledTaskName()
    {
        Assert.Equal("Carowinds Passdown Daily Refresh", ScheduledTaskStatusProvider.TaskName);
    }
}
