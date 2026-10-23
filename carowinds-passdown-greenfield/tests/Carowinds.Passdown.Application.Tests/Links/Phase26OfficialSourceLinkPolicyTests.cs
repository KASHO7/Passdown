using Carowinds.Passdown.Application.Links;

namespace Carowinds.Passdown.Application.Tests.Links;

/// <summary>Phase 26: only official HTTPS Carowinds/Six Flags pages may ever be opened.</summary>
public class Phase26OfficialSourceLinkPolicyTests
{
    [Theory]
    [InlineData("https://www.sixflags.com/carowinds/memberships-and-season-passes")]
    [InlineData("https://sixflags.com/carowinds/memberships-and-season-passes")]
    [InlineData("https://WWW.SIXFLAGS.COM/carowinds/tickets?ref=1")]
    [InlineData("https://www.sixflags.com/carowinds")]
    [InlineData("https://www.carowinds.com/season-passes")]
    public void AcceptsOfficialHttpsPages(string address)
    {
        Assert.True(OfficialSourceLinkPolicy.TryValidate(new Uri(address), out var safe));
        Assert.Equal(Uri.UriSchemeHttps, safe.Scheme);
    }

    [Theory]
    [InlineData("http://www.sixflags.com/carowinds/memberships-and-season-passes")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://www.sixflags.com.evil.example/carowinds")]
    [InlineData("https://evil.example/carowinds/memberships-and-season-passes")]
    [InlineData("https://www.sixflags.com/magicmountain/season-passes")]
    [InlineData("https://www.sixflags.com/carowindsfake/page")]
    [InlineData("https://user:pass@www.sixflags.com/carowinds")]
    [InlineData("https://www.sixflags.com:8443/carowinds")]
    [InlineData("https://192.168.1.10/carowinds")]
    [InlineData("ftp://www.sixflags.com/carowinds")]
    [InlineData("mailto:someone@sixflags.com")]
    [InlineData("\\\\server\\share\\carowinds.html")]
    public void RejectsEverythingElse(string address)
    {
        var parsed = Uri.TryCreate(address, UriKind.Absolute, out var uri);
        Assert.False(parsed && OfficialSourceLinkPolicy.TryValidate(uri, out _));
    }

    [Fact]
    public void RejectsNullAndRelativeAddresses()
    {
        Assert.False(OfficialSourceLinkPolicy.TryValidate(null, out _));
        Assert.False(OfficialSourceLinkPolicy.TryValidate(new Uri("/carowinds/memberships-and-season-passes", UriKind.Relative), out _));
        Assert.False(OfficialSourceLinkPolicy.TryValidate(new Uri("C:\\Program Files\\app.exe"), out _));
    }

    [Fact]
    public void DisplayAddress_IsHostAndPath()
    {
        Assert.True(OfficialSourceLinkPolicy.TryValidate(new Uri("https://www.sixflags.com/carowinds/memberships-and-season-passes"), out var safe));
        Assert.Equal("www.sixflags.com/carowinds/memberships-and-season-passes", OfficialSourceLinkPolicy.DisplayAddress(safe));
    }
}
