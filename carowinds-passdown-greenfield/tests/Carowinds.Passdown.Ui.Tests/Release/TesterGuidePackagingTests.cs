namespace Carowinds.Passdown.Ui.Tests.Release;

/// <summary>
/// Phase 7F Part L/V: the nontechnical tester guide must exist and be staged into the release ZIP
/// at the release root (not under <c>docs/</c>, which stays developer-only). Mirrors
/// <see cref="PortableSetupHelperTests"/>'s approach — reads the shipped script/text directly rather
/// than executing PowerShell (no <c>pwsh</c> on this dev host).
/// </summary>
public class TesterGuidePackagingTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string PackagingWindowsDirectory = Path.Combine(RepoRoot, "packaging", "windows");
    private static string TesterGuidePath => Path.Combine(PackagingWindowsDirectory, "TESTER-GUIDE.txt");
    private static string BuildPortableReleaseScriptText => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, "Build-PortableRelease.ps1"));

    [Fact]
    public void TesterGuide_Exists()
    {
        Assert.True(File.Exists(TesterGuidePath));
    }

    [Fact]
    public void TesterGuide_IsStagedAtReleaseRoot_ByBuildPortableReleaseScript()
    {
        Assert.Contains("TESTER-GUIDE.txt", BuildPortableReleaseScriptText);
        Assert.Contains("Copy-Item -Path $testerGuideSource -Destination $stagingReleaseDirectory", BuildPortableReleaseScriptText);
    }

    [Fact]
    public void BuildPortableReleaseScript_FailsIfTesterGuideIsMissing()
    {
        Assert.Contains("Tester guide not found", BuildPortableReleaseScriptText);
    }

    [Fact]
    public void TesterGuide_ContainsNoDeveloperArchitectureTerminology()
    {
        var text = File.ReadAllText(TesterGuidePath);

        Assert.DoesNotContain("RefreshOrchestrator", text);
        Assert.DoesNotContain("IUnitOfWork", text);
        Assert.DoesNotContain("LastKnownGood", text);
    }

    [Fact]
    public void TesterGuide_MentionsExportDiagnostics()
    {
        Assert.Contains("Export Diagnostics", File.ReadAllText(TesterGuidePath));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repo root from test base directory.");
    }
}
