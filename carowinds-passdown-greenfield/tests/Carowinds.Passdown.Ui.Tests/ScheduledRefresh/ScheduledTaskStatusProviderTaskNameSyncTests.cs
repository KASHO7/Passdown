using Carowinds.Passdown.Desktop.ScheduledRefresh;
using Carowinds.Passdown.Infrastructure.Diagnostics;

namespace Carowinds.Passdown.Ui.Tests.ScheduledRefresh;

/// <summary>
/// <see cref="ScheduledTaskStatusProvider.TaskName"/> is a hand-synced copy of
/// <see cref="ScheduledTaskPlan.TaskName"/> — Infrastructure cannot reference the Desktop project
/// (CLAUDE.md architecture: dependencies only flow Desktop → Infrastructure), so this Ui.Tests
/// project (which references Desktop, and transitively Infrastructure) is the one place both
/// constants are reachable together.
/// </summary>
public class ScheduledTaskStatusProviderTaskNameSyncTests
{
    [Fact]
    public void TaskName_MatchesScheduledTaskPlanTaskName() =>
        Assert.Equal(ScheduledTaskPlan.TaskName, ScheduledTaskStatusProvider.TaskName);
}
