using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Updates;

/// <summary>
/// Phase 23D: where releases are discovered and downloaded from — the provider boundary. GitHub Releases
/// is the only implementation; a SharePoint folder could implement the same three operations later.
/// A source only moves bytes: it never decides whether a release is approved (that is the signed manifest,
/// checked by <see cref="IUpdateManifestVerifier"/>) and never chooses where a download goes on disk.
/// Failures are reported as <see cref="UpdateSourceException"/> with a plain <see cref="UpdateSourceFailureKind"/>.
/// </summary>
public interface IUpdateReleaseSource
{
    /// <summary>Lists published releases (newest first is not assumed). Drafts may be included and are filtered by the caller.</summary>
    Task<IReadOnlyList<ReleaseCandidate>> ListReleasesAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads a small asset (manifest, signature) fully into memory, refusing anything larger than <paramref name="maximumBytes"/>.</summary>
    Task<byte[]> DownloadSmallAssetAsync(ReleaseCandidate release, string assetName, int maximumBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the package asset into <paramref name="destination"/>, refusing to write more than
    /// <paramref name="expectedBytes"/>. <paramref name="progress"/> receives the running byte count.
    /// </summary>
    Task DownloadPackageAsync(ReleaseCandidate release, string assetName, Stream destination, long expectedBytes, IProgress<long>? progress, CancellationToken cancellationToken = default);
}

/// <summary>Phase 23D: a release as the source lists it — untrusted until its signed manifest verifies.</summary>
public sealed record ReleaseCandidate(
    string Tag,
    SemanticVersion Version,
    string? Title,
    DateTimeOffset? PublishedAtUtc,
    bool IsDraft,
    IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? FindAsset(string name) => Assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.Ordinal));
}

public sealed record ReleaseAsset(string Name, long Size);

public enum UpdateSourceFailureKind
{
    /// <summary>No network, DNS failure, connection refused.</summary>
    Offline,

    /// <summary>The source did not answer (or stopped sending data) in time.</summary>
    Timeout,

    /// <summary>The source asked us to slow down (GitHub's unauthenticated limit is 60 requests per hour per network address).</summary>
    RateLimited,

    /// <summary>Any other unsuccessful HTTP status.</summary>
    HttpError,

    /// <summary>The response could not be understood, was too large, or redirected somewhere untrusted.</summary>
    InvalidResponse,
}

public sealed class UpdateSourceException(UpdateSourceFailureKind kind, string message, DateTimeOffset? retryAfterUtc = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public UpdateSourceFailureKind Kind { get; } = kind;

    /// <summary>For <see cref="UpdateSourceFailureKind.RateLimited"/>: when the source said it is safe to ask again, if it said.</summary>
    public DateTimeOffset? RetryAfterUtc { get; } = retryAfterUtc;
}
