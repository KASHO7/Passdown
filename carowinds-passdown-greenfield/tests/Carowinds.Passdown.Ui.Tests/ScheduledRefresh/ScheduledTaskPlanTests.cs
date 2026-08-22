using Carowinds.Passdown.Desktop.ScheduledRefresh;

namespace Carowinds.Passdown.Ui.Tests.ScheduledRefresh;

/// <summary>Phase 6C Part Q: the cross-platform-testable half of the Windows Task Scheduler integration — see ScheduledTaskPlan.</summary>
public class ScheduledTaskPlanTests
{
    // 1. Expected scheduled task name is stable.
    [Fact]
    public void TaskName_IsStable()
    {
        Assert.Equal("Carowinds Passdown Daily Refresh", ScheduledTaskPlan.TaskName);
    }

    // 3. Action arguments are exactly --scheduled-refresh (same constant Program.cs parses).
    [Fact]
    public void Build_ArgumentsMatchScheduledRefreshFlag()
    {
        var definition = ScheduledTaskPlan.Build(@"C:\Program Files\Carowinds Passdown\Carowinds.Passdown.Desktop.exe");

        Assert.Equal(CommandLineArguments.ScheduledRefreshFlag, definition.Arguments);
    }

    // 4. No normal UI argument/path is accidentally registered: the only argument ever produced is the scheduled-refresh flag.
    [Fact]
    public void Build_NeverProducesEmptyOrNormalModeArguments()
    {
        var definition = ScheduledTaskPlan.Build(@"C:\Apps\Carowinds.Passdown.Desktop.exe");

        Assert.False(string.IsNullOrWhiteSpace(definition.Arguments));
        Assert.Equal("--scheduled-refresh", definition.Arguments);
    }

    // 7. Executable path with spaces is handled (quoted in the display command line).
    [Fact]
    public void QuotedCommandLine_PathWithSpaces_IsQuoted()
    {
        var definition = ScheduledTaskPlan.Build(@"C:\Program Files\Carowinds Passdown\Carowinds.Passdown.Desktop.exe");

        Assert.Equal(
            "\"C:\\Program Files\\Carowinds Passdown\\Carowinds.Passdown.Desktop.exe\" --scheduled-refresh",
            definition.QuotedCommandLine);
    }

    [Fact]
    public void QuotedCommandLine_PathWithoutSpaces_IsNotQuoted()
    {
        var definition = ScheduledTaskPlan.Build(@"C:\Apps\Carowinds.Passdown.Desktop.exe");

        Assert.Equal("C:\\Apps\\Carowinds.Passdown.Desktop.exe --scheduled-refresh", definition.QuotedCommandLine);
    }

    // 2 / 8. Action executable path is handled safely; missing/blank executable path fails clearly.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_NullOrBlankPath_ThrowsArgumentException(string? path)
    {
        Assert.Throws<ArgumentException>(() => ScheduledTaskPlan.Build(path!));
    }

    // Fragile relative paths are rejected outright rather than registered (Part F "avoid fragile relative paths").
    [Theory]
    [InlineData(@"Carowinds.Passdown.Desktop.exe")]
    [InlineData(@"..\Carowinds.Passdown.Desktop.exe")]
    [InlineData(@".\bin\Carowinds.Passdown.Desktop.exe")]
    public void Build_RelativePath_ThrowsArgumentException(string relativePath)
    {
        Assert.Throws<ArgumentException>(() => ScheduledTaskPlan.Build(relativePath));
    }

    // Part G: working directory matches the executable's own directory (Windows-style path, tested from a non-Windows dev host).
    [Fact]
    public void Build_WindowsDriveRootedPath_SetsWorkingDirectoryToExecutableDirectory()
    {
        var definition = ScheduledTaskPlan.Build(@"C:\Program Files\Carowinds Passdown\Carowinds.Passdown.Desktop.exe");

        Assert.Equal(@"C:\Program Files\Carowinds Passdown", definition.WorkingDirectory);
    }

    // A UNC path is also a valid absolute path.
    [Fact]
    public void Build_UncRootedPath_Succeeds()
    {
        var definition = ScheduledTaskPlan.Build(@"\\FileServer\Apps\Carowinds.Passdown.Desktop.exe");

        Assert.Equal(@"\\FileServer\Apps", definition.WorkingDirectory);
    }

    // 9. No user-specific hard-coded path exists anywhere in the plan — TaskName is path-independent, and Build requires an explicit caller-supplied path every time.
    [Fact]
    public void TaskName_ContainsNoPathSeparatorOrUserReference()
    {
        Assert.DoesNotContain('\\', ScheduledTaskPlan.TaskName);
        Assert.DoesNotContain('/', ScheduledTaskPlan.TaskName);
    }

    [Fact]
    public void Build_SamePathTwice_ProducesIdenticalDefinition()
    {
        const string path = @"C:\Apps\Carowinds.Passdown.Desktop.exe";

        var first = ScheduledTaskPlan.Build(path);
        var second = ScheduledTaskPlan.Build(path);

        Assert.Equal(first, second);
    }

    // Phase 7D Part M/N: rebinding after moving the portable folder (re-running
    // "Enable Automatic Refresh.cmd" from the new location) must always target the exact same
    // task name regardless of which path it was last built for — that stable identity is what
    // lets Install-ScheduledRefresh.ps1's Register-ScheduledTask -Force replace the one existing
    // task in place rather than ever registering a second, duplicate task at the new path.
    [Fact]
    public void Build_DifferentExecutablePaths_ShareTheSameTaskName_ButUpdateTheExecutableAndWorkingDirectory()
    {
        var oldLocation = ScheduledTaskPlan.Build(@"C:\Users\User\Downloads\Carowinds Passdown\Carowinds.Passdown.Desktop.exe");
        var newLocation = ScheduledTaskPlan.Build(@"C:\Users\User\Desktop\Operations\Carowinds Passdown\Carowinds.Passdown.Desktop.exe");

        Assert.Equal(oldLocation.TaskName, newLocation.TaskName);
        Assert.NotEqual(oldLocation.ExecutablePath, newLocation.ExecutablePath);
        Assert.NotEqual(oldLocation.WorkingDirectory, newLocation.WorkingDirectory);
        Assert.Equal(oldLocation.Arguments, newLocation.Arguments);
    }
}
