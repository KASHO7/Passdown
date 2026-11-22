using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Ui.Tests.Release;

/// <summary>
/// Phase 32 local-test candidate: Build-PortableRelease.ps1 -LocalTestVersion stamps only a LOCAL-ONLY test
/// build's displayed version. The release path never passes it, and the documented form sorts above every
/// published release, so the in-app updater can never offer to install a release over a test build.
/// </summary>
public class LocalTestVersionStampTests
{
    private static readonly string PackagingWindowsDirectory = Path.Combine(FindRepoRoot(), "packaging", "windows");

    private static string Script(string name) => File.ReadAllText(Path.Combine(PackagingWindowsDirectory, name));

    [Fact]
    public void LocalTestVersion_IsRefusedWithoutALocalTestLabel_AndMustSayLocalTest()
    {
        var build = Script("Build-PortableRelease.ps1");

        Assert.Contains("-LocalTestVersion can only be used together with -LocalTestLabel.", build, StringComparison.Ordinal);
        Assert.Contains("$LocalTestVersion -notmatch 'LOCALTEST'", build, StringComparison.Ordinal);
        Assert.Contains("-LocalTestVersion must differ from the source version", build, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasePath_NeverPassesADisplayedVersion()
    {
        var build = Script("Build-PortableRelease.ps1");

        Assert.Contains("& $publishScript -OutputDirectory $publishDirectory -InformationalVersion $LocalTestVersion", build, StringComparison.Ordinal);
        Assert.Contains("        & $publishScript\n", build.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal); // the release call is unchanged
        Assert.Contains("if ($isLocalTest -and $hasLocalTestVersion)", build, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishScript_StampsOnlyWhenAsked()
    {
        var publish = Script("Publish-Windows.ps1");

        Assert.Contains("if (-not [string]::IsNullOrWhiteSpace($InformationalVersion))", publish, StringComparison.Ordinal);
        Assert.Contains("-p:InformationalVersion=$InformationalVersion", publish, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.1.0-rc.11")]
    [InlineData("0.1.0-rc.12")]
    [InlineData("0.1.0-rc.12.1")]
    public void DocumentedLocalTestVersion_SortsAbovePublishedAndNextReleases(string release)
    {
        Assert.True(SemanticVersion.TryParse("0.1.0-rc.12-LOCALTEST.phase32", out var localTest));
        Assert.True(localTest > SemanticVersion.Parse(release));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
