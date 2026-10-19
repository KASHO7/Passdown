namespace Carowinds.Passdown.Ui.Tests.Release;

/// <summary>
/// Phase 7D Part D/T (#10): static content contracts proving none of the portable-release
/// packaging/setup scripts ever delete or otherwise touch
/// <c>%LOCALAPPDATA%\Carowinds Passdown</c> — the "delete the portable folder != delete user
/// data" invariant. Read directly from the shipped script text, same approach as
/// <see cref="PortableSetupHelperTests"/>, rather than executing PowerShell (no <c>pwsh</c> on
/// this dev host).
/// </summary>
public class PortablePackagingDataSafetyTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string PackagingWindowsDirectory = Path.Combine(RepoRoot, "packaging", "windows");

    private static string PublishWindowsScriptText => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, "Publish-Windows.ps1"));
    private static string BuildPortableReleaseScriptText => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, "Build-PortableRelease.ps1"));
    private static string InstallScriptText => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, "Install-ScheduledRefresh.ps1"));
    private static string UninstallScriptText => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, "Uninstall-ScheduledRefresh.ps1"));
    private static string EnableHelperText => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, "Enable Automatic Refresh.cmd"));
    private static string DisableHelperText => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, "Disable Automatic Refresh.cmd"));

    // Publish-Windows.ps1 and Build-PortableRelease.ps1 only ever stage/delete their own
    // artifacts/ build output — neither has any legitimate reason to reference the user-data
    // directory at all.
    [Fact]
    public void PublishAndReleaseBuildScripts_NeverReferenceLocalAppData()
    {
        AssertNoLocalAppDataReference(PublishWindowsScriptText, "Publish-Windows.ps1");
        AssertNoLocalAppDataReference(BuildPortableReleaseScriptText, "Build-PortableRelease.ps1");
    }

    // The scheduled-task install/uninstall scripts are OS Task Scheduler wrappers only — they
    // never delete any file at all, let alone one under LocalAppData.
    [Fact]
    public void InstallAndUninstallScripts_NeverCallAnyFileDeleteCmdlet()
    {
        string[] deleteCmdlets = ["Remove-Item", "rd /s", "del /"];
        foreach (var cmdlet in deleteCmdlets)
        {
            Assert.DoesNotContain(cmdlet, InstallScriptText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(cmdlet, UninstallScriptText, StringComparison.OrdinalIgnoreCase);
        }
    }

    // The double-click helpers only ever invoke the install/uninstall PowerShell scripts and
    // never touch the filesystem themselves beyond checking the app .exe exists.
    [Fact]
    public void EnableAndDisableHelpers_NeverReferenceLocalAppDataOrDeleteFiles()
    {
        AssertNoLocalAppDataReference(EnableHelperText, "Enable Automatic Refresh.cmd");
        AssertNoLocalAppDataReference(DisableHelperText, "Disable Automatic Refresh.cmd");

        Assert.DoesNotContain("del ", EnableHelperText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("del ", DisableHelperText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rd /s", EnableHelperText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rd /s", DisableHelperText, StringComparison.OrdinalIgnoreCase);
    }

    // Every Remove-Item call across the two build scripts targets only this script's own
    // artifacts/ staging output (repo-relative $stagingReleaseDirectory/$zipPath/$OutputDirectory
    // variables built from $repoRoot), never a raw LocalAppData/AppData path.
    [Fact]
    public void BuildScripts_RemoveItemCallsOnlyTargetArtifactsStagingVariables_NeverAppDataPaths()
    {
        AssertRemoveItemCallsTargetOnlyKnownVariables(
            PublishWindowsScriptText,
            "Publish-Windows.ps1",
            ["$OutputDirectory", "$removedPdbs", "$updaterOutput"]);

        // Phase 23D: the updater's temporary publish folder is repo-relative under artifacts/, like every other target.
        Assert.Contains("$updaterOutput = Join-Path $repoRoot 'artifacts' 'publish' 'win-x64-updater'", PublishWindowsScriptText);

        AssertRemoveItemCallsTargetOnlyKnownVariables(
            BuildPortableReleaseScriptText,
            "Build-PortableRelease.ps1",
            ["$stagingReleaseDirectory", "$strayPdbs", "$zipPath"]);
    }

    private static void AssertRemoveItemCallsTargetOnlyKnownVariables(string scriptText, string scriptName, string[] allowedTargetVariables)
    {
        var removeItemLines = scriptText
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("Remove-Item", StringComparison.OrdinalIgnoreCase) && !line.StartsWith('#'));

        foreach (var line in removeItemLines)
        {
            Assert.True(
                allowedTargetVariables.Any(variable => line.Contains(variable, StringComparison.OrdinalIgnoreCase)),
                $"{scriptName} contains a Remove-Item call not targeting a known artifacts/ staging variable: '{line}'");
        }
    }

    private static void AssertNoLocalAppDataReference(string text, string fileName)
    {
        string[] forbiddenFragments = ["LOCALAPPDATA", "LocalApplicationData", "AppData"];
        foreach (var fragment in forbiddenFragments)
        {
            Assert.DoesNotContain(fragment, text, StringComparison.OrdinalIgnoreCase);
        }
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
