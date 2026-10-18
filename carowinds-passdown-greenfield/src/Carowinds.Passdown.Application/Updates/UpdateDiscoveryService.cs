using Carowinds.Passdown.Domain.Updates;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Application.Updates;

/// <summary>
/// Phase 23D: finds the newest approved update. A release is approved only when all of these hold:
/// it is not a draft; its tag is "v" + a semantic version newer than the installed one; it carries the
/// package, manifest and signature assets under their exact expected names; the manifest's signature
/// verifies against a key bundled in this build; and the signed manifest names this product, win-x64, the
/// same version as the tag, the expected package asset, and a channel this installation accepts. Public
/// tags, GitHub's "Latest release" marker and its "prerelease" flag grant nothing.
/// Candidates are tried newest-first by semantic-version precedence (never string order); at most
/// <see cref="MaximumManifestsChecked"/> manifests are downloaded per check.
/// </summary>
public sealed class UpdateDiscoveryService(
    IUpdateReleaseSource source,
    IUpdateManifestVerifier verifier,
    UpdateConfiguration configuration,
    ILogger<UpdateDiscoveryService>? logger = null)
{
    /// <summary>Manifests and signatures are tiny; anything larger is not ours.</summary>
    public const int MaximumManifestBytes = 1024 * 1024;

    public const int MaximumSignatureBytes = 16 * 1024;

    /// <summary>Bounds the requests one check can make when several newer releases fail verification.</summary>
    public const int MaximumManifestsChecked = 3;

    public async Task<UpdateCheckResult> CheckAsync(SemanticVersion installedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);

        if (!configuration.IsConfigured || !verifier.HasTrustedKeys)
        {
            return UpdateCheckResult.NotConfigured();
        }

        IReadOnlyList<ReleaseCandidate> releases;
        try
        {
            releases = await source.ListReleasesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (UpdateSourceException ex)
        {
            logger?.LogInformation("Update check could not reach the release source ({Kind}).", ex.Kind);
            return UpdateCheckResult.Failed(ex.Kind, ex.RetryAfterUtc);
        }

        var candidates = releases
            .Where(release => !release.IsDraft)
            .Where(release => release.Tag == UpdateAssetNames.Tag(release.Version))
            .Where(release => UpdateChannelPolicy.Accepts(configuration.Channel, release.Version.IsPrerelease ? UpdateChannel.ReleaseCandidate : UpdateChannel.Stable))
            .Where(release => release.Version > installedVersion)
            .Where(HasRequiredAssets)
            .OrderByDescending(release => release.Version)
            .Take(MaximumManifestsChecked)
            .ToList();

        var rejected = 0;
        foreach (var release in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] manifestBytes;
            byte[] signatureBytes;
            try
            {
                manifestBytes = await source.DownloadSmallAssetAsync(release, UpdateAssetNames.Manifest(release.Version), MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
                signatureBytes = await source.DownloadSmallAssetAsync(release, UpdateAssetNames.Signature(release.Version), MaximumSignatureBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (UpdateSourceException ex)
            {
                logger?.LogInformation("Update check could not download release metadata ({Kind}).", ex.Kind);
                return UpdateCheckResult.Failed(ex.Kind, ex.RetryAfterUtc);
            }

            var verification = verifier.Verify(manifestBytes, signatureBytes);
            if (!verification.IsVerified)
            {
                rejected++;
                logger?.LogWarning("Release {Tag} was not accepted: {Rejection}.", release.Tag, verification.Rejection);
                continue;
            }

            var manifest = verification.Manifest!;
            var reason = CheckBinding(release, manifest, installedVersion);
            if (reason is not null)
            {
                rejected++;
                logger?.LogWarning("Release {Tag} was not accepted: {Reason}.", release.Tag, reason);
                continue;
            }

            return UpdateCheckResult.Available(new UpdateOffer(release, manifest, release.FindAsset(manifest.Package.FileName)!.Size));
        }

        return UpdateCheckResult.UpToDate(rejected);
    }

    private static bool HasRequiredAssets(ReleaseCandidate release) =>
        release.FindAsset(UpdateAssetNames.Package(release.Version)) is { Size: > 0 }
        && release.FindAsset(UpdateAssetNames.Manifest(release.Version)) is { Size: > 0 and <= MaximumManifestBytes }
        && release.FindAsset(UpdateAssetNames.Signature(release.Version)) is { Size: > 0 and <= MaximumSignatureBytes };

    /// <summary>The signed manifest must describe exactly the release it was found on (no replay of another version's manifest).</summary>
    private string? CheckBinding(ReleaseCandidate release, UpdateManifest manifest, SemanticVersion installedVersion)
    {
        if (!manifest.Version.Equals(release.Version) || manifest.Version.ToString() != release.Version.Normalized)
        {
            return "manifest version does not match the release tag";
        }

        if (manifest.Package.FileName != UpdateAssetNames.Package(manifest.Version)
            || manifest.Package.RootFolder != UpdateAssetNames.ReleaseName(manifest.Version))
        {
            return "manifest package name is not the expected name";
        }

        var asset = release.FindAsset(manifest.Package.FileName);
        if (asset is null || asset.Size != manifest.Package.Size)
        {
            return "package asset size does not match the signed manifest";
        }

        if (!UpdateChannelPolicy.IsInstallable(configuration.Channel, installedVersion, manifest.Channel, manifest.Version))
        {
            return "release channel or version is not installable here";
        }

        return null;
    }
}

public enum UpdateCheckStatus
{
    /// <summary>No trusted key or no repository: automatic updates are not set up in this build.</summary>
    NotConfigured,
    UpToDate,
    UpdateAvailable,
    Failed,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateOffer? Offer,
    UpdateSourceFailureKind? FailureKind,
    DateTimeOffset? RetryAfterUtc,
    int RejectedReleaseCount)
{
    public static UpdateCheckResult NotConfigured() => new(UpdateCheckStatus.NotConfigured, null, null, null, 0);

    public static UpdateCheckResult UpToDate(int rejected = 0) => new(UpdateCheckStatus.UpToDate, null, null, null, rejected);

    public static UpdateCheckResult Available(UpdateOffer offer) => new(UpdateCheckStatus.UpdateAvailable, offer, null, null, 0);

    public static UpdateCheckResult Failed(UpdateSourceFailureKind kind, DateTimeOffset? retryAfterUtc = null) => new(UpdateCheckStatus.Failed, null, kind, retryAfterUtc, 0);
}

/// <summary>Phase 23D: a verified, installable release — its listing plus its signed manifest.</summary>
public sealed record UpdateOffer(ReleaseCandidate Release, UpdateManifest Manifest, long DownloadBytes)
{
    public SemanticVersion Version => Manifest.Version;
}
