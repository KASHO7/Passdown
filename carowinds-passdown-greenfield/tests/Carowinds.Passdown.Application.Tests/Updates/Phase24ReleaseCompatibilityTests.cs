using System.Reflection;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Tests.Updates;

/// <summary>
/// Phase 24: the RC.8 candidate (the first updater-enabled build) must never treat its own release, or an
/// older one, as an update, and must be offered the next release candidate. Synthetic GitHub listings only.
/// </summary>
public class Phase24ReleaseCompatibilityTests
{
    private readonly FakeReleaseSource _source = new();
    private readonly FakeManifestVerifier _verifier = new();

    private UpdateDiscoveryService Service() => new(_source, _verifier, UpdateFixtures.Configuration());

    [Fact]
    public async Task InstalledRc8_IgnoresRc7AndRc8_AndIsOfferedRc9()
    {
        var installed = SemanticVersion.Parse("0.1.0-rc.8");
        _source.Releases.AddRange([FakeReleaseSource.Release("0.1.0-rc.7"), FakeReleaseSource.Release("0.1.0-rc.8")]);
        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(installed)).Status);
        Assert.Empty(_source.DownloadedAssets);

        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.9"));
        var result = await Service().CheckAsync(installed);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("0.1.0-rc.9", result.Offer!.Version.Normalized);
    }

    [Fact]
    public async Task BuiltVersion_IsNeverOfferedItself_ButIsOfferedTheNextCandidate()
    {
        var informational = typeof(UpdateDiscoveryService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var built = SemanticVersion.Parse(informational.Split('+')[0]);
        Assert.True(built.IsPrerelease && built.PrereleaseIdentifiers is ["rc", _], $"Expected an rc.N build, got {built}.");
        var next = $"{built.Major}.{built.Minor}.{built.Patch}-rc.{int.Parse(built.PrereleaseIdentifiers[1]) + 1}";

        _source.Releases.Add(FakeReleaseSource.Release(built.Normalized));
        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(built)).Status);

        _source.Releases.Add(FakeReleaseSource.Release(next));
        Assert.Equal(next, (await Service().CheckAsync(built)).Offer!.Version.Normalized);
    }
}
