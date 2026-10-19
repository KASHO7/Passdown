using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Domain.Tests.Updates;

/// <summary>Phase 23D: the explicit release-channel policy.</summary>
public class UpdateChannelPolicyTests
{
    private static SemanticVersion V(string text) => SemanticVersion.Parse(text);

    [Theory]
    [InlineData(UpdateChannel.ReleaseCandidate, "0.1.0-rc.8", true)]
    [InlineData(UpdateChannel.ReleaseCandidate, "0.1.0-rc.10", true)]
    [InlineData(UpdateChannel.ReleaseCandidate, "0.1.0", false)]
    [InlineData(UpdateChannel.ReleaseCandidate, "0.1.0-beta.1", false)]
    [InlineData(UpdateChannel.ReleaseCandidate, "0.1.0-rc", false)]
    [InlineData(UpdateChannel.ReleaseCandidate, "0.1.0-rc.0", false)]
    [InlineData(UpdateChannel.ReleaseCandidate, "0.1.0-rc.8.1", false)]
    [InlineData(UpdateChannel.Stable, "0.1.0", true)]
    [InlineData(UpdateChannel.Stable, "0.1.0-rc.8", false)]
    public void Channel_MustMatchVersionShape(UpdateChannel channel, string version, bool expected) =>
        Assert.Equal(expected, UpdateChannelPolicy.IsConsistent(channel, V(version)));

    [Theory]
    [InlineData(UpdateChannel.ReleaseCandidate, UpdateChannel.ReleaseCandidate, true)]
    [InlineData(UpdateChannel.ReleaseCandidate, UpdateChannel.Stable, true)]
    [InlineData(UpdateChannel.Stable, UpdateChannel.Stable, true)]
    [InlineData(UpdateChannel.Stable, UpdateChannel.ReleaseCandidate, false)]
    public void StableInstallations_NeverAcceptPrereleases(UpdateChannel installed, UpdateChannel release, bool expected) =>
        Assert.Equal(expected, UpdateChannelPolicy.Accepts(installed, release));

    [Theory]
    [InlineData("0.1.0-rc.7", "0.1.0-rc.8", true)]
    [InlineData("0.1.0-rc.7", "0.1.0-rc.7", false)]
    [InlineData("0.1.0-rc.8", "0.1.0-rc.7", false)]
    [InlineData("0.1.0-rc.9", "0.1.0-rc.10", true)]
    public void IsInstallable_NeverDowngradesOrReinstalls(string installed, string candidate, bool expected) =>
        Assert.Equal(expected, UpdateChannelPolicy.IsInstallable(UpdateChannel.ReleaseCandidate, V(installed), UpdateChannel.ReleaseCandidate, V(candidate)));

    [Fact]
    public void ManifestNames_RoundTrip()
    {
        foreach (var channel in Enum.GetValues<UpdateChannel>())
        {
            Assert.True(UpdateChannelPolicy.TryParseManifestName(UpdateChannelPolicy.ToManifestName(channel), out var parsed));
            Assert.Equal(channel, parsed);
        }

        Assert.False(UpdateChannelPolicy.TryParseManifestName("Stable", out _));
        Assert.False(UpdateChannelPolicy.TryParseManifestName(null, out _));
    }
}
