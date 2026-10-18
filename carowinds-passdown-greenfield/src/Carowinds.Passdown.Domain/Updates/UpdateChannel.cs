namespace Carowinds.Passdown.Domain.Updates;

/// <summary>
/// Phase 23D: which signed releases an installation accepts. The channel is part of the build's own
/// configuration and is also declared inside every signed update manifest, so a release cannot be moved
/// to another channel without re-signing it.
/// </summary>
public enum UpdateChannel
{
    /// <summary>The current testing stage: signed release candidates ("-rc.N") and signed stable releases.</summary>
    ReleaseCandidate,

    /// <summary>Signed stable releases only; never a prerelease, whatever its version number.</summary>
    Stable,
}

/// <summary>Phase 23D: the explicit channel and version rules every update candidate must pass.</summary>
public static class UpdateChannelPolicy
{
    /// <summary>The manifest's channel name for <see cref="UpdateChannel.ReleaseCandidate"/>.</summary>
    public const string ReleaseCandidateName = "release-candidate";

    /// <summary>The manifest's channel name for <see cref="UpdateChannel.Stable"/>.</summary>
    public const string StableName = "stable";

    public static string ToManifestName(UpdateChannel channel) => channel switch
    {
        UpdateChannel.ReleaseCandidate => ReleaseCandidateName,
        UpdateChannel.Stable => StableName,
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public static bool TryParseManifestName(string? name, out UpdateChannel channel)
    {
        switch (name)
        {
            case ReleaseCandidateName:
                channel = UpdateChannel.ReleaseCandidate;
                return true;
            case StableName:
                channel = UpdateChannel.Stable;
                return true;
            default:
                channel = default;
                return false;
        }
    }

    /// <summary>
    /// A release's declared channel must match its version: a stable release has no prerelease part, and a
    /// release candidate's prerelease part is exactly "rc.N" (N a positive number). Anything else (alpha,
    /// beta, nightly, "rc" without a number) is not approved for any channel.
    /// </summary>
    public static bool IsConsistent(UpdateChannel releaseChannel, SemanticVersion version) => releaseChannel switch
    {
        UpdateChannel.Stable => !version.IsPrerelease,
        UpdateChannel.ReleaseCandidate => IsReleaseCandidateVersion(version),
        _ => false,
    };

    /// <summary>Whether an installation on <paramref name="installedChannel"/> may install a release declared for <paramref name="releaseChannel"/>.</summary>
    public static bool Accepts(UpdateChannel installedChannel, UpdateChannel releaseChannel) => installedChannel switch
    {
        UpdateChannel.ReleaseCandidate => releaseChannel is UpdateChannel.ReleaseCandidate or UpdateChannel.Stable,
        UpdateChannel.Stable => releaseChannel is UpdateChannel.Stable,
        _ => false,
    };

    /// <summary>
    /// The one place that decides whether a signed release may be offered: its channel is accepted, its
    /// version matches its channel, and it is strictly newer than what is installed (no downgrade, no
    /// reinstall of the same version).
    /// </summary>
    public static bool IsInstallable(UpdateChannel installedChannel, SemanticVersion installedVersion, UpdateChannel releaseChannel, SemanticVersion releaseVersion) =>
        Accepts(installedChannel, releaseChannel)
        && IsConsistent(releaseChannel, releaseVersion)
        && releaseVersion > installedVersion;

    private static bool IsReleaseCandidateVersion(SemanticVersion version) =>
        version.PrereleaseIdentifiers is [var label, var number]
        && label == "rc"
        && number.Length > 0
        && number.All(char.IsAsciiDigit)
        && number != "0";
}
