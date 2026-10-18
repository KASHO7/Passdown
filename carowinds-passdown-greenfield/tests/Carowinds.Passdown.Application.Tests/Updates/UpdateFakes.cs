using System.Text;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Tests.Updates;

/// <summary>Phase 23D: synthetic release listings and a verifier whose decisions the test controls. No network.</summary>
internal sealed class FakeReleaseSource : IUpdateReleaseSource
{
    public List<ReleaseCandidate> Releases { get; } = [];

    public UpdateSourceException? ListFailure { get; set; }

    public UpdateSourceException? AssetFailure { get; set; }

    public int ListCalls { get; private set; }

    public List<string> DownloadedAssets { get; } = [];

    public TaskCompletionSource? ListGate { get; set; }

    public async Task<IReadOnlyList<ReleaseCandidate>> ListReleasesAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        if (ListGate is not null)
        {
            await ListGate.Task.WaitAsync(cancellationToken);
        }

        if (ListFailure is not null)
        {
            throw ListFailure;
        }

        return Releases;
    }

    public Task<byte[]> DownloadSmallAssetAsync(ReleaseCandidate release, string assetName, int maximumBytes, CancellationToken cancellationToken = default)
    {
        if (AssetFailure is not null)
        {
            throw AssetFailure;
        }

        DownloadedAssets.Add(assetName);
        // The fake verifier decodes the version from these bytes.
        return Task.FromResult(Encoding.UTF8.GetBytes(assetName.EndsWith(".sig", StringComparison.Ordinal) ? "sig" : release.Version.Normalized));
    }

    public Task DownloadPackageAsync(ReleaseCandidate release, string assetName, Stream destination, long expectedBytes, IProgress<long>? progress, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public static ReleaseCandidate Release(string version, bool draft = false, bool withPackage = true, bool withManifest = true, bool withSignature = true, string? tag = null, long packageSize = 1000)
    {
        var parsed = SemanticVersion.Parse(version);
        var assets = new List<ReleaseAsset>();
        if (withPackage)
        {
            assets.Add(new ReleaseAsset(UpdateAssetNames.Package(parsed), packageSize));
        }

        if (withManifest)
        {
            assets.Add(new ReleaseAsset(UpdateAssetNames.Manifest(parsed), 2000));
        }

        if (withSignature)
        {
            assets.Add(new ReleaseAsset(UpdateAssetNames.Signature(parsed), 200));
        }

        return new ReleaseCandidate(tag ?? UpdateAssetNames.Tag(parsed), parsed, $"Release {version}", new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero), draft, assets);
    }
}

internal sealed class FakeManifestVerifier : IUpdateManifestVerifier
{
    public bool HasTrustedKeys { get; set; } = true;

    /// <summary>Versions whose signature should be treated as invalid.</summary>
    public HashSet<string> Invalid { get; } = [];

    /// <summary>Overrides the manifest produced for a version (e.g. a replayed manifest naming another version).</summary>
    public Dictionary<string, Func<UpdateManifest, UpdateManifest>> Tamper { get; } = [];

    public ManifestVerificationResult Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureBytes)
    {
        var version = Encoding.UTF8.GetString(manifestBytes);
        if (Invalid.Contains(version))
        {
            return ManifestVerificationResult.Rejected(ManifestRejection.InvalidSignature);
        }

        var manifest = UpdateFixtures.Manifest(version);
        if (Tamper.TryGetValue(version, out var change))
        {
            manifest = change(manifest);
        }

        return ManifestVerificationResult.Verified(manifest, "test-key");
    }
}

internal static class UpdateFixtures
{
    public static UpdateConfiguration Configuration(UpdateChannel channel = UpdateChannel.ReleaseCandidate)
    {
        Assert.True(UpdateRepository.TryParse("example-owner/example-repo", out var repository));
        return new UpdateConfiguration(repository, channel, [new TrustedUpdateKey("test-key", "unused-by-fake-verifier")]);
    }

    public static UpdateManifest Manifest(string versionText, long packageSize = 1000, string notes = "## Highlights\n- Faster saving")
    {
        var version = SemanticVersion.Parse(versionText);
        return new UpdateManifest(
            UpdateManifest.CurrentFormatVersion,
            UpdateManifest.CarowindsOperationsProductId,
            version,
            version.IsPrerelease ? UpdateChannel.ReleaseCandidate : UpdateChannel.Stable,
            UpdateManifest.WindowsX64RuntimeIdentifier,
            new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero),
            $"Release {versionText}",
            notes,
            new UpdatePackageDescriptor(UpdateAssetNames.Package(version), packageSize, new string('a', 64), UpdateAssetNames.ReleaseName(version)),
            [new UpdatePackageFile("Carowinds.Passdown.Desktop.exe", 10, new string('b', 64))]);
    }
}
