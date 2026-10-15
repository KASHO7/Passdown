using System.Diagnostics;
using System.Reflection;

namespace Carowinds.Passdown.Ui.Tests.Release;

/// <summary>
/// Phase 7A Part I/T: guards that the single authoritative version source
/// (Directory.Build.props' &lt;Version&gt;) is actually flowing into the published executable's
/// assembly/file version metadata, rather than every project silently falling back to the .NET
/// SDK's default 1.0.0.0. Deliberately does not assert the exact version number — that would make
/// this test brittle across ordinary version bumps (CLAUDE.md "avoid brittle tests").
/// </summary>
public class ReleaseVersionTests
{
    private static readonly Assembly DesktopAssembly = typeof(Desktop.App).Assembly;

    [Fact]
    public void DesktopAssembly_VersionIsNotTheSdkDefault()
    {
        var version = DesktopAssembly.GetName().Version;

        Assert.NotNull(version);
        Assert.NotEqual(new Version(0, 0, 0, 0), version);
        Assert.NotEqual(new Version(1, 0, 0, 0), version);
    }

    [Fact]
    public void DesktopAssembly_InformationalVersionMarksThisAsAPreReleaseDevelopmentBuild()
    {
        var informationalVersion = DesktopAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.False(string.IsNullOrWhiteSpace(informationalVersion));
        Assert.Contains('-', informationalVersion);
    }

    /// <summary>
    /// Phase 18E: a release candidate's numeric file version (Explorer → Properties → Details) must
    /// carry its RC number, so two candidates never show the same file version. Checks the rule, not a
    /// specific number.
    /// </summary>
    [Fact]
    public void DesktopAssembly_ReleaseCandidateFileVersionCarriesTheCandidateNumber()
    {
        var informationalVersion = DesktopAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        var version = informationalVersion.Split('+')[0];
        var fileVersion = FileVersionInfo.GetVersionInfo(DesktopAssembly.Location).FileVersion;

        var match = System.Text.RegularExpressions.Regex.Match(version, @"^(\d+\.\d+\.\d+)-rc\.(\d+)$");
        if (!match.Success)
        {
            return; // not a release candidate (e.g. a development version) — the rule does not apply
        }

        Assert.Equal($"{match.Groups[1].Value}.{match.Groups[2].Value}", fileVersion);
    }

    [Fact]
    public void DesktopAssembly_ProductNameIsTheUserFacingApplicationName()
    {
        var location = DesktopAssembly.Location;
        Assert.False(string.IsNullOrWhiteSpace(location));

        var productName = FileVersionInfo.GetVersionInfo(location).ProductName;

        Assert.Equal("Carowinds Operations", productName);
    }
}
