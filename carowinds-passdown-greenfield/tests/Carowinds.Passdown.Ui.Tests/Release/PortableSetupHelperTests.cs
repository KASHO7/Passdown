using Carowinds.Passdown.Desktop.ScheduledRefresh;

namespace Carowinds.Passdown.Ui.Tests.Release;

/// <summary>
/// Phase 7C Part P: static content contracts for the nontechnical double-click setup helpers
/// (<c>packaging/windows/*.cmd</c>) and their integration into <c>Build-PortableRelease.ps1</c>.
/// These run on any platform without invoking PowerShell (CLAUDE.md "dev host Fedora"; no
/// <c>pwsh</c> is available on this dev host — see docs/release-strategy.md Phase 7C) by reading
/// the shipped script text directly rather than executing it. Real Windows execution of the
/// helpers stays deferred to Phase 7E, exactly as Phase 7A/7B deferred it for the scripts these
/// helpers wrap.
/// </summary>
public class PortableSetupHelperTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string PackagingWindowsDirectory = Path.Combine(RepoRoot, "packaging", "windows");

    private static string EnableHelperPath => Path.Combine(PackagingWindowsDirectory, "Enable Automatic Refresh.cmd");
    private static string DisableHelperPath => Path.Combine(PackagingWindowsDirectory, "Disable Automatic Refresh.cmd");

    private static string EnableHelperText => File.ReadAllText(EnableHelperPath);
    private static string DisableHelperText => File.ReadAllText(DisableHelperPath);
    private static string BuildPortableReleaseScriptText => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, "Build-PortableRelease.ps1"));
    private static string InstallScriptText => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, "Install-ScheduledRefresh.ps1"));
    private static string UninstallScriptText => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, "Uninstall-ScheduledRefresh.ps1"));

    private static string HelperText(string which) => which == "Enable" ? EnableHelperText : DisableHelperText;

    // 1/10. Enable helper exists and Build-PortableRelease.ps1 stages it at the release root (not tools\, so a
    // nontechnical user finds it immediately next to the .exe).
    [Fact]
    public void EnableHelper_ExistsAndIsStagedAtReleaseRoot()
    {
        Assert.True(File.Exists(EnableHelperPath));
        Assert.Contains("Enable Automatic Refresh.cmd", BuildPortableReleaseScriptText);
        Assert.Contains("Copy-Item -Path $enableHelperSource -Destination $stagingReleaseDirectory", BuildPortableReleaseScriptText);
    }

    // 2/10. Disable helper exists and is staged the same way.
    [Fact]
    public void DisableHelper_ExistsAndIsStagedAtReleaseRoot()
    {
        Assert.True(File.Exists(DisableHelperPath));
        Assert.Contains("Disable Automatic Refresh.cmd", BuildPortableReleaseScriptText);
        Assert.Contains("Copy-Item -Path $disableHelperSource -Destination $stagingReleaseDirectory", BuildPortableReleaseScriptText);
    }

    // Build-PortableRelease.ps1 fails loudly instead of silently shipping a ZIP without a helper.
    [Fact]
    public void BuildPortableReleaseScript_FailsIfEitherHelperIsMissing()
    {
        Assert.Contains("Enable Automatic Refresh.cmd not found", BuildPortableReleaseScriptText);
        Assert.Contains("Disable Automatic Refresh.cmd not found", BuildPortableReleaseScriptText);
    }

    // 3. Helpers resolve their own directory via %~dp0 rather than any absolute literal path.
    [Theory]
    [InlineData("Enable")]
    [InlineData("Disable")]
    public void Helper_ResolvesOwnDirectoryViaDp0(string which)
    {
        Assert.Contains("%~dp0", HelperText(which));
    }

    // 3/8. Enable helper locates the app executable relative to itself and verifies it exists
    // before doing anything else — the resolved absolute path is what gets passed to the existing
    // registration logic, never a path the user types.
    [Fact]
    public void EnableHelper_ResolvesAndVerifiesAppExecutableRelativeToItself()
    {
        Assert.Contains("APP_EXE=%APP_DIR%Carowinds.Passdown.Desktop.exe", EnableHelperText);
        Assert.Contains("if not exist \"%APP_EXE%\"", EnableHelperText);
    }

    // 4. No developer-specific path from this dev environment leaks into a shipped helper.
    [Theory]
    [InlineData("Enable")]
    [InlineData("Disable")]
    public void Helper_ContainsNoDeveloperSpecificPath(string which)
    {
        var text = HelperText(which);
        string[] forbiddenFragments =
        [
            "/Users/", "/home/", "carowinds-passdown-greenfield", "C:\\Users\\",
            "/private/tmp", "/mnt/",
        ];

        foreach (var fragment in forbiddenFragments)
        {
            Assert.DoesNotContain(fragment, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // 5. Execution-policy bypass is scoped to the one PowerShell process this line launches —
    // never a persistent Set-ExecutionPolicy call against LocalMachine/CurrentUser.
    [Theory]
    [InlineData("Enable")]
    [InlineData("Disable")]
    public void Helper_UsesProcessScopedExecutionPolicyBypassOnly(string which)
    {
        var text = HelperText(which);
        Assert.Contains("-ExecutionPolicy Bypass", text);
        Assert.DoesNotContain("Set-ExecutionPolicy", text, StringComparison.OrdinalIgnoreCase);
    }

    // No admin/elevation is ever requested by either helper.
    [Theory]
    [InlineData("Enable")]
    [InlineData("Disable")]
    public void Helper_NeverRequestsElevation(string which)
    {
        var text = HelperText(which);
        Assert.DoesNotContain("runas", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RunLevel Highest", text, StringComparison.OrdinalIgnoreCase);
    }

    // 6. The task name flowing through the whole chain (C# plan, both PowerShell scripts) stays
    // exactly one value — guards against silent hand-sync drift the scripts' own comments flag.
    [Fact]
    public void TaskName_MatchesAcrossPlanAndBothPowerShellScripts()
    {
        var expected = $"'{ScheduledTaskPlan.TaskName}'";
        Assert.Contains(expected, InstallScriptText);
        Assert.Contains(expected, UninstallScriptText);
    }

    // 7. The fixed --scheduled-refresh argument used by the scheduled task matches the same C#
    // constant Program.cs parses.
    [Fact]
    public void ScheduledRefreshArgument_MatchesInstallScript()
    {
        Assert.Contains($"'{CommandLineArguments.ScheduledRefreshFlag}'", InstallScriptText);
    }

    // 9. Disable helper invokes Uninstall-ScheduledRefresh.ps1 with no extra or user-suppliable
    // arguments — that script takes no parameters and only ever targets the one fixed task name,
    // so this helper cannot be used to remove any other scheduled task.
    [Fact]
    public void DisableHelper_InvokesUninstallScriptWithNoExtraArguments()
    {
        var invocationLine = FindPowerShellInvocationLine(DisableHelperText);

        Assert.Contains("-File \"%UNINSTALL_SCRIPT%\"", invocationLine);
        Assert.DoesNotContain("-TaskName", invocationLine, StringComparison.OrdinalIgnoreCase);
    }

    // Enable helper passes only the resolved executable path argument — nothing user-suppliable.
    [Fact]
    public void EnableHelper_InvokesInstallScriptWithOnlyExecutablePathArgument()
    {
        var invocationLine = FindPowerShellInvocationLine(EnableHelperText);

        Assert.Contains("-File \"%INSTALL_SCRIPT%\" -ExecutablePath \"%APP_EXE%\"", invocationLine);
    }

    // 11. This phase's release-packaging changes introduce no Manager workflow content.
    [Fact]
    public void BuildPortableReleaseScript_MentionsNoManagerWorkflow()
    {
        Assert.DoesNotContain("Manager", BuildPortableReleaseScriptText, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindPowerShellInvocationLine(string helperText)
    {
        var line = helperText
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("powershell.exe", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(line);
        return line!;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repo root (CarowindsPassdown.slnx) from '{AppContext.BaseDirectory}'.");
    }
}
