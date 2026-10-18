using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Tests.Updates;

/// <summary>Phase 23D: approved-release discovery against synthetic GitHub listings.</summary>
public class UpdateDiscoveryServiceTests
{
    private static readonly SemanticVersion Installed = SemanticVersion.Parse("0.1.0-rc.7");

    private readonly FakeReleaseSource _source = new();
    private readonly FakeManifestVerifier _verifier = new();

    private UpdateDiscoveryService Service(UpdateChannel channel = UpdateChannel.ReleaseCandidate) =>
        new(_source, _verifier, UpdateFixtures.Configuration(channel));

    [Fact]
    public async Task NoReleases_IsUpToDate()
    {
        var result = await Service().CheckAsync(Installed);
        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.Offer);
    }

    [Fact]
    public async Task NewerApprovedRelease_IsOffered()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8"));
        var result = await Service().CheckAsync(Installed);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("0.1.0-rc.8", result.Offer!.Version.Normalized);
        Assert.Equal(1000, result.Offer.DownloadBytes);
    }

    [Theory]
    [InlineData("0.1.0-rc.6")]
    [InlineData("0.1.0-rc.7")]
    public async Task OlderOrSameVersion_IsNeverOffered(string version)
    {
        _source.Releases.Add(FakeReleaseSource.Release(version));
        var result = await Service().CheckAsync(Installed);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Empty(_source.DownloadedAssets);
    }

    [Fact]
    public async Task MultipleReleaseCandidates_NewestBySemVerWins_NotAlphabetical()
    {
        _source.Releases.AddRange([FakeReleaseSource.Release("0.1.0-rc.9"), FakeReleaseSource.Release("0.1.0-rc.10"), FakeReleaseSource.Release("0.1.0-rc.8")]);
        var result = await Service().CheckAsync(Installed);
        Assert.Equal("0.1.0-rc.10", result.Offer!.Version.Normalized);
    }

    [Fact]
    public async Task ReleaseCandidateChannel_AcceptsStable_StableChannelIgnoresPrereleases()
    {
        _source.Releases.AddRange([FakeReleaseSource.Release("0.1.0-rc.9"), FakeReleaseSource.Release("0.1.0")]);
        Assert.Equal("0.1.0", (await Service().CheckAsync(Installed)).Offer!.Version.Normalized);

        _source.Releases.Clear();
        _source.Releases.Add(FakeReleaseSource.Release("0.2.0-rc.1"));
        var stable = await Service(UpdateChannel.Stable).CheckAsync(SemanticVersion.Parse("0.1.0"));
        Assert.Equal(UpdateCheckStatus.UpToDate, stable.Status);
    }

    [Fact]
    public async Task Draft_IsIgnored()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8", draft: true));
        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(Installed)).Status);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task MissingRequiredAsset_IsIgnored(bool package, bool manifest, bool signature)
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8", withPackage: package, withManifest: manifest, withSignature: signature));
        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(Installed)).Status);
    }

    [Fact]
    public async Task TagThatDoesNotMatchTheVersionExactly_IsIgnored()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8", tag: "release-0.1.0-rc.8"));
        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(Installed)).Status);
    }

    [Fact]
    public async Task UnsignedOrInvalidlySignedRelease_IsNotApproved_AndOlderValidOneIsOffered()
    {
        _source.Releases.AddRange([FakeReleaseSource.Release("0.1.0-rc.9"), FakeReleaseSource.Release("0.1.0-rc.8")]);
        _verifier.Invalid.Add("0.1.0-rc.9");

        var result = await Service().CheckAsync(Installed);
        Assert.Equal("0.1.0-rc.8", result.Offer!.Version.Normalized);
    }

    [Fact]
    public async Task OnlyInvalidReleases_ReportsUpToDate_WithRejectionCount_NeverAvailable()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8"));
        _verifier.Invalid.Add("0.1.0-rc.8");

        var result = await Service().CheckAsync(Installed);
        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Equal(1, result.RejectedReleaseCount);
    }

    [Fact]
    public async Task ReplayedManifestForAnotherVersion_IsRejected()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.9"));
        _verifier.Tamper["0.1.0-rc.9"] = _ => UpdateFixtures.Manifest("0.1.0-rc.8");
        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(Installed)).Status);
    }

    [Fact]
    public async Task PackageSizeDifferentFromSignedManifest_IsRejected()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8", packageSize: 999));
        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(Installed)).Status);
    }

    [Fact]
    public async Task ManifestNamingAnotherPackage_IsRejected()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8"));
        _verifier.Tamper["0.1.0-rc.8"] = m => m with { Package = m.Package with { FileName = "other.zip" } };
        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service().CheckAsync(Installed)).Status);
    }

    [Theory]
    [InlineData(UpdateSourceFailureKind.Offline)]
    [InlineData(UpdateSourceFailureKind.Timeout)]
    [InlineData(UpdateSourceFailureKind.HttpError)]
    [InlineData(UpdateSourceFailureKind.InvalidResponse)]
    public async Task SourceFailures_AreReportedAsFailed_NeverAsAvailable(UpdateSourceFailureKind kind)
    {
        _source.ListFailure = new UpdateSourceException(kind, "synthetic");
        var result = await Service().CheckAsync(Installed);
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(kind, result.FailureKind);
    }

    [Fact]
    public async Task RateLimit_CarriesRetryTime()
    {
        var reset = new DateTimeOffset(2026, 10, 18, 13, 0, 0, TimeSpan.Zero);
        _source.ListFailure = new UpdateSourceException(UpdateSourceFailureKind.RateLimited, "synthetic", reset);
        var result = await Service().CheckAsync(Installed);
        Assert.Equal(UpdateSourceFailureKind.RateLimited, result.FailureKind);
        Assert.Equal(reset, result.RetryAfterUtc);
    }

    [Fact]
    public async Task MetadataDownloadFailure_IsFailed()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8"));
        _source.AssetFailure = new UpdateSourceException(UpdateSourceFailureKind.Timeout, "synthetic");
        Assert.Equal(UpdateCheckStatus.Failed, (await Service().CheckAsync(Installed)).Status);
    }

    [Fact]
    public async Task NoTrustedKeys_NeverContactsTheSource()
    {
        _verifier.HasTrustedKeys = false;
        var result = await Service().CheckAsync(Installed);
        Assert.Equal(UpdateCheckStatus.NotConfigured, result.Status);
        Assert.Equal(0, _source.ListCalls);

        var unconfigured = new UpdateDiscoveryService(_source, new FakeManifestVerifier(), UpdateConfiguration.Disabled);
        Assert.Equal(UpdateCheckStatus.NotConfigured, (await unconfigured.CheckAsync(Installed)).Status);
        Assert.Equal(0, _source.ListCalls);
    }

    [Fact]
    public async Task AtMostThreeManifestsAreDownloadedPerCheck()
    {
        foreach (var n in Enumerable.Range(8, 6))
        {
            _source.Releases.Add(FakeReleaseSource.Release($"0.1.0-rc.{n}"));
            _verifier.Invalid.Add($"0.1.0-rc.{n}");
        }

        await Service().CheckAsync(Installed);
        Assert.Equal(UpdateDiscoveryService.MaximumManifestsChecked * 2, _source.DownloadedAssets.Count);
    }
}
