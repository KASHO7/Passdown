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

    // Phase 29A finding F2: with <Version> still at a shipped RC, the release build would
    // Remove-Item -Force that RC's frozen staging folder and ZIP. The guard must refuse when the
    // ZIP already exists, and it must run before the publish step and before any delete.
    [Fact]
    public void ReleaseBuild_RefusesToOverwriteAnExistingReleaseZip_BeforePublishingOrDeleting()
    {
        var script = BuildPortableReleaseScriptText;

        Assert.Contains("$frozenZipPath = Join-Path $releasesDirectory \"$releaseName.zip\"", script);
        Assert.Contains("if (Test-Path -LiteralPath $frozenZipPath) {", script);
        var guard = script.IndexOf("Refusing to overwrite a built release", StringComparison.Ordinal);
        Assert.True(guard > 0);
        Assert.True(guard < script.IndexOf("& $publishScript", StringComparison.Ordinal), "guard must precede the publish step");
        Assert.True(guard < script.IndexOf("Remove-Item -LiteralPath", StringComparison.Ordinal), "guard must precede every Remove-Item");
        Assert.True(guard < script.IndexOf("| Remove-Item", StringComparison.Ordinal), "guard must precede every Remove-Item");
    }

    // A LOCAL-ONLY test build writes only under artifacts/local-test/<label>/ (publish, staging and
    // ZIP), so artifacts/releases, artifacts/release-staging and artifacts/publish/win-x64 are untouched.
    [Fact]
    public void LocalTestBuild_KeepsPublishStagingAndZipUnderArtifactsLocalTest()
    {
        var script = BuildPortableReleaseScriptText;

        Assert.Contains("[string]$LocalTestLabel", script);
        Assert.Contains("$localTestRoot = Join-Path $repoRoot 'artifacts' 'local-test' $LocalTestLabel", script);
        Assert.Contains("$releaseName = \"LOCAL-ONLY-$LocalTestLabel-win-x64\"", script);
        Assert.Contains("$publishDirectory = Join-Path $localTestRoot 'publish'", script);
        Assert.Contains("$stagingRoot = Join-Path $localTestRoot 'staging'", script);
        Assert.Contains("$releasesDirectory = $localTestRoot", script);
        Assert.Contains("& $publishScript -OutputDirectory $publishDirectory", script);
        Assert.Contains("'LOCAL-ONLY-TEST-BUILD.txt'", script);

        // The release-staging root is chosen only on the non-test branch.
        var releaseStaging = script.IndexOf("$stagingRoot = Join-Path $repoRoot 'artifacts' 'release-staging'", StringComparison.Ordinal);
        Assert.True(releaseStaging > script.IndexOf("$releasesDirectory = $localTestRoot", StringComparison.Ordinal));
        Assert.Equal(script.IndexOf("'release-staging'", StringComparison.Ordinal), script.LastIndexOf("'release-staging'", StringComparison.Ordinal));

        // The label cannot smuggle a path (no separators or '..' as a whole label).
        Assert.Contains("$LocalTestLabel -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$'", script);
        Assert.Contains("-SkipPublish cannot be combined with -LocalTestLabel", script);
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
