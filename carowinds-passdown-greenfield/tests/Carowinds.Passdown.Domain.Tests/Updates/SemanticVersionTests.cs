using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Domain.Tests.Updates;

/// <summary>Phase 23D: SemVer 2.0.0 parsing and precedence — never alphabetical order.</summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("0.1.0-rc.7", 0, 1, 0, true)]
    [InlineData("0.1.0", 0, 1, 0, false)]
    [InlineData("10.20.30", 10, 20, 30, false)]
    [InlineData("1.0.0-alpha.beta.1", 1, 0, 0, true)]
    [InlineData("1.0.0+build.5", 1, 0, 0, false)]
    public void Parse_AcceptsValidVersions(string text, int major, int minor, int patch, bool prerelease)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        Assert.Equal((major, minor, patch, prerelease), (version.Major, version.Minor, version.Patch, version.IsPrerelease));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v0.1.0")]
    [InlineData("0.1")]
    [InlineData("0.1.0.7")]
    [InlineData("01.1.0")]
    [InlineData("0.1.0-rc.07")]
    [InlineData("0.1.0-")]
    [InlineData("0.1.0-rc..7")]
    [InlineData(" 0.1.0")]
    [InlineData("0.1.0-rc 7")]
    [InlineData("0.1.0+")]
    [InlineData("99999999999.0.0")]
    public void Parse_RejectsInvalidVersions(string text) => Assert.False(SemanticVersion.TryParse(text, out _));

    [Fact]
    public void Parse_RejectsOverlongInput() => Assert.False(SemanticVersion.TryParse("1.0.0-" + new string('a', 200), out _));

    [Theory]
    [InlineData("0.1.0-rc.9", "0.1.0-rc.10")]
    [InlineData("0.1.0-rc.7", "0.1.0-rc.8")]
    [InlineData("0.1.0-rc.99", "0.1.0")]
    [InlineData("0.1.0", "0.1.1-rc.1")]
    [InlineData("0.1.0", "0.2.0")]
    [InlineData("0.9.0", "0.10.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    public void Precedence_FollowsSemVer(string lower, string higher)
    {
        var a = SemanticVersion.Parse(lower);
        var b = SemanticVersion.Parse(higher);
        Assert.True(a < b);
        Assert.True(b > a);
        Assert.Equal(-1, a.CompareTo(b));
    }

    [Fact]
    public void Precedence_IsNotAlphabetical()
    {
        var versions = new[] { "0.1.0-rc.10", "0.1.0-rc.9", "0.1.0", "0.1.0-rc.2" }.Select(SemanticVersion.Parse).OrderBy(v => v).Select(v => v.ToString());
        Assert.Equal(["0.1.0-rc.2", "0.1.0-rc.9", "0.1.0-rc.10", "0.1.0"], versions);
    }

    [Fact]
    public void BuildMetadata_IsIgnoredForEqualityButKeptForDisplay()
    {
        var a = SemanticVersion.Parse("0.1.0-rc.7+abc");
        var b = SemanticVersion.Parse("0.1.0-rc.7");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal("0.1.0-rc.7", a.Normalized);
        Assert.Equal("0.1.0-rc.7+abc", a.ToString());
    }
}
