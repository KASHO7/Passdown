using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Tests.Updates;

/// <summary>
/// Phase 27: release discovery around the RC.9 candidate, from both sides — the installed RC.8 looking at RC.9, and
/// RC.9 looking at older, equal and later releases. Synthetic GitHub listings only; nothing contacts GitHub.
/// </summary>
public class Phase27ReleaseCompatibilityTests
{
    private static readonly SemanticVersion Rc8 = SemanticVersion.Parse("0.1.0-rc.8");
    private static readonly SemanticVersion Rc9 = SemanticVersion.Parse("0.1.0-rc.9");

    private readonly FakeReleaseSource _source = new();
    private readonly FakeManifestVerifier _verifier = new();

    private UpdateDiscoveryService Service() => new(_source, _verifier, UpdateFixtures.Configuration());

    [Fact]
    public async Task Rc8_SeesRc9AsNewer()
    {
        _source.Releases.AddRange([FakeReleaseSource.Release("0.1.0-rc.8"), FakeReleaseSource.Release("0.1.0-rc.9")]);

        var result = await Service().CheckAsync(Rc8);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("0.1.0-rc.9", result.Offer!.Version.Normalized);
        Assert.Equal("Carowinds-Operations-0.1.0-rc.9-win-x64.zip", result.Offer.Manifest.Package.FileName);
    }

    [Fact]
    public async Task Rc9_IsNeverOfferedRc8OrItself()
    {
        _source.Releases.AddRange([FakeReleaseSource.Release("0.1.0-rc.7"), FakeReleaseSource.Release("0.1.0-rc.8"), FakeReleaseSource.Release("0.1.0-rc.9")]);

        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(Rc9)).Status);
        Assert.Empty(_source.DownloadedAssets);
    }

    [Fact]
    public async Task Rc9_IsOfferedRc10_NotOrderedAsText()
    {
        _source.Releases.AddRange([FakeReleaseSource.Release("0.1.0-rc.9"), FakeReleaseSource.Release("0.1.0-rc.10")]);

        Assert.Equal("0.1.0-rc.10", (await Service().CheckAsync(Rc9)).Offer!.Version.Normalized);
    }

    [Fact]
    public async Task Rc8_IgnoresADraftRc9()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.9", draft: true));

        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(Rc8)).Status);
    }

    [Theory]
    [InlineData(false, true)] // tag published without the signed manifest
    [InlineData(true, false)] // manifest without its signature
    public async Task Rc8_IgnoresAnRc9TagWithoutTheSignedAssets(bool withManifest, bool withSignature)
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.9", withManifest: withManifest, withSignature: withSignature));

        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(Rc8)).Status);
    }

    [Fact]
    public async Task Rc8_RejectsAnRc9WhoseSignatureDoesNotVerify()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.9"));
        _verifier.Invalid.Add("0.1.0-rc.9");

        Assert.NotEqual(UpdateCheckStatus.UpdateAvailable, (await Service().CheckAsync(Rc8)).Status);
    }
}
